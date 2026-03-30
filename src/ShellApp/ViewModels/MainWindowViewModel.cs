using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellApp.Services;
using ShellApp.Views;

namespace ShellApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PluginManager _pluginManager;
    private readonly ThemeService _themeService;
    private readonly GlobalStatusService _globalStatus;
    /// <summary>プラグイン Id ごとにビューを再利用し、メニュー切り替え時に各ページの状態を保持する。プラグイン再読み込み後は必ず Clear する。</summary>
    private readonly Dictionary<string, UserControl> _pluginViewCache = new(StringComparer.OrdinalIgnoreCase);
    private UserControl? _homeView;

    public MainWindowViewModel(PluginManager pluginManager, ThemeService themeService, GlobalStatusService globalStatusService)
    {
        _pluginManager = pluginManager;
        _themeService = themeService;
        _globalStatus = globalStatusService;
        GlobalStatus = globalStatusService;
        _pluginManager.PluginsChanged += OnPluginsChanged;

        ToggleMenuCommand = new RelayCommand(ToggleMenu);
        ReloadPluginsCommand = new RelayCommand(ReloadPlugins);
        MenuItems = new ObservableCollection<PluginMenuItemViewModel>();
        IsDarkTheme = _themeService.IsDarkTheme;

        ReloadMenuItems();
    }

    public ObservableCollection<PluginMenuItemViewModel> MenuItems { get; }

    public GlobalStatusService GlobalStatus { get; }

    [ObservableProperty]
    private PluginMenuItemViewModel? selectedMenuItem;

    [ObservableProperty]
    private UserControl? currentView;

    [ObservableProperty]
    private bool isMenuCollapsed;

    [ObservableProperty]
    private bool isDarkTheme;

    public double MenuWidth => IsMenuCollapsed ? 84 : 260;

    public string PluginCountText => $"プラグイン数: {MenuItems.Count}";

    /// <summary>ナビ下部の件数表示。折りたたみ時は数字のみで幅を抑える。</summary>
    public string PluginCountNavDisplay =>
        IsMenuCollapsed ? MenuItems.Count.ToString() : $"プラグイン数: {MenuItems.Count}";

    /// <summary>折りたたみ時はアイコンのみで「ホーム」を収める。</summary>
    public string HomeNavCaption => IsMenuCollapsed ? "🏠" : "ホーム";

    public IRelayCommand ToggleMenuCommand { get; }
    public IRelayCommand ReloadPluginsCommand { get; }

    partial void OnSelectedMenuItemChanged(PluginMenuItemViewModel? value)
    {
        ApplyCurrentView();
    }

    partial void OnIsMenuCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(MenuWidth));
        OnPropertyChanged(nameof(PluginCountNavDisplay));
        OnPropertyChanged(nameof(HomeNavCaption));
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeService.ApplyTheme(value);
        _globalStatus.RefreshTextBrushAfterThemeChange();
        RefreshCurrentViewAfterThemeChange();
    }

    private void ApplyCurrentView()
    {
        if (SelectedMenuItem is null)
        {
            CurrentView = GetOrCreateHomeView();
            return;
        }

        if (!_pluginViewCache.TryGetValue(SelectedMenuItem.Id, out var view))
        {
            view = SelectedMenuItem.Module.CreateView();
            _pluginViewCache[SelectedMenuItem.Id] = view;
        }

        CurrentView = view;
    }

    private UserControl GetOrCreateHomeView()
    {
        return _homeView ??= new HomeView();
    }

    private void RefreshCurrentViewAfterThemeChange()
    {
        var view = CurrentView;
        if (view is null)
        {
            return;
        }

        CurrentView = null;
        CurrentView = view;

        ThemeResourceRefresh.InvalidateThemeBoundProperties(view);
    }

    private void ToggleMenu()
    {
        IsMenuCollapsed = !IsMenuCollapsed;
    }

    [RelayCommand]
    private void ShowAuthor()
    {
        var authorWindow = new AuthorWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        authorWindow.ShowDialog();
    }

    [RelayCommand]
    private void ShowHome()
    {
        SelectedMenuItem = null;
    }

    [RelayCommand]
    private void NavigateToPlugin(PluginMenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedMenuItem = item;
    }

    private void ReloadPlugins()
    {
        _pluginManager.ReloadAll();
    }

    private void OnPluginsChanged(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.Invoke(ReloadMenuItems);
    }

    private void ReloadMenuItems()
    {
        _pluginViewCache.Clear();
        _homeView = null;
        MenuItems.Clear();
        foreach (var plugin in _pluginManager.GetModules().OrderBy(x => x.Module.Order))
        {
            MenuItems.Add(new PluginMenuItemViewModel(plugin.Module, plugin.SourcePath));
        }

        OnPropertyChanged(nameof(PluginCountText));
        OnPropertyChanged(nameof(PluginCountNavDisplay));

        if (MenuItems.Count == 0)
        {
            SelectedMenuItem = null;
            ApplyCurrentView();
            return;
        }

        var wasHome = SelectedMenuItem is null;
        var previousId = SelectedMenuItem?.Id;
        PluginMenuItemViewModel? match = null;
        if (!string.IsNullOrEmpty(previousId))
        {
            match = MenuItems.FirstOrDefault(x =>
                string.Equals(x.Id, previousId, StringComparison.OrdinalIgnoreCase));
        }

        if (wasHome)
        {
            SelectedMenuItem = null;
        }
        else
        {
            SelectedMenuItem = match ?? MenuItems[0];
        }

        ApplyCurrentView();
    }
}
