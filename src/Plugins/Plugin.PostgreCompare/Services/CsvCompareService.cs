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
    private const int CsvBufferSize = 262144;
    private const char KeySeparator = '\0';
    private const string NullMarker = "\x01NULL\x01";
    private CsvLoadCacheEntry? _lastLoadCache;
    private CsvLoadCacheEntry? _previousLoadCache;

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
            BufferSize = CsvBufferSize,
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

        var normalizedPrimaryKeys = primaryKeyColumns?
            .Where(pk => !string.IsNullOrWhiteSpace(pk))
            .ToArray() ?? Array.Empty<string>();

        bool useFullRowComparison = normalizedPrimaryKeys.Length == 0;

        if (useFullRowComparison)
        {
            progress?.Report((0, 0, "主キーがないため、整行比較モードで実行します。"));
        }
        else
        {
            var baseHeaders = await ReadHeadersAsync(baseCsvPath);
            var compareHeaders = await ReadHeadersAsync(compareCsvPath);
            var missingBaseKeys = GetMissingPrimaryKeys(baseHeaders, normalizedPrimaryKeys);
            var missingCompareKeys = GetMissingPrimaryKeys(compareHeaders, normalizedPrimaryKeys);

            if (missingBaseKeys.Count > 0 || missingCompareKeys.Count > 0)
            {
                var baseMessage = missingBaseKeys.Count > 0
                    ? $"Base 不足: {string.Join(", ", missingBaseKeys)}"
                    : "Base OK";
                var compareMessage = missingCompareKeys.Count > 0
                    ? $"Compare 不足: {string.Join(", ", missingCompareKeys)}"
                    : "Compare OK";

                progress?.Report((0, 0,
                    $"主キー列を CSV 見出しに解決できないため、整行比較モードで実行します。({baseMessage}; {compareMessage})"));
                useFullRowComparison = true;
            }
        }

        var baseData = await LoadCsvDataAsync(
            baseCsvPath,
            normalizedPrimaryKeys,
            progress,
            "Base",
            useFullRowComparison);

        var compareData = await LoadCsvDataAsync(
            compareCsvPath,
            normalizedPrimaryKeys,
            progress,
            "Compare",
            useFullRowComparison);

        progress?.Report((0, 0, "データ比較を実行しています..."));

        var fullTableName = $"{schemaName}.{tableName}";
        var results = new List<RowComparisonResult>();

        foreach (var kvp in baseData)
        {
            if (!compareData.TryGetValue(kvp.Key, out var compareRow))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Deleted,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    OldValues = kvp.Value.Values
                });
            }
            else if (kvp.Value.Hash != compareRow.Hash)
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Updated,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
                    OldValues = kvp.Value.Values,
                    NewValues = compareRow.Values
                });
            }
        }

        foreach (var kvp in compareData)
        {
            if (!baseData.ContainsKey(kvp.Key))
            {
                results.Add(new RowComparisonResult
                {
                    TableName = fullTableName,
                    Status = ComparisonStatus.Added,
                    PrimaryKeyValues = kvp.Value.PrimaryKeyValues,
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
        IReadOnlyList<string> primaryKeyColumns,
        IProgress<(int current, int total, string message)>? progress,
        string label,
        bool useFullRowComparison = false)
    {
        if (TryGetCachedLoad(csvPath, primaryKeyColumns, useFullRowComparison, out var cached))
        {
            progress?.Report((0, 0, $"[{label}] キャッシュ済みデータを再利用しました"));
            return cached;
        }

        var data = new Dictionary<string, CsvRowData>(StringComparer.Ordinal);

        if (!File.Exists(csvPath))
        {
            progress?.Report((0, 0, $"ファイルが見つかりません: {csvPath}"));
            return data;
        }

        var config = CreateCsvConfiguration();

        await using var stream = new FileStream(
            csvPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CsvBufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var reader = new StreamReader(
            stream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: CsvBufferSize);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return data;
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        if (headers.Length == 0)
        {
            return data;
        }

        int[] pkIndices;
        int[] nonPkIndices;
        string[] pkColumnNames;

        if (useFullRowComparison)
        {
            pkIndices = Enumerable.Range(0, headers.Length).ToArray();
            nonPkIndices = Array.Empty<int>();
            pkColumnNames = headers;
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

            pkIndices = resolvedPk.Select(p => p.idx).ToArray();
            pkColumnNames = resolvedPk.Select(p => p.name).ToArray();

            var pkIndexSet = new bool[headers.Length];
            foreach (var idx in pkIndices)
            {
                pkIndexSet[idx] = true;
            }

            nonPkIndices = new int[headers.Length - pkIndices.Length];
            var sourceIndex = 0;
            var targetIndex = 0;
            for (; sourceIndex < headers.Length; sourceIndex++)
            {
                if (!pkIndexSet[sourceIndex])
                {
                    nonPkIndices[targetIndex++] = sourceIndex;
                }
            }
        }

        long processedRows = 0;
        var headerHashes = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            headerHashes[i] = StringComparer.Ordinal.GetHashCode(headers[i]);
        }

        while (await csv.ReadAsync())
        {
            var record = csv.Parser.Record;
            if (record == null || record.Length != headers.Length)
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
                hash = ComputeRowHash(record, pkIndices, headerHashes);
            }
            else
            {
                nonPkValues = new Dictionary<string, object?>(nonPkIndices.Length);
                foreach (var idx in nonPkIndices)
                {
                    var raw = record[idx];
                    nonPkValues[headers[idx]] = string.IsNullOrEmpty(raw) ? null : raw;
                }

                hash = ComputeRowHash(record, nonPkIndices, headerHashes);
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
        SetCachedLoad(csvPath, primaryKeyColumns, useFullRowComparison, data);
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

    private static int ComputeRowHash(string?[] record, int[] indices, int[] headerHashes)
    {
        var hc = new HashCode();
        for (var i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            hc.Add(headerHashes[idx]);
            hc.Add(record[idx] ?? NullMarker);
        }

        return hc.ToHashCode();
    }

    private static async Task<string[]> ReadHeadersAsync(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            return Array.Empty<string>();
        }

        await using var stream = new FileStream(
            csvPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CsvBufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var reader = new StreamReader(
            stream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: CsvBufferSize);
        using var csv = new CsvReader(reader, CreateCsvConfiguration());

        if (!await csv.ReadAsync())
        {
            return Array.Empty<string>();
        }

        csv.ReadHeader();
        return csv.HeaderRecord ?? Array.Empty<string>();
    }

    private static List<string> GetMissingPrimaryKeys(IReadOnlyList<string> headers, IReadOnlyList<string> primaryKeyColumns)
    {
        var missingKeys = new List<string>();
        if (headers.Count == 0 || primaryKeyColumns.Count == 0)
        {
            missingKeys.AddRange(primaryKeyColumns);
            return missingKeys;
        }

        for (var i = 0; i < primaryKeyColumns.Count; i++)
        {
            if (FindHeaderIndex(headers, primaryKeyColumns[i]) < 0)
            {
                missingKeys.Add(primaryKeyColumns[i]);
            }
        }

        return missingKeys;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, string columnName)
    {
        var t = NormalizeHeaderToken(columnName);
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(NormalizeHeaderToken(headers[i]), t, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string NormalizeHeaderToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Trim('\uFEFF');
        if (normalized.Length >= 2 &&
            normalized[0] == '"' &&
            normalized[^1] == '"')
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    private sealed class CsvRowData
    {
        public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new();
        public Dictionary<string, object?> Values { get; set; } = new();
        public int Hash { get; set; }
    }

    private bool TryGetCachedLoad(
        string csvPath,
        IReadOnlyList<string> primaryKeyColumns,
        bool useFullRowComparison,
        out Dictionary<string, CsvRowData> data)
    {
        if (IsCacheMatch(_lastLoadCache, csvPath, primaryKeyColumns, useFullRowComparison))
        {
            data = _lastLoadCache!.Data;
            return true;
        }

        if (IsCacheMatch(_previousLoadCache, csvPath, primaryKeyColumns, useFullRowComparison))
        {
            (_lastLoadCache, _previousLoadCache) = (_previousLoadCache, _lastLoadCache);
            data = _lastLoadCache!.Data;
            return true;
        }

        data = null!;
        return false;
    }

    private void SetCachedLoad(
        string csvPath,
        IReadOnlyList<string> primaryKeyColumns,
        bool useFullRowComparison,
        Dictionary<string, CsvRowData> data)
    {
        var cacheEntry = new CsvLoadCacheEntry(csvPath, BuildPrimaryKeySignature(primaryKeyColumns), useFullRowComparison, data);
        _previousLoadCache = _lastLoadCache;
        _lastLoadCache = cacheEntry;
    }

    private static bool IsCacheMatch(
        CsvLoadCacheEntry? entry,
        string csvPath,
        IReadOnlyList<string> primaryKeyColumns,
        bool useFullRowComparison)
    {
        return entry != null &&
               string.Equals(entry.CsvPath, csvPath, StringComparison.Ordinal) &&
               entry.UseFullRowComparison == useFullRowComparison &&
               string.Equals(entry.PrimaryKeySignature, BuildPrimaryKeySignature(primaryKeyColumns), StringComparison.Ordinal);
    }

    private static string BuildPrimaryKeySignature(IReadOnlyList<string> primaryKeyColumns)
    {
        if (primaryKeyColumns.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(KeySeparator, primaryKeyColumns);
    }

    private sealed record CsvLoadCacheEntry(
        string CsvPath,
        string PrimaryKeySignature,
        bool UseFullRowComparison,
        Dictionary<string, CsvRowData> Data);
}
