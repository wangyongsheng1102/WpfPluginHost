namespace Plugin.Abstractions;

/// <summary>
/// ホスト・プラグイン共通のタイポグラフィ、レイアウト、アニメーション数値。
/// XAML では <c>x:Static</c>、コードビハインドでは直接参照する。
/// </summary>
public static class ThemeDimensions
{
    // ── Typography ──────────────────────────────────────
    public const double TitleFontSize = 28;
    public const double TitleCompactFontSize = 26;
    public const double SubtitleFontSize = 20;
    public const double BodyFontSize = 16;
    public const double CaptionFontSize = 15;
    public const double SmallFontSize = 13;
    public const double MicroFontSize = 12;
    public const double ButtonFontSize = 16;
    public const double DataGridHeaderFontSize = 15;
    public const double NavIconGlyphFontSize = 24;
    public const double CardIconGlyphFontSize = 32;
    public const double LineHeightDefault = 20;
    public const double LineHeightRelaxed = 22;

    // ── Window / Shell ──────────────────────────────────
    public const double MainWindowWidth = 1250;
    public const double MainWindowHeight = 900;
    public const double MainWindowMinWidth = 950;
    public const double MainWindowMinHeight = 700;
    public const double CaptionBarHeight = 52;
    public const double StatusBarHeight = 48;
    public const double ProgressBarHeight = 4;
    public const double ProgressBarAreaWidth = 150;

    public const double AuthorWindowWidth = 520;
    public const double AuthorWindowHeight = 620;

    // ── Navigation ──────────────────────────────────────
    public const double NavExpandedWidth = 260;
    public const double NavCollapsedWidth = 84;
    public const double NavItemHeight = 40;
    public const double NavIconMarginLeft = 22;
    public const double NavTextMarginLeft = 70;
    public const double NavIndicatorHeight = 16;
    public const double NavIndicatorWidth = 3;

    // ── Icon / Button ───────────────────────────────────
    public const double IconButtonSize = 40;
    public const double IconViewboxLarge = 28;
    public const double IconViewboxMedium = 26;
    public const double IconViewboxSmall = 22;
    public const double IconCanvasSize = 24;
    public const double HamburgerBarWidth = 16;
    public const double HamburgerBarHeight = 2;
    public const double HamburgerGridSize = 22;
    public const double HomeIconSize = 32;

    // ── Control Heights ─────────────────────────────────
    public const double ControlHeight = 36;
    public const double ControlHeightLarge = 40;
    public const double ControlHeightSmall = 30;
    public const double ComboBoxMinHeight = 34;
    public const double CheckBoxBulletSize = 18;
    public const double ComboBoxGlyphWidth = 9;
    public const double ComboBoxGlyphHeight = 5;

    // ── Card / Home ─────────────────────────────────────
    public const double PluginCardWidth = 300;
    public const double PluginCardMinHeight = 168;
    public const double PluginCardIconSize = 100;
    public const double PluginCardDescMaxHeight = 80;
    public const double AuthorAvatarSize = 44;
    public const double AuthorCloseButtonWidth = 140;
    public const double PluginCountMaxWidth = 248;

    // ── Spacing (4px grid) ──────────────────────────────
    public const double SpaceXxs = 2;
    public const double SpaceXs = 4;
    public const double SpaceSm = 8;
    public const double SpaceMd = 12;
    public const double SpaceLg = 16;
    public const double SpaceXl = 20;
    public const double SpaceXxl = 24;
    public const double SpaceXxxl = 30;
    public const double SpaceSection = 32;

    // ── Corner Radius ───────────────────────────────────
    public const double RadiusSmall = 4;
    public const double RadiusMedium = 8;
    public const double RadiusLarge = 10;

    // ── Stroke / Border ─────────────────────────────────
    public const double StrokeThin = 1.5;
    public const double StrokeMedium = 1.7;
    public const double StrokeThick = 2;

    // ── Shadow ──────────────────────────────────────────
    public const double ShadowBlurLevel1 = 14;
    public const double ShadowDepthLevel1 = 2;
    public const double ShadowOpacityLevel1 = 0.14;
    public const double ShadowBlurLevel2 = 24;
    public const double ShadowDepthLevel2 = 4;
    public const double ShadowOpacityLevel2 = 0.22;

    // ── Opacity ─────────────────────────────────────────
    public const double OpacitySubtle = 0.5;
    public const double OpacityHover = 0.85;
    public const double OpacityPressed = 0.88;
    public const double OpacityDisabled = 0.4;
    public const double OpacityDisabledStrong = 0.3;
    public const double OpacityDisabledCheckBox = 0.45;

    // ── Animation (ms) ──────────────────────────────────
    public const double AnimMenuExpandMs = 250;
    public const double AnimFadeQuickMs = 100;
    public const double AnimFadeNormalMs = 180;
    public const double AnimFadeSlowMs = 250;
    public const double AnimEntranceFadeMs = 500;
    public const double AnimEntranceSlideMs = 350;
    public const double AnimEntranceSlideOffset = 40;
    public const double AnimPressMs = 60;

    // ── Special (icon animation, etc.) ──────────────────
    public const double ShutdownWireDashLength = 96;
    public const double IconHoverScale = 1.08;
}
