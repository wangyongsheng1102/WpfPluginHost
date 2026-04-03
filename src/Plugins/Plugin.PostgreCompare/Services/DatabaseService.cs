using System;
using System.Collections.Generic;
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
    public async Task<List<string>> GetSchemasAsync(string connectionString)
    {
        var schemas = new List<string>();
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT nspname 
                FROM pg_namespace 
                WHERE nspname NOT LIKE 'pg_%' 
                  AND nspname != 'information_schema'
                ORDER BY nspname";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                schemas.Add(reader.GetString(0));
            }
        }
        catch
        {
            schemas.Add("public");
            schemas.Add("unisys");
        }
        return schemas;
    }

    public async Task<List<TableInfo>> GetTablesAsync(string connectionString, string schemaName)
    {
        var tables = new List<TableInfo>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
        SELECT 
            n.nspname  AS table_schema,
            c.relname  AS table_name,
            c.reltuples::bigint AS approx_rows
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @schema
          AND c.relkind = 'r'
        ORDER BY c.relname";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new TableInfo
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                RowCount = reader.GetInt64(2)
            });
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

            await using var fileStream = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
            await using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8, 65536);

            await using var pgReader = await conn.BeginRawBinaryCopyAsync($"COPY {schemaName}.{tableName} TO STDOUT WITH (FORMAT CSV, HEADER true, QUOTE '\"', FORCE_QUOTE *, ESCAPE '\"', ENCODING 'UTF8')");

            var buffer = new byte[65536];
            int bytesRead;
            while ((bytesRead = await pgReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
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

            var stopwatch = Stopwatch.StartNew();

            await using var fileStream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);

            try
            {
                await using var writer = await conn.BeginRawBinaryCopyAsync(
                    $"COPY {schemaName}.{tableName} FROM STDIN WITH (FORMAT CSV, HEADER true, DELIMITER ',', QUOTE '\"', ESCAPE '\"', ENCODING 'UTF8')");

                var buffer = new byte[65536];
                int bytesRead;
                long totalBytes = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await writer.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;

                    if (totalBytes % (1024 * 1024) < 65536)
                    {
                        progress?.Report($"約 {totalBytes / 1024:N0} KB を送信しました...");
                    }
                }

                await writer.DisposeAsync();

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
