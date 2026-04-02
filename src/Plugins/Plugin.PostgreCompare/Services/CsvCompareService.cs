using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

public class CsvCompareService
{
    private const int BatchSize = 10000;

    public async Task<List<RowComparisonResult>> CompareCsvFilesAsync(
        string baseCsvPath,
        string compareCsvPath,
        List<string>? primaryKeyColumns,
        string connectionString,
        string schemaName,
        string tableName,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var results = new List<RowComparisonResult>();

        progress?.Report((0, 0, "CSV ファイルの比較を開始しています..."));

        bool useFullRowComparison = primaryKeyColumns == null || primaryKeyColumns.Count == 0;

        if (useFullRowComparison)
        {
            progress?.Report((0, 0, "主キーがないため、整行比較モードで実行します。"));
        }

        var baseData = await LoadCsvDataWithHashAsync(
            baseCsvPath,
            primaryKeyColumns ?? new List<string>(),
            progress,
            "Base",
            useFullRowComparison);

        var compareData = await LoadCsvDataWithHashAsync(
            compareCsvPath,
            primaryKeyColumns ?? new List<string>(),
            progress,
            "Compare",
            useFullRowComparison);

        progress?.Report((0, 0, "データ比較を実行しています..."));

        // baseData にあって compareData にないものを「削除」とする
        foreach (var kvp in baseData)
        {
            if (!compareData.ContainsKey(kvp.Key))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Deleted,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    OldValues = kvp.Value.Values
                });
            }
        }

        // compareData にあって baseData にないものを「追加」
        // 両方にあるがハッシュが異なるものを「更新」とする
        foreach (var kvp in compareData)
        {
            if (!baseData.TryGetValue(kvp.Key, out var baseRow))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Added,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    NewValues = kvp.Value.Values
                });
            }
            else if (baseRow.Hash != kvp.Value.Hash)
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    OldValues = baseRow.Values,
                    NewValues = kvp.Value.Values
                });
            }
        }

        progress?.Report((results.Count, results.Count,
            $"比較完了: 削除={results.Count(r => r.Status == ComparisonStatus.Deleted)}, 追加={results.Count(r => r.Status == ComparisonStatus.Added)}, 更新={results.Count(r => r.Status == ComparisonStatus.Updated)}"));

        return results;
    }

    private async Task<Dictionary<string, RowData>> LoadCsvDataWithHashAsync(
        string csvPath,
        List<string> primaryKeyColumns,
        IProgress<(int current, int total, string message)>? progress,
        string label,
        bool useFullRowComparison = false)
    {
        var data = new Dictionary<string, RowData>();

        if (!File.Exists(csvPath))
        {
            progress?.Report((0, 0, $"ファイルが見つかりません: {csvPath}"));
            return data;
        }

        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) return data;

        var headers = ParseCsvLine(headerLine);
        
        // ファイルの行数を概算で見積もる（進捗表示用）
        var fileSize = new FileInfo(csvPath).Length;
        var processedBytes = 0L;
        var lastReportedPercentage = -1;

        List<int> primaryKeyIndices;
        List<int> nonPrimaryKeyIndices;

        if (useFullRowComparison)
        {
            primaryKeyIndices = Enumerable.Range(0, headers.Count).ToList();
            nonPrimaryKeyIndices = new List<int>();
        }
        else
        {
            primaryKeyIndices = primaryKeyColumns
                .Select(pk => headers.IndexOf(pk))
                .Where(idx => idx >= 0)
                .ToList();

            if (primaryKeyIndices.Count == 0)
            {
                progress?.Report((0, 0, $"主キー列が見つかりません: {string.Join(", ", primaryKeyColumns)}"));
                return data;
            }

            nonPrimaryKeyIndices = Enumerable.Range(0, headers.Count)
                .Where(i => !primaryKeyIndices.Contains(i))
                .ToList();
        }

        string? line;
        long processedRows = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            processedRows++;
            processedBytes += Encoding.UTF8.GetByteCount(line) + 2; // +2 for \r\n

            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = ParseCsvLine(line);
            if (values.Count != headers.Count) continue;

            var primaryKeyValues = new Dictionary<string, object?>();
            var pkKeyBuilder = new StringBuilder();
            foreach (var idx in primaryKeyIndices)
            {
                var colName = headers[idx];
                var value = values[idx];
                primaryKeyValues[colName] = string.IsNullOrEmpty(value) ? null : value;
                pkKeyBuilder.Append(value ?? "NULL").Append('\u001F'); // Use unit separator to avoid collision
            }

            var pkKey = pkKeyBuilder.ToString();
            var rowValues = new Dictionary<string, object?>();
            var hashBuilder = new StringBuilder();

            if (useFullRowComparison)
            {
                foreach (var idx in primaryKeyIndices)
                {
                    var colName = headers[idx];
                    var value = values[idx];
                    rowValues[colName] = string.IsNullOrEmpty(value) ? null : value;

                    hashBuilder.Append(colName).Append('=').Append(value ?? "NULL").Append('|');
                }
            }
            else
            {
                foreach (var idx in nonPrimaryKeyIndices)
                {
                    var colName = headers[idx];
                    var value = values[idx];
                    rowValues[colName] = string.IsNullOrEmpty(value) ? null : value;

                    hashBuilder.Append(colName).Append('=').Append(value ?? "NULL").Append('|');
                }
            }

            // Using a simple but effective hash for performance
            var rowHash = GetStableHashCode(hashBuilder.ToString());
            
            data[pkKey] = new RowData 
            { 
                PrimaryKeyValues = primaryKeyValues,
                Values = rowValues, 
                Hash = rowHash 
            };

            if (processedRows % BatchSize == 0)
            {
                var percentage = (int)(processedBytes * 100 / fileSize);
                if (percentage != lastReportedPercentage)
                {
                    progress?.Report((percentage, 100, $"[{label}] {processedRows} 行を処理しました ({percentage}%)"));
                    lastReportedPercentage = percentage;
                }
            }
        }

        return data;
    }

    private static long GetStableHashCode(string str)
    {
        unchecked
        {
            long hash1 = 5381;
            long hash2 = hash1;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i + 1 == str.Length || str[i + 1] == '\0')
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    current.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private sealed class RowData
    {
        public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new();
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }
}

