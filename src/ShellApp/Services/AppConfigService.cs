using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using ShellApp.Models;

namespace ShellApp.Services;

/// <summary>
/// アプリケーション全体の構成設定を管理するサービス。
/// </summary>
public class AppConfigService
{
    private readonly string _configPath;
    private AppConfig _config = new();

    public AppConfig Config => _config;

    public AppConfigService()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(baseDir, "WpfPluginHost");
        Directory.CreateDirectory(appDir);
        _configPath = Path.Combine(appDir, "appsettings.json");
        
        Load();
    }

    /// <summary>
    /// 設定をファイルから読み込む。
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            _config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            _config = new AppConfig();
        }
    }

    /// <summary>
    /// 設定をファイルに保存する。
    /// </summary>
    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"設定の保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// プラグイン固有の設定を取得する。
    /// </summary>
    public T? GetPluginSetting<T>(string pluginId)
    {
        if (_config.PluginSettings.TryGetValue(pluginId, out var element))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText());
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    /// <summary>
    /// プラグイン固有の設定を保存する。
    /// </summary>
    public void SetPluginSetting<T>(string pluginId, T setting)
    {
        if (setting == null)
        {
            _config.PluginSettings.Remove(pluginId);
        }
        else
        {
            var json = JsonSerializer.Serialize(setting);
            _config.PluginSettings[pluginId] = JsonDocument.Parse(json).RootElement;
        }
        Save();
    }
}
