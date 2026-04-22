using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShellApp.ViewModels;

public sealed class ReleaseNoteItem
{
    public required string VersionTitle { get; init; }
    public required string DateLabel { get; init; }
    public required string Notes { get; init; }
    public bool IsHighlight { get; init; }
}

public partial class AuthorWindowViewModel : ObservableObject
{
    private readonly System.Action _closeWindow;

    public string WindowTitle => "開発者情報 & アップデート履歴";

    public string TitleBarText => "開発者情報";

    public string ProductName => "WPF Plugin Shell";

    public string VersionDisplay => "Version 1.5.0";

    public string ProfileGlyph => "👤";

    public string DeveloperName => "wangys";

    public string DeveloperTeam => "oneman Team";

    public string ReleaseNotesHeading => "アップデート履歴 (Release Notes)";

    public string CloseButtonText => "閉じる";

    public ObservableCollection<ReleaseNoteItem> ReleaseNotes { get; }

    public AuthorWindowViewModel(System.Action closeWindow)
    {
        _closeWindow = closeWindow;
        ReleaseNotes = new ObservableCollection<ReleaseNoteItem>(BuildReleaseNotes());
    }

    private static IEnumerable<ReleaseNoteItem> BuildReleaseNotes()
    {
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.5.0 - 単一ファイル配布 & InputRecorder MVVM 化",
            DateLabel = "2026/04",
            IsHighlight = true,
            Notes =
                "• dotnet publish で単一ファイル＋圧縮＋ネイティブ同梱＋PDB 除外の配布構成を追加\n" +
                "• InputRecorder プラグインを MVVM パターンに全面リファクタリング\n" +
                "• データモデル（InputEvent）と表示モデル（InputEventDisplay）を分離\n" +
                "• 長図スクロール検索を Parallel.For で並列化\n" +
                "• 閉じるボタンを switch-on/off SVG トグルアニメーションに刷新"
        };
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.4.0 - About ボタン演出 & バグ修正",
            DateLabel = "2026/04",
            IsHighlight = false,
            Notes =
                "• About ボタン：ホバー時に回転＋パルス＋グラデーション発光リング＋弾性プレスの新アニメーション\n" +
                "• PostgreCompare エクスポート/インポートの COPY 形式不一致エラーを修正\n" +
                "• XAML の GridLength / CornerRadius 型不一致による起動時クラッシュを修正"
        };
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.3.0 - UI 統一管理 & テーマ基盤強化",
            DateLabel = "2026/04",
            IsHighlight = false,
            Notes =
                "• ThemeDimensions を 100+ 定数に拡張し、UI 数値の一元管理を実現\n" +
                "• フォント・ウィンドウサイズ・ナビ幅・アイコンサイズ・余白・角丸・影・透過度・アニメーション時間をすべて定数化\n" +
                "• MainWindow / AuthorWindow / HomeView のハードコード値を x:Static 参照に統一\n" +
                "• WPF の GridLength / CornerRadius / Thickness 型制約に対応した設計ガイドラインを確立"
        };
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.2.0 - ナビゲーションメニュー・アニメーション改善",
            DateLabel = "2026/04",
            IsHighlight = false,
            Notes =
                "• 左メニューの展開・収縮アニメーションを全面刷新（QuarticEase → CubicEase EaseOut）\n" +
                "• 不要な横シフト・透明度フラッシュアニメーションを除去\n" +
                "• メニュー項目テキストのフェード・イン/アウトにイージングと遅延を追加\n" +
                "• 下部パネル（リロード・テーマ切替・プラグイン数）の展開時の跳動を解消\n" +
                "• NavPanel に ClipToBounds を追加し、幅アニメーション中のオーバーフローを防止\n" +
                "• プラグイン数テキストの折り返しによるレイアウト突変を解消（NoWrap + Ellipsis）"
        };
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.1.0 - パフォーマンス最適化",
            DateLabel = "2026/04",
            IsHighlight = false,
            Notes =
                "• PixelCompare: 2パス画像走査を1パスに統合、差分検出と可視化を同時実行\n" +
                "• PixelCompare: diffMap を 2D bool[,] から 1D byte[] に変更（キャッシュ効率向上）\n" +
                "• PixelCompare: ExpandDiffMap を可分離ボックスフィルタ化（O(N×expand²) → O(N)）\n" +
                "• PixelCompare: BFS を線形インデックス化し、タプル割当を排除\n" +
                "• PixelCompare: Clone() 廃止・PNG BestSpeed 圧縮・3画像並列保存\n" +
                "• PostgreCompare: CSV 事前スキャン廃止（行数を読込中にカウント）\n" +
                "• PostgreCompare: SHA256 → System.HashCode（非暗号用途で約10倍高速化）\n" +
                "• PostgreCompare: 辞書型主キー → 文字列複合キー（O(n log n) → O(k) ハッシュ）\n" +
                "• PostgreCompare: DB インポート/エクスポートを Raw Binary Copy + 64KB バッファに変更"
        };
        yield return new ReleaseNoteItem
        {
            VersionTitle = "v1.0.0 - 初期リリース",
            DateLabel = "2026/03",
            IsHighlight = false,
            Notes =
                "• WPF Plugin ベースシェルの基本フレームワークを確立\n" +
                "• SampleA / SampleB プラグインモジュールの基礎設計"
        };
    }

    [RelayCommand]
    private void Close() => _closeWindow();
}
