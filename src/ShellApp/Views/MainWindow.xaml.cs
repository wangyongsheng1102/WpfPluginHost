using System.Windows;
using System.Windows.Media.Animation;
using System.ComponentModel;
using Plugin.Abstractions;
using ShellApp.ViewModels;

namespace ShellApp.Views;

public partial class MainWindow : Window
{
    private const double ExpandedMenuWidth = ThemeDimensions.NavExpandedWidth;
    private const double CollapsedMenuWidth = ThemeDimensions.NavCollapsedWidth;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel vm)
        {
            NavPanel.Width = vm.IsMenuCollapsed ? CollapsedMenuWidth : ExpandedMenuWidth;
        }

        if (e.NewValue is INotifyPropertyChanged newNotify)
        {
            newNotify.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMenuCollapsed))
        {
            return;
        }

        if (sender is not MainWindowViewModel vm)
        {
            return;
        }

        AnimateMenu(vm.IsMenuCollapsed);
    }

    private void AnimateMenu(bool isCollapsed)
    {
        var targetWidth = isCollapsed ? CollapsedMenuWidth : ExpandedMenuWidth;
        var widthAnimation = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(ThemeDimensions.AnimMenuExpandMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(widthAnimation, 120);

        NavPanel.BeginAnimation(WidthProperty, widthAnimation, HandoffBehavior.SnapshotAndReplace);
    }
}
