namespace Plugin.PixelCompare.Models;

/// <summary>
/// 1 行分の Excel 画像抽出結果。ピクセル比較可能な行と、検証エラー行の両方を表す。
/// </summary>
public sealed class PixelCompareRowExtract
{
    public required string SheetName { get; init; }
    public required int RowIndex { get; init; }

    /// <summary>左右列の画像のみでピクセル比較を実行できる場合 true。</summary>
    public required bool IsPixelComparable { get; init; }

    /// <summary>比較不可のときの差異率欄に表示する日本語メッセージ。</summary>
    public string? ValidationMessageJa { get; init; }

    public required string Image1Path { get; init; }
    public required string Image2Path { get; init; }
}
