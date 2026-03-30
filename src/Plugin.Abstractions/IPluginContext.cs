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
}
