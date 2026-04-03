namespace ShellApp;

/// <summary>
/// テーマ用数値（XAML の clr-namespace:System;assembly=* を避け、起動時の Assembly 解決失敗を防ぐ）
/// </summary>
public static class ThemeDimensions
{
    public const double TitleFontSize = 28;
    public const double SubtitleFontSize = 20;
    public const double BodyFontSize = 16;
    public const double CaptionFontSize = 15;
    public const double ShutdownWireDashLength = 96;
    public const double IconHoverScale = 1.08;
}
