using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// CSV 比較（ロジックは WslPostgreTool CsvCompareService と同一）
/// </summary>
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

        progress?.Report((0, 0, $"CSV ファイルの比較を開始しています..."));

        bool useFullRowComparison = primaryKeyColumns == null || primaryKeyColumns.Count == 0;

        if (useFullRowComparison)
        {
            progress?.Report((0, 0, "主キーがないため、整行比較モードで実行します。"));
        }
        else
        {
            var pkList = primaryKeyColumns!;
            // 片方だけ整行・片方だけ主キーになるとキー構造が不一致になるため、両ファイルで主キー列が揃うときだけ主キーモード
            if (!PrimaryKeysResolvableInCsv(baseCsvPath, pkList) || !PrimaryKeysResolvableInCsv(compareCsvPath, pkList))
            {
                progress?.Report((0, 0,
                    "いずれかの CSV で主キー列が見出しと一致しないため、両ファイルとも整行比較モードで実行します。"));
                useFullRowComparison = true;
            }
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
        var baseFrozen = baseData.ToFrozenDictionary(comparer);
        var compareFrozen = compareData.ToFrozenDictionary(comparer);

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
            $"比較完了: 削除={results.Count(r => r.Status == ComparisonStatus.Deleted)}, " +
            $"追加={results.Count(r => r.Status == ComparisonStatus.Added)}, " +
            $"更新={results.Count(r => r.Status == ComparisonStatus.Updated)}"));

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

        // UTF-8 BOM が付くと先頭列名が一致しない
        var headers = ParseCsvLine(lines[0].TrimStart('\uFEFF'));
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
            var resolvedPkIndices = new HashSet<int>();
            foreach (var pk in primaryKeyColumns)
            {
                var idx = FindHeaderIndex(headers, pk);
                if (idx >= 0)
                    resolvedPkIndices.Add(idx);
            }

            primaryKeyIndices = Enumerable.Range(0, headers.Count)
                .Where(resolvedPkIndices.Contains)
                .ToList();

            if (resolvedPkIndices.Count == 0)
            {
                progress?.Report((0, 0, $"主キー列が見つかりません: {string.Join(", ", primaryKeyColumns)}"));
                return data;
            }

            nonPrimaryKeyIndices = Enumerable.Range(0, headers.Count)
                .Where(i => !resolvedPkIndices.Contains(i))
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
            if (useFullRowComparison)
            {
                foreach (var idx in primaryKeyIndices)
                {
                    var colName = headers[idx];
                    var value = idx < values.Count ? values[idx] : null;
                    primaryKeyValues[colName] = string.IsNullOrEmpty(value) ? null : value;
                }
            }
            else
            {
                // キーは DB の主キー列名に統一（各 CSV の見出しの大文字小文字が違っても同一行として突き合わせる）
                foreach (var pkCol in primaryKeyColumns)
                {
                    var idx = FindHeaderIndex(headers, pkCol);
                    if (idx < 0)
                        continue;
                    var value = idx < values.Count ? values[idx] : null;
                    primaryKeyValues[pkCol] = string.IsNullOrEmpty(value) ? null : value;
                }
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

    /// <summary>
    /// 複合主キーは構成列がすべて見出しに存在するときのみ true（CompareCsvFilesAsync で両 CSV を同じモードにするため）
    /// </summary>
    private static bool PrimaryKeysResolvableInCsv(string csvPath, List<string> primaryKeyColumns)
    {
        if (!File.Exists(csvPath) || primaryKeyColumns.Count == 0)
            return false;

        string? firstLine;
        using (var reader = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            firstLine = reader.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(firstLine))
            return false;

        var headers = ParseCsvLine(firstLine.TrimStart('\uFEFF'));
        return primaryKeyColumns.All(pk => FindHeaderIndex(headers, pk) >= 0);
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, string columnName)
    {
        var t = columnName.Trim();
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), t, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                if (inQuotes && current.Length > 0 && current[current.Length - 1] == '"')
                {
                    current.Length--;
                    current.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());

        return values;
    }

    private sealed class RowData
    {
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }
}
