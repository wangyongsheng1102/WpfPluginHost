using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShellApp.Services;

namespace ShellApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PluginManager _pluginManager;
    private readonly ThemeService _themeService;
    /// <summary>按插件 Id 复用视图，切换菜单时保留各页状态；插件重载后必须 Clear。</summary>
    private readonly Dictionary<string, UserControl> _pluginViewCache = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(PluginManager pluginManager, ThemeService themeService)
    {
        _pluginManager = pluginManager;
        _themeService = themeService;
        _pluginManager.PluginsChanged += OnPluginsChanged;

        ToggleMenuCommand = new RelayCommand(ToggleMenu);
        MenuItems = new ObservableCollection<PluginMenuItemViewModel>();
        IsDarkTheme = _themeService.IsDarkTheme;

        ReloadMenuItems();
    }

    public ObservableCollection<PluginMenuItemViewModel> MenuItems { get; }

    [ObservableProperty]
    private PluginMenuItemViewModel? selectedMenuItem;

    [ObservableProperty]
    private UserControl? currentView;

    [ObservableProperty]
    private bool isMenuCollapsed;

    [ObservableProperty]
    private bool isDarkTheme;

    public double MenuWidth => IsMenuCollapsed ? 72 : 260;
    public string PluginCountText => $"Plugins: {MenuItems.Count}";

    public IRelayCommand ToggleMenuCommand { get; }

    partial void OnSelectedMenuItemChanged(PluginMenuItemViewModel? value)
    {
        if (value is null)
        {
            CurrentView = null;
            return;
        }

        if (!_pluginViewCache.TryGetValue(value.Id, out var view))
        {
            view = value.Module.CreateView();
            _pluginViewCache[value.Id] = view;
        }

        CurrentView = view;
    }

    partial void OnIsMenuCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(MenuWidth));
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeService.ApplyTheme(value);
        // 不重建插件视图，以免丢失编辑状态；摘挂 + 失效样式/画刷促使 DynamicResource 随 Application 主题字典更新
        RefreshCurrentPluginViewAfterThemeChange();
    }

    private void RefreshCurrentPluginViewAfterThemeChange()
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

    private void OnPluginsChanged(object? sender, EventArgs e)
    {
        App.Current.Dispatcher.Invoke(ReloadMenuItems);
    }

    private void ReloadMenuItems()
    {
        _pluginViewCache.Clear();
        MenuItems.Clear();
        foreach (var plugin in _pluginManager.GetModules().OrderBy(x => x.Module.Order))
        {
            MenuItems.Add(new PluginMenuItemViewModel(plugin.Module, plugin.SourcePath));
        }

        OnPropertyChanged(nameof(PluginCountText));

        if (MenuItems.Count == 0)
        {
            SelectedMenuItem = null;
            CurrentView = null;
            return;
        }

        // 始终从新的 MenuItems 里取实例，避免 Clear 后仍指向已不在集合中的旧 VM，并触发视图按新程序集重建
        var previousId = SelectedMenuItem?.Id;
        PluginMenuItemViewModel? match = null;
        if (!string.IsNullOrEmpty(previousId))
        {
            match = MenuItems.FirstOrDefault(x =>
                string.Equals(x.Id, previousId, StringComparison.OrdinalIgnoreCase));
        }

        SelectedMenuItem = match ?? MenuItems[0];
    }
}
