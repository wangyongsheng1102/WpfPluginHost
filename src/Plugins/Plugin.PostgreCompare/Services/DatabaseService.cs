using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

public class DatabaseService
{
    public async Task<List<TableInfo>> GetTablesAsync(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        string? database = builder.ContainsKey("username") ? builder["username"]?.ToString() : null;

        var schema = "public";
        if (!string.IsNullOrEmpty(database) && database.StartsWith("cis", StringComparison.OrdinalIgnoreCase))
        {
            schema = "unisys";
        }

        var tables = new List<TableInfo>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
        SELECT 
            table_schema,
            table_name
        FROM information_schema.tables t
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
          AND table_type = 'BASE TABLE'
          AND table_schema = @schema
        ORDER BY table_schema, table_name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync();

        var tableList = new List<(string Schema, string Table)>();
        while (await reader.ReadAsync())
        {
            tableList.Add((reader.GetString(0), reader.GetString(1)));
        }

        await reader.CloseAsync();

        foreach (var (schemaName, tableName) in tableList)
        {
            try
            {
                var countSql = $"SELECT COUNT(*) FROM \"{schemaName}\".\"{tableName}\"";
                await using var countCmd = new NpgsqlCommand(countSql, conn);
                var rowCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

                tables.Add(new TableInfo
                {
                    SchemaName = schemaName,
                    TableName = tableName,
                    RowCount = rowCount
                });
            }
            catch
            {
                tables.Add(new TableInfo
                {
                    SchemaName = schemaName,
                    TableName = tableName,
                    RowCount = -1
                });
            }
        }

        return tables;
    }

    public async Task<string> GetTableCommentAsync(string connectionString, string schemaName, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT obj_description(c.oid, 'pg_class') as comment
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema
              AND c.relname = @table";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? tableName;
    }

    public async Task<Dictionary<string, string>> GetColumnCommentsAsync(string connectionString, string schemaName, string tableName)
    {
        var comments = new Dictionary<string, string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
        SELECT 
            c.column_name,
            COALESCE(pgd.description, '') as comment
        FROM information_schema.columns c
        JOIN pg_class pc ON pc.relname = c.table_name
        JOIN pg_namespace pn ON pn.oid = pc.relnamespace AND pn.nspname = c.table_schema
        LEFT JOIN pg_description pgd ON pgd.objoid = pc.oid 
            AND pgd.objsubid = c.ordinal_position
        WHERE c.table_schema = @schema
            AND c.table_name = @table
        ORDER BY c.ordinal_position";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(0);
            var comment = reader.IsDBNull(1) ? columnName : reader.GetString(1);
            comments[columnName] = string.IsNullOrEmpty(comment) ? columnName : comment;
        }

        return comments;
    }

    public async Task<List<string>> GetPrimaryKeyColumnsAsync(string connectionString, string schemaName, string tableName)
    {
        var columns = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT column_name
            FROM information_schema.key_column_usage
            WHERE table_schema = @schema
              AND table_name = @table
              AND constraint_name IN (
                  SELECT constraint_name
                  FROM information_schema.table_constraints
                  WHERE constraint_type = 'PRIMARY KEY'
                    AND table_schema = @schema
                    AND table_name = @table
              )
            ORDER BY ordinal_position";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    public async Task ExportTableToCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        progress?.Report($"テーブル '{schemaName}.{tableName}' をエクスポートしています...");

        try
        {
            var copyCommand = $"COPY {schemaName}.{tableName} " +
                              $"TO STDOUT WITH (" +
                              $"FORMAT CSV, " +
                              $"HEADER true, " +
                              $"QUOTE '\"', " +
                              $"FORCE_QUOTE *, " +
                              $"ESCAPE '\"', " +
                              $"ENCODING 'UTF8'" +
                              $")";

            await using var fileStream = File.Create(csvPath);
            await using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);

            using (var textWriter = conn.BeginTextExport(copyCommand))
            {
                string? line;
                while ((line = await textWriter.ReadLineAsync()) != null)
                {
                    await streamWriter.WriteLineAsync(line);
                }
            }

            progress?.Report($"テーブル '{schemaName}.{tableName}' のエクスポートが完了しました。");
        }
        catch (Exception ex)
        {
            progress?.Report($"エクスポート中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    public async Task ImportTableFromCsvAsync(string connectionString, string schemaName, string tableName, string csvPath, IProgress<string>? progress = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            progress?.Report($"テーブル '{schemaName}.{tableName}' の存在を確認しています...");

            var tableExists = await CheckTableExistsAsync(conn, schemaName, tableName);
            if (!tableExists)
            {
                progress?.Report($"テーブル '{schemaName}.{tableName}' は存在しません。インポートをスキップします。");
                return;
            }

            if (!File.Exists(csvPath))
            {
                progress?.Report($"CSVファイル '{csvPath}' が存在しません。");
                throw new FileNotFoundException($"CSVファイル '{csvPath}' が見つかりません。");
            }

            progress?.Report($"テーブル '{schemaName}.{tableName}' をクリアしています...");

            try
            {
                await using var truncateCmd = new NpgsqlCommand($"TRUNCATE TABLE {schemaName}.{tableName}", conn);
                await truncateCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                progress?.Report($"テーブルクリアに失敗しましたが続行します: {ex.Message}");
            }

            progress?.Report($"テーブル '{schemaName}.{tableName}' にデータをインポートしています...");

            var copySql = $"COPY {schemaName}.{tableName} FROM STDIN WITH (" +
                         $"FORMAT CSV, " +
                         $"HEADER true, " +
                         $"DELIMITER ',', " +
                         $"QUOTE '\"', " +
                         $"ESCAPE '\"', " +
                         $"ENCODING 'UTF8'" +
                         $")";

            long importedRows = 0;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await using (var writer = conn.BeginTextImport(copySql))
                {
                    using var fileStream = File.OpenRead(csvPath);
                    using var reader = new StreamReader(fileStream, Encoding.UTF8);

                    char[] buffer = new char[8192];
                    int charsRead;

                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await writer.WriteAsync(buffer, 0, charsRead);

                        importedRows += buffer.Count(c => c == '\n');
                        if (importedRows % 10000 == 0)
                        {
                            progress?.Report($"約 {importedRows:N0} 行を処理しました...");
                        }
                    }
                }

                stopwatch.Stop();
                progress?.Report($"テーブル '{schemaName}.{tableName}' のインポートが完了しました。処理時間: {stopwatch.Elapsed.TotalSeconds:F2} 秒");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                progress?.Report($"インポート中にテーブル '{schemaName}.{tableName}' が削除されました。");
                throw;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"テーブル '{schemaName}.{tableName}' のインポート中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    private async Task<bool> CheckTableExistsAsync(NpgsqlConnection conn, string schemaName, string tableName)
    {
        const string checkSql = @"
        SELECT EXISTS (
            SELECT 1 
            FROM information_schema.tables 
            WHERE table_schema = @schemaName 
            AND table_name = @tableName
        )";

        await using var checkCmd = new NpgsqlCommand(checkSql, conn);
        checkCmd.Parameters.AddWithValue("@schemaName", schemaName);
        checkCmd.Parameters.AddWithValue("@tableName", tableName);

        var result = await checkCmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value && Convert.ToBoolean(result);
    }
}

