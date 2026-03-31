using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.Services;

/// <summary>
/// 接続設定の永続化サービス（シンプルな JSON 保存）。
/// </summary>
public class ConfigService
{
    private readonly string _configPath;

    public ConfigService()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(baseDir, "PostgreCompare");
        Directory.CreateDirectory(appDir);
        _configPath = Path.Combine(appDir, "connections.json");
    }

    public IReadOnlyList<DatabaseConnection> LoadConnections()
    {
        if (!File.Exists(_configPath))
        {
            return Array.Empty<DatabaseConnection>();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var items = JsonSerializer.Deserialize<List<DatabaseConnection>>(json) ?? new List<DatabaseConnection>();
            return items;
        }
        catch
        {
            return Array.Empty<DatabaseConnection>();
        }
    }

    public void SaveConnections(IReadOnlyList<DatabaseConnection> connections)
    {
        var json = JsonSerializer.Serialize(connections, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }
}

