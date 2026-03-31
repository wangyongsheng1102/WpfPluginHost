using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Plugin.PostgreCompare.Models;

public partial class DatabaseConnection : ObservableObject
{
    [ObservableProperty]
    private string _configurationName = string.Empty;
    [ObservableProperty]
    private string _host = "localhost";
    [ObservableProperty]
    private int _port = 5432;
    [ObservableProperty]
    private string _database = string.Empty;
    [ObservableProperty]
    private string _user = string.Empty;
    [ObservableProperty]
    private string _password = string.Empty;
    [ObservableProperty]
    private string _wslDistro = string.Empty;

    public string GetConnectionString()
    {
        return $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password}";
    }
}

public partial class CsvFileInfo : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public CsvFileInfo(string fileName)
    {
        FileName = fileName;
    }
}

public partial class TableInfo : ObservableObject
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FullName => $"{SchemaName}.{TableName}";

    [ObservableProperty]
    private bool _isSelected = true;

    public long RowCount { get; set; }
}

public enum ComparisonStatus
{
    Deleted,
    Added,
    Updated,
    Unchanged
}

public class RowComparisonResult
{
    public string TableName { get; set; } = string.Empty;
    public ComparisonStatus Status { get; set; }
    public Dictionary<string, object?> OldValues { get; set; } = new();
    public Dictionary<string, object?> NewValues { get; set; } = new();
    public Dictionary<string, object?> PrimaryKeyValues { get; set; } = new();
}

