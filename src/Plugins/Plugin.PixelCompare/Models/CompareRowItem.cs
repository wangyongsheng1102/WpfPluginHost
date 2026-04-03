using CommunityToolkit.Mvvm.ComponentModel;

namespace Plugin.PixelCompare.Models;

public partial class CompareRowItem : ObservableObject
{
    [ObservableProperty]
    private int _rowIndex;

    [ObservableProperty]
    private string _image1Path = string.Empty;

    [ObservableProperty]
    private string _image2Path = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffCountDisplay), nameof(HasNonZeroDiffCount))]
    private int _diffCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferencePercentageText))]
    private double _differencePercentage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferencePercentageText), nameof(DiffCountDisplay), nameof(HasNonZeroDiffCount), nameof(HasInfinityStyleDiffCount))]
    private bool _isSizeMismatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferencePercentageText), nameof(DiffCountDisplay), nameof(HasNonZeroDiffCount), nameof(HasInfinityStyleDiffCount))]
    private bool _isRowValidationError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferencePercentageText))]
    private string? _rowValidationMessageJa;

    [ObservableProperty]
    private string _sizeInfo = string.Empty;

    [ObservableProperty]
    private string? _differenceImagePath;

    [ObservableProperty]
    private string? _markedImage1Path;

    [ObservableProperty]
    private string? _markedImage2Path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isComparisonLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DifferencePercentageText))]
    private string? _errorMessage;

    public string DifferencePercentageText
    {
        get
        {
            if (IsRowValidationError && !string.IsNullOrWhiteSpace(RowValidationMessageJa))
            {
                return RowValidationMessageJa;
            }

            if (IsSizeMismatch)
            {
                return "サイズ不一致";
            }

            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                return "比較失敗";
            }

            return $"{DifferencePercentage:F2}%";
        }
    }

    public string StatusText
    {
        get
        {
            if (IsLoading)
            {
                return "比較中...";
            }

            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                return "失敗";
            }

            return IsComparisonLoaded ? "完了" : "待機中";
        }
    }

    /// <summary>サイズ不一致・行検証エラー時は一覧上で差異数を ∞（深紅）で示す。</summary>
    public string DiffCountDisplay => HasInfinityStyleDiffCount ? "\u221E" : DiffCount.ToString();

    /// <summary>∞ 表示と同じスタイル（深紅・強調）を当てる。</summary>
    public bool HasInfinityStyleDiffCount => IsSizeMismatch || IsRowValidationError;

    /// <summary>ピクセル差異数が 0 より大きい（サイズ不一致・行検証エラーは除く）。</summary>
    public bool HasNonZeroDiffCount => !IsSizeMismatch && !IsRowValidationError && DiffCount > 0;
}
