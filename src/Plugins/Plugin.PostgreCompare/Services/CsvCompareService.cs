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

        var comparer = new PrimaryKeyComparer();
        var baseFrozen = baseData.ToDictionary(k => k.Key, v => v.Value, comparer);
        var compareFrozen = compareData.ToDictionary(k => k.Key, v => v.Value, comparer);

        foreach (var kvp in baseFrozen)
        {
            if (!compareFrozen.ContainsKey(kvp.Key))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Deleted,
                    PrimaryKeyValues = kvp.Key,
                    OldValues = kvp.Value.Values
                });
            }
        }

        foreach (var kvp in compareFrozen)
        {
            if (!baseFrozen.TryGetValue(kvp.Key, out var baseRow))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Added,
                    PrimaryKeyValues = kvp.Key,
                    NewValues = kvp.Value.Values
                });
            }
            else if (baseRow.Hash != kvp.Value.Hash)
            {
                results.Add(new RowComparisonResult
                {
                    TableName = $"{schemaName}.{tableName}",
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Key,
                    OldValues = baseRow.Values,
                    NewValues = kvp.Value.Values
                });
            }
        }

        progress?.Report((results.Count, results.Count,
            $"比較完了: 削除={results.Count(r => r.Status == ComparisonStatus.Deleted)}, 追加={results.Count(r => r.Status == ComparisonStatus.Added)}, 更新={results.Count(r => r.Status == ComparisonStatus.Updated)}"));

        return results;
    }

    private async Task<Dictionary<Dictionary<string, object?>, RowData>> LoadCsvDataWithHashAsync(
        string csvPath,
        List<string> primaryKeyColumns,
        IProgress<(int current, int total, string message)>? progress,
        string label,
        bool useFullRowComparison = false)
    {
        var data = new Dictionary<Dictionary<string, object?>, RowData>(new PrimaryKeyComparer());

        if (!File.Exists(csvPath))
        {
            progress?.Report((0, 0, $"ファイルが見つかりません: {csvPath}"));
            return data;
        }

        var lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return data;
        }

        var headers = ParseCsvLine(lines[0]);
        var totalRows = lines.Length - 1;
        var processedRows = 0L;

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

        var batch = new List<(Dictionary<string, object?> pk, Dictionary<string, object?> values, long hash)>();

        for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            var line = lines[rowIndex];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = ParseCsvLine(line);
            if (values.Count != headers.Count) continue;

            var primaryKeyValues = new Dictionary<string, object?>();
            foreach (var idx in primaryKeyIndices)
            {
                var colName = headers[idx];
                var value = idx < values.Count ? values[idx] : null;
                primaryKeyValues[colName] = string.IsNullOrEmpty(value) ? null : value;
            }

            var nonPkValues = new Dictionary<string, object?>();
            var hashBuilder = new StringBuilder();

            if (useFullRowComparison)
            {
                foreach (var idx in primaryKeyIndices)
                {
                    var colName = headers[idx];
                    var value = idx < values.Count ? values[idx] : null;
                    nonPkValues[colName] = string.IsNullOrEmpty(value) ? null : value;

                    hashBuilder.Append(colName).Append('=');
                    hashBuilder.Append(value ?? "NULL").Append('|');
                }
            }
            else
            {
                foreach (var idx in nonPrimaryKeyIndices)
                {
                    var colName = headers[idx];
                    var value = idx < values.Count ? values[idx] : null;
                    nonPkValues[colName] = string.IsNullOrEmpty(value) ? null : value;

                    hashBuilder.Append(colName).Append('=');
                    hashBuilder.Append(value ?? "NULL").Append('|');
                }
            }

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString()));
            var hash = BitConverter.ToInt64(hashBytes, 0);

            batch.Add((primaryKeyValues, nonPkValues, hash));
            processedRows++;

            if (batch.Count >= BatchSize)
            {
                ProcessBatch(batch, data);
                batch.Clear();

                var percentage = totalRows > 0 ? (int)(processedRows * 100 / totalRows) : 0;
                progress?.Report((percentage, 100, $"[{label}] {processedRows}/{totalRows} 行を処理しました ({percentage}%)"));
            }
        }

        if (batch.Count > 0)
        {
            ProcessBatch(batch, data);
        }

        return data;
    }

    private static void ProcessBatch(
        List<(Dictionary<string, object?> pk, Dictionary<string, object?> values, long hash)> batch,
        Dictionary<Dictionary<string, object?>, RowData> data)
    {
        foreach (var (pk, values, hash) in batch)
        {
            data[pk] = new RowData { Values = values, Hash = hash };
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
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }

    private sealed class PrimaryKeyComparer : IEqualityComparer<Dictionary<string, object?>>
    {
        public bool Equals(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
        {
            if (x == null || y == null) return x == y;
            if (x.Count != y.Count) return false;

            foreach (var kvp in x)
            {
                if (!y.TryGetValue(kvp.Key, out var value) || !Equals(kvp.Value, value))
                    return false;
            }

            return true;
        }

        public int GetHashCode(Dictionary<string, object?> obj)
        {
            var hash = new HashCode();
            foreach (var kvp in obj.OrderBy(k => k.Key))
            {
                hash.Add(kvp.Key);
                hash.Add(kvp.Value);
            }
            return hash.ToHashCode();
        }
    }
}

