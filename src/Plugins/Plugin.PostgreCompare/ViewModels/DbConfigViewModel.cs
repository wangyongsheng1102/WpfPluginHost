using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.PostgreCompare.Models;
using Plugin.PostgreCompare.Services;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Plugin.PostgreCompare.ViewModels;

public partial class DbConfigViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    private readonly MainViewModel _mainViewModel;

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

        _mainViewModel.SaveConnections();
    }

    private void AttachConnectionItem(DatabaseConnection item)
    {
        item.PropertyChanged -= OnConnectionPropertyChanged;
        item.PropertyChanged += OnConnectionPropertyChanged;
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _mainViewModel.SaveConnections();
    }
}

