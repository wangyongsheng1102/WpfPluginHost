namespace Plugin.Abstractions;

/// <summary>プラグインがホスト側のグローバルステータスバー等と通信するためのコンテキスト。</summary>
public interface IPluginContext
{
    void ReportProgress(string message, double percentage = 0, bool isIndeterminate = false);
    void ReportInfo(string message);
    void ReportWarning(string message);
    void ReportSuccess(string message);
    void ReportError(string message);
    void ClearStatus();

    /// <summary>プラグイン固有の設定を取得する（JSON 形式の文字列）。</summary>
    string? GetPluginSetting(string pluginId);

    /// <summary>プラグイン固有の設定を保存する（JSON 形式の文字列）。</summary>
    void SavePluginSetting(string pluginId, string json);
}
