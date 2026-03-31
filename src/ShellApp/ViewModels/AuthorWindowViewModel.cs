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

    public string VersionDisplay => "Version 1.0.0";

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
        // yield return new ReleaseNoteItem
        // {
        //     VersionTitle = "v1.2.0 - プレシジョン・アップデート",
        //     DateLabel = "2026/03",
        //     IsHighlight = true,
        //     Notes =
        //         "• Windows 11 Fluent Design（アクリル背景・角丸・シャドウ）を全体設計に導入\n" +
        //         "• SampleA: ドラッグ＆ドロップ対応のバッチ処理キューシステムへ完全にアップグレード\n" +
        //         "• SampleA: 操作性を向上させる独立ステータスバーを実装\n" +
        //         "• SampleA: 「外部リンクの自動切断」安全機能を追加\n" +
        //         "• ダーク／ライトモードのシームレスなホットリロードを最適化"
        // };
        // yield return new ReleaseNoteItem
        // {
        //     VersionTitle = "v1.1.0 - アーキテクチャ強化",
        //     DateLabel = "2026/02",
        //     IsHighlight = false,
        //     Notes =
        //         "• プラグインのダイナミック・ホットリロード機構（再起動不要）を実装\n" +
        //         "• 共有依存関係（Excel COM等）のアセンブリ分離と安定性向上\n" +
        //         "• MVVMアーキテクチャの標準化とCommunityToolkitの導入"
        // };
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
