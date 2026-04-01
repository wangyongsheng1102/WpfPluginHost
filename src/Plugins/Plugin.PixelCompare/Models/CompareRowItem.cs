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
    [NotifyPropertyChangedFor(nameof(DifferencePercentageText), nameof(DiffCountDisplay), nameof(HasNonZeroDiffCount))]
    private bool _isSizeMismatch;

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
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorMessage;

    public string DifferencePercentageText
    {
        get
        {
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

    /// <summary>サイズ不一致時は一覧上で差異数を ∞（深紅）で示す。</summary>
    public string DiffCountDisplay => IsSizeMismatch ? "\u221E" : DiffCount.ToString();

    /// <summary>ピクセル差異数が 0 より大きい（サイズ不一致は除く）。</summary>
    public bool HasNonZeroDiffCount => !IsSizeMismatch && DiffCount > 0;
}
