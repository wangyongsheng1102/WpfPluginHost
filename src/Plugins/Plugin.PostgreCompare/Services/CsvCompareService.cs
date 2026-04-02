using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// CSV 比較。参照 WslPostgreTool CsvCompareService：CsvHelper でレコード単位読込（フィールド内改行に対応）。
/// 主キー見出しは DB 名と CSV の大文字小文字差を FindHeaderIndex で吸収し、主キー辞書のキーは DB 列名に統一。
/// </summary>
public class CsvCompareService
{
    private const int BatchSize = 10000;

    private static CsvConfiguration CreateCsvConfiguration()
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = false,
            Delimiter = ",",
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.None,
            BadDataFound = null,
            MissingFieldFound = null,
        };
    }

    private static async Task<long> CountCsvDataRecordsAsync(string csvPath)
    {
        var config = CreateCsvConfiguration();

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return 0;
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        long count = 0;
        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headers.Length)
            {
                continue;
            }

            count++;
        }

        return count;
    }

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
            if (!await PrimaryKeysResolvableInCsvAsync(baseCsvPath, pkList) ||
                !await PrimaryKeysResolvableInCsvAsync(compareCsvPath, pkList))
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

        var config = CreateCsvConfiguration();
        var totalRows = await CountCsvDataRecordsAsync(csvPath);
        var processedRows = 0L;

        List<int>? primaryKeyIndices = null;
        List<int> nonPrimaryKeyIndices;

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return data;
        }

        csv.ReadHeader();
        var headers = (csv.HeaderRecord ?? Array.Empty<string>()).ToList();

        if (headers.Count == 0)
        {
            return data;
        }

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

        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headers.Count)
            {
                continue;
            }

            var primaryKeyValues = new Dictionary<string, object?>();
            if (useFullRowComparison)
            {
                foreach (var idx in primaryKeyIndices!)
                {
                    var colName = headers[idx];
                    var rawValue = csv.GetField<string>(idx);
                    primaryKeyValues[colName] = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                }
            }
            else
            {
                foreach (var pkCol in primaryKeyColumns)
                {
                    var idx = FindHeaderIndex(headers, pkCol);
                    if (idx < 0)
                        continue;
                    var rawValue = csv.GetField<string>(idx);
                    primaryKeyValues[pkCol] = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                }
            }

            var nonPkValues = new Dictionary<string, object?>();
            var hashBuilder = new StringBuilder();

            if (useFullRowComparison)
            {
                foreach (var idx in primaryKeyIndices!)
                {
                    var colName = headers[idx];
                    var rawValue = csv.GetField<string>(idx);
                    var value = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                    nonPkValues[colName] = value;

                    hashBuilder.Append(colName).Append('=');
                    hashBuilder.Append(value ?? "NULL").Append('|');
                }
            }
            else
            {
                foreach (var idx in nonPrimaryKeyIndices)
                {
                    var colName = headers[idx];
                    var rawValue = csv.GetField<string>(idx);
                    var value = string.IsNullOrEmpty(rawValue) ? null : rawValue;
                    nonPkValues[colName] = value;

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

    private static async Task<bool> PrimaryKeysResolvableInCsvAsync(string csvPath, List<string> primaryKeyColumns)
    {
        if (!File.Exists(csvPath) || primaryKeyColumns.Count == 0)
            return false;

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());

        if (!await csv.ReadAsync())
            return false;

        csv.ReadHeader();
        var headers = (csv.HeaderRecord ?? Array.Empty<string>()).ToList();
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

    private sealed class RowData
    {
        public Dictionary<string, object?> Values { get; set; } = new();
        public long Hash { get; set; }
    }
}
