using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;

namespace Plugin.PostgreCompare.ViewModels;

public partial class ImportExportViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<TableInfo> _tables = new();

    public int SelectedTableCount => Tables.Count(t => t.IsSelected);

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFolderPath = string.Empty;

    [ObservableProperty]
    private string _importFolderPath = string.Empty;

    private readonly DatabaseService _databaseService = new();
    private readonly MainViewModel _mainViewModel;

    public ImportExportViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    [RelayCommand]
    private async Task LoadTables()
    {
        if (SelectedConnection == null)
        {
            _mainViewModel.AppendLog("接続を選択してください。", LogLevel.Error);
            return;
        }

        IsProcessing = true;
        _mainViewModel.AppendLog("テーブルリストを取得しています...");

        var tables = await _databaseService.GetTablesAsync(SelectedConnection.GetConnectionString());
        Tables.Clear();
        foreach (var table in tables)
        {
            Tables.Add(table);
        }

        OnPropertyChanged(nameof(SelectedTableCount));
        IsProcessing = false;
        _mainViewModel.AppendLog($"{Tables.Count} 個のテーブルを取得しました。", LogLevel.Success);
    }
}

