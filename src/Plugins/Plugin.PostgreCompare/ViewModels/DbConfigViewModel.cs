using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Dialogs;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;

namespace Plugin.PostgreCompare.ViewModels;

public partial class DbConfigViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    private readonly MainViewModel _mainViewModel;
    private readonly WslService _wslService = new();

    public DbConfigViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        AttachConnectionCollection(Connections);
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
    private async Task StartPostgreSqlInWsl()
    {
        var dialog = new WslDistroInputDialog
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var distro = dialog.DistroName;
        if (string.IsNullOrWhiteSpace(distro))
        {
            _mainViewModel.AppendLog("WSL ディストリビューション名が空です。", LogLevel.Error);
            return;
        }

        try
        {
            _mainViewModel.ReportProgress($"WSL ({distro}) で PostgreSQL を起動しています...", 0, true);
            _mainViewModel.AppendLog($"WSL ディストリビューション「{distro}」で pg ユーザーとして pg_ctl start を実行しています...", LogLevel.Info);

            var result = await _wslService.StartPostgreSqlAsPgAsync(distro);

            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                _mainViewModel.AppendLog(result.StandardOutput, LogLevel.Info);
            }

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                _mainViewModel.AppendLog(result.StandardError, LogLevel.Warning);
            }

            if (result.ExitCode == 0 || LooksLikePostgreAlreadyRunning(result))
            {
                _mainViewModel.AppendLog(
                    LooksLikePostgreAlreadyRunning(result) && result.ExitCode != 0
                        ? "PostgreSQL は既に起動している可能性があります。"
                        : "PostgreSQL の起動コマンドが完了しました。",
                    LogLevel.Success);
            }
            else
            {
                _mainViewModel.AppendLog(
                    $"pg_ctl の終了コード: {result.ExitCode}。WSL 内の設定（.bash_profile の PGDATA / PATH）を確認してください。",
                    LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            _mainViewModel.AppendLog($"WSL 経由の起動に失敗しました: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _mainViewModel.ReportProgress(string.Empty, 100);
        }
    }

    private static bool LooksLikePostgreAlreadyRunning(WslPostgreStartResult result)
    {
        var text = $"{result.StandardOutput}\n{result.StandardError}";
        return text.Contains("another postgresql server might be running", StringComparison.OrdinalIgnoreCase)
               || text.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnConnectionsChanged(ObservableCollection<DatabaseConnection> value)
    {
        AttachConnectionCollection(value);
    }

    private void AttachConnectionCollection(ObservableCollection<DatabaseConnection> collection)
    {
        collection.CollectionChanged -= OnConnectionsCollectionChanged;
        collection.CollectionChanged += OnConnectionsCollectionChanged;

        foreach (var item in collection)
        {
            AttachConnectionItem(item);
        }
    }

    private void OnConnectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<DatabaseConnection>())
            {
                AttachConnectionItem(item);
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<DatabaseConnection>())
            {
                item.PropertyChanged -= OnConnectionPropertyChanged;
            }
        }

        if (!_mainViewModel.IsBulkUpdatingConnections)
            _mainViewModel.SaveConnections();
    }

    private void AttachConnectionItem(DatabaseConnection item)
    {
        item.PropertyChanged -= OnConnectionPropertyChanged;
        item.PropertyChanged += OnConnectionPropertyChanged;
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_mainViewModel.IsBulkUpdatingConnections)
            _mainViewModel.SaveConnections();
    }
}

