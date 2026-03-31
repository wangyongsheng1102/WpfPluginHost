using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Plugin.PostgreCompare.Models;

namespace Plugin.PostgreCompare.ViewModels;

public partial class CompareViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<DatabaseConnection> _connections = new();

    [ObservableProperty]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    private string _baseFolderPath = string.Empty;

    [ObservableProperty]
    private string _oldFolderPath = string.Empty;

    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CsvFileInfo> _csvFileInfos = new();

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    private readonly MainViewModel _mainViewModel;

    public CompareViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }
}

