using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private const char KeySeparator = '\0';
    private const string NullMarker = "\x01NULL\x01";

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
            BufferSize = 65536,
        };
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

        var baseData = await LoadCsvDataAsync(
            baseCsvPath,
            primaryKeyColumns ?? new List<string>(),
            progress,
            "Base",
            useFullRowComparison);

        var compareData = await LoadCsvDataAsync(
            compareCsvPath,
            primaryKeyColumns ?? new List<string>(),
            progress,
            "Compare",
            useFullRowComparison);

        progress?.Report((0, 0, "データ比較を実行しています..."));

        var fullTableName = $"{schemaName}.{tableName}";
        var results = new List<RowComparisonResult>();

        foreach (var kvp in baseData)
        {
            if (!compareData.ContainsKey(kvp.Key))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Deleted,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    OldValues = kvp.Value.Values
                });
            }
        }

        foreach (var kvp in compareData)
        {
            if (!baseData.TryGetValue(kvp.Key, out var baseRow))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Added,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    NewValues = kvp.Value.Values
                });
            }
            else if (baseRow.Hash != kvp.Value.Hash)
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
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

    private async Task<Dictionary<string, CsvRowData>> LoadCsvDataAsync(
        string csvPath,
        List<string> primaryKeyColumns,
        IProgress<(int current, int total, string message)>? progress,
        string label,
        bool useFullRowComparison = false)
    {
        var data = new Dictionary<string, CsvRowData>(StringComparer.Ordinal);

        if (!File.Exists(csvPath))
        {
            progress?.Report((0, 0, $"ファイルが見つかりません: {csvPath}"));
            return data;
        }

        var config = CreateCsvConfiguration();

        await using var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);
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

        int[] pkIndices;
        int[] nonPkIndices;
        string[] pkColumnNames;

        if (useFullRowComparison)
        {
            pkIndices = Enumerable.Range(0, headers.Count).ToArray();
            nonPkIndices = Array.Empty<int>();
            pkColumnNames = headers.ToArray();
        }
        else
        {
            var resolvedPk = new List<(int idx, string name)>();
            foreach (var pk in primaryKeyColumns)
            {
                var idx = FindHeaderIndex(headers, pk);
                if (idx >= 0)
                    resolvedPk.Add((idx, pk));
            }

            if (resolvedPk.Count == 0)
            {
                progress?.Report((0, 0, $"主キー列が見つかりません: {string.Join(", ", primaryKeyColumns)}"));
                return data;
            }

            var pkIndexSet = new HashSet<int>(resolvedPk.Select(p => p.idx));
            pkIndices = resolvedPk.Select(p => p.idx).ToArray();
            pkColumnNames = resolvedPk.Select(p => p.name).ToArray();
            nonPkIndices = Enumerable.Range(0, headers.Count)
                .Where(i => !pkIndexSet.Contains(i))
                .ToArray();
        }

        long processedRows = 0;
        var headersArray = headers.ToArray();

        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headersArray.Length)
            {
                continue;
            }

            var compositeKey = BuildCompositeKey(record, pkIndices);

            var pkValues = new Dictionary<string, object?>(pkIndices.Length);
            for (var i = 0; i < pkIndices.Length; i++)
            {
                var raw = record[pkIndices[i]];
                pkValues[pkColumnNames[i]] = string.IsNullOrEmpty(raw) ? null : raw;
            }

            Dictionary<string, object?> nonPkValues;
            int hash;

            if (useFullRowComparison)
            {
                nonPkValues = pkValues;
                hash = ComputeRowHash(record, pkIndices, headersArray);
            }
            else
            {
                nonPkValues = new Dictionary<string, object?>(nonPkIndices.Length);
                foreach (var idx in nonPkIndices)
                {
                    var raw = record[idx];
                    nonPkValues[headersArray[idx]] = string.IsNullOrEmpty(raw) ? null : raw;
                }

                hash = ComputeRowHash(record, nonPkIndices, headersArray);
            }

            data[compositeKey] = new CsvRowData
            {
                PrimaryKeyValues = pkValues,
                Values = nonPkValues,
                Hash = hash
            };

            processedRows++;
            if (processedRows % 50000 == 0)
            {
                progress?.Report((0, 0, $"[{label}] {processedRows:N0} 行を処理しました"));
            }
        }

        progress?.Report((0, 0, $"[{label}] 合計 {processedRows:N0} 行をロードしました"));
        return data;
    }

    private static string BuildCompositeKey(string?[] record, int[] keyIndices)
    {
        if (keyIndices.Length == 1)
        {
            return record[keyIndices[0]] ?? NullMarker;
        }

        var totalLen = 0;
        for (var i = 0; i < keyIndices.Length; i++)
        {
            var val = record[keyIndices[i]];
            totalLen += (val?.Length ?? NullMarker.Length) + 1;
        }

        return string.Create(totalLen, (record, keyIndices), static (span, state) =>
        {
            var (rec, indices) = state;
            var pos = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                if (i > 0)
                {
                    span[pos++] = KeySeparator;
                }

                var val = rec[indices[i]];
                if (val == null)
                {
                    NullMarker.AsSpan().CopyTo(span.Slice(pos));
                    pos += NullMarker.Length;
                }
                else
                {
                    val.AsSpan().CopyTo(span.Slice(pos));
                    pos += val.Length;
                }
            }
        });
    }

    private static int ComputeRowHash(string?[] record, int[] indices, string[] headers)
    {
        var hc = new HashCode();
        for (var i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            hc.Add(headers[idx]);
            hc.Add(record[idx] ?? NullMarker);
        }

        return hc.ToHashCode();
    }

    private static async Task<bool> PrimaryKeysResolvableInCsvAsync(string csvPath, List<string> primaryKeyColumns)
    {
        if (!File.Exists(csvPath) || primaryKeyColumns.Count == 0)
            return false;

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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

    private sealed class CsvRowData
    {
        public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new();
        public Dictionary<string, object?> Values { get; set; } = new();
        public int Hash { get; set; }
    }
}
