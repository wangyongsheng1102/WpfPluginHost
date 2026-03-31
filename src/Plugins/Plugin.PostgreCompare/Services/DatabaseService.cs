using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
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
}

