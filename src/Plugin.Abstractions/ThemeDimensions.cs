namespace Plugin.Abstractions;

/// <summary>
/// ホスト・プラグイン共通のテーマ数値。XAML の <c>clr-namespace:System;assembly=*</c> や
/// ホスト専用の <c>StaticResource BodyFontSize</c> に依存せず、<c>x:Static</c> で参照する。
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
