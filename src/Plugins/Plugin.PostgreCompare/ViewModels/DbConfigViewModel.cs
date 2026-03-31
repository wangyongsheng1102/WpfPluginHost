using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;

namespace Plugin.PostgreCompare.ViewModels;

public partial class DbConfigViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private ObservableCollection<string> _wslDistributions = new();

    private readonly WslService _wslService = new();
    private readonly MainViewModel _mainViewModel;

    public DbConfigViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;

        Connections.CollectionChanged += (_, _) =>
        {
            _mainViewModel.SaveConnections();
        };
    }

    [RelayCommand]
    private void AddConnection()
    {
        var newConnection = new DatabaseConnection
        {
            ConfigurationName = $"設定{Connections.Count + 1}",
            Host = "localhost",
            Port = 5880,
            User = "cisdb_unisys",
            Password = "cisdb_unisys",
            Database = "cisdb"
        };

        Connections.Add(newConnection);
        _mainViewModel.AppendLog($"接続 '{newConnection.ConfigurationName}' を追加しました。");
    }

    [RelayCommand]
    private void RemoveConnection(DatabaseConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        var name = connection.ConfigurationName;
        Connections.Remove(connection);
        _mainViewModel.AppendLog($"接続 '{name}' を削除しました。");
    }

    [RelayCommand]
    private async Task RefreshWslDistributions()
    {
        _mainViewModel.AppendLog("WSL ディストリビューションリストを更新しています...");
        var distros = await _wslService.GetWslDistributionsAsync();
        WslDistributions.Clear();
        foreach (var distro in distros)
        {
            WslDistributions.Add(distro);
        }
        _mainViewModel.AppendLog($"{WslDistributions.Count} 個の WSL ディストリビューションを検出しました。");
    }
}

