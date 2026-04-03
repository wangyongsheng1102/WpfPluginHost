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
            n.nspname as table_schema,
            c.relname as table_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @schema
          AND c.relkind = 'r'
        ORDER BY c.relname";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);

        var tableList = new List<(string Schema, string Table)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tableList.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (s, t) in tableList)
        {
            long rowCount = 0;
            try
            {
                var countSql = $"SELECT COUNT(*) FROM \"{s}\".\"{t}\"";
                await using var countCmd = new NpgsqlCommand(countSql, conn);
                rowCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
            }
            catch
            {
                rowCount = -1;
            }

            tables.Add(new TableInfo
            {
                SchemaName = s,
                TableName = t,
                RowCount = rowCount
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

            using var textReader = conn.BeginTextExport(copyCommand);
            var buffer = new char[65536];
            int charsRead;
            while ((charsRead = await textReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await streamWriter.WriteAsync(buffer, 0, charsRead);
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

            var quotedRef = GetQuotedTableRef(schemaName, tableName);
            var copySql = $"COPY {quotedRef} FROM STDIN WITH (" +
                          $"FORMAT CSV, " +
                          $"HEADER true, " +
                          $"DELIMITER ',', " +
                          $"QUOTE '\"', " +
                          $"ESCAPE '\"', " +
                          $"ENCODING 'UTF8'" +
                          $")";

            long importedRows = 0;
            var stopwatch = Stopwatch.StartNew();

            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                progress?.Report($"テーブル '{schemaName}.{tableName}' をクリアしています（全件削除後に CSV を取り込みます）...");
                await ClearTableForFullImportAsync(conn, tx, schemaName, tableName, quotedRef, progress);

                progress?.Report($"テーブル '{schemaName}.{tableName}' にデータをインポートしています...");

                await using (var writer = conn.BeginTextImport(copySql))
                {
                    using var fileStream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                    using var reader = new StreamReader(fileStream, Encoding.UTF8, bufferSize: 65536);

                    var buffer = new char[65536];
                    int charsRead;

                    while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await writer.WriteAsync(buffer, 0, charsRead);
                        importedRows += CountNewlines(buffer, charsRead);

                        if (importedRows % 10000 < 100)
                        {
                            progress?.Report($"約 {importedRows:N0} 行を処理しました...");
                        }
                    }
                }

                await tx.CommitAsync();

                stopwatch.Stop();
                progress?.Report($"テーブル '{schemaName}.{tableName}' のインポートが完了しました。処理時間: {stopwatch.Elapsed.TotalSeconds:F2} 秒");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                await tx.RollbackAsync();
                progress?.Report($"インポート中にテーブル '{schemaName}.{tableName}' が削除されました。");
                throw;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"テーブル '{schemaName}.{tableName}' のインポート中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 全量インポート前に対象テーブルを空にする。TRUNCATE を優先し、FK 等で失敗した場合のみ DELETE にフォールバックする。
    /// </summary>
    private static async Task ClearTableForFullImportAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string schemaName,
        string tableName,
        string quotedTableRef,
        IProgress<string>? progress)
    {
        try
        {
            await using var truncateCmd = new NpgsqlCommand(
                $"TRUNCATE TABLE {quotedTableRef} RESTART IDENTITY",
                conn,
                tx);
            await truncateCmd.ExecuteNonQueryAsync();
            progress?.Report($"テーブル '{schemaName}.{tableName}' を TRUNCATE しました。");
        }
        catch (PostgresException ex) when (IsTruncateBlockedByForeignKey(ex))
        {
            progress?.Report(
                $"TRUNCATE が使用できないため DELETE で全件削除します（{ex.SqlState}: {ex.Message}）");
            await using var deleteCmd = new NpgsqlCommand($"DELETE FROM {quotedTableRef}", conn, tx);
            var deleted = await deleteCmd.ExecuteNonQueryAsync();
            progress?.Report($"DELETE により {deleted} 行を削除しました。");
        }
    }

    private static bool IsTruncateBlockedByForeignKey(PostgresException ex)
    {
        if (ex.SqlState == "0A000")
        {
            return true;
        }

        var msg = ex.Message ?? string.Empty;
        return msg.Contains("foreign key", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("参照", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuotePgIdent(string name)
    {
        return "\"" + (name ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string GetQuotedTableRef(string schemaName, string tableName)
    {
        return $"{QuotePgIdent(schemaName)}.{QuotePgIdent(tableName)}";
    }

    private static long CountNewlines(char[] buffer, int length)
    {
        long count = 0;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] == '\n') count++;
        }
        return count;
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
