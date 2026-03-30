using System.Windows.Controls;

namespace Plugin.Abstractions;

/// <summary>動的読み込みされるプラグインモジュールの契約。</summary>
public interface IPluginModule
{
    /// <summary>プラグインの一意識別子。</summary>
    string Id { get; }
    /// <summary>ナビゲーションに表示するタイトル。</summary>
    string Title { get; }
    /// <summary>折りたたみ時のツールチップ等に使う説明文。</summary>
    string Description { get; }
    /// <summary>絵文字または画像パスなど、メニュー用のアイコン指定。</summary>
    string IconKey { get; }
    /// <summary>メニュー項目の並び順（昇順）。</summary>
    int Order { get; }
    
    /// <summary>プラグインを初期化し、ホストシェルからのコンテキスト（グローバルステータスバー等）を受け取る。</summary>
    void Initialize(IPluginContext context);

    /// <summary>プラグインのメイン <see cref="UserControl"/> を生成する。</summary>
    UserControl CreateView();
}
