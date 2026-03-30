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
    /// <summary>プラグイン Id ごとにビューを再利用し、メニュー切り替え時に各ページの状態を保持する。プラグイン再読み込み後は必ず Clear する。</summary>
    private readonly Dictionary<string, UserControl> _pluginViewCache = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(PluginManager pluginManager, ThemeService themeService)
    {
        _pluginManager = pluginManager;
        _themeService = themeService;
        _pluginManager.PluginsChanged += OnPluginsChanged;

        ToggleMenuCommand = new RelayCommand(ToggleMenu);
        ReloadPluginsCommand = new RelayCommand(ReloadPlugins);
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

    public double MenuWidth => IsMenuCollapsed ? 84 : 260;
    public string PluginCountText => $"プラグイン数: {MenuItems.Count}";

    public IRelayCommand ToggleMenuCommand { get; }
    public IRelayCommand ReloadPluginsCommand { get; }

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
        // プラグインビューは作り直さず編集状態を保持。一旦外して戻し、スタイル／ブラシを無効化して Application テーマの DynamicResource を反映
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

    [RelayCommand]
    private void ShowAuthor()
    {
        System.Windows.MessageBox.Show(
            "WPF Plugin Shell\n\n" +
            "👤 開発者: Antigravity AI (Google DeepMind Team)\n" +
            "💡 現在のバージョン: 1.2.0\n\n" +
            "✨ 最新のアップデート内容:\n" +
            "- Windows 11 Fluent Design（アクリル背景・角丸）を全体に導入しました\n" +
            "- SampleA: ドラッグ＆ドロップ対応のバッチ処理キューシステムを再構築しました\n" +
            "- SampleA: 独立したステータスパネルと「外部リンクの切断」の自動化判定を追加しました\n" +
            "- コア体験：主要ボタンの視認性を向上させ、テーマのシームレスな切り替えを最適化しました",
            "開発者情報 ＆ アップデート履歴",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
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

        // 常に新しい MenuItems から項目を選び、Clear 後も古い VM を参照しないようにし、新しいアセンブリに合わせてビューを再構築する
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
