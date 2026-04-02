using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShellApp.Models;

/// <summary>
/// アプリケーション全体の永続化設定。
/// </summary>
public class AppConfig
{
    /// <summary>テーマ選択（ライト/ダーク）</summary>
    public bool IsDarkTheme { get; set; } = false;

    /// <summary>メニューの収縮状態</summary>
    public bool IsMenuCollapsed { get; set; } = false;

    /// <summary>最後に選択されていたメニュー（プラグインID）</summary>
    public string? LastSelectedPluginId { get; set; }

    /// <summary>
    /// 各プラグイン固有の必要設定情報。
    /// キーはプラグインID、値は JsonElement（デシリアライズ時に具体的な型に変換するため）。
    /// </summary>
    public Dictionary<string, JsonElement> PluginSettings { get; set; } = new();
}
