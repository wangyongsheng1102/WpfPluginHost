using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.ComponentModel;
using ShellApp.ViewModels;

namespace ShellApp.Views;

public partial class MainWindow : Window
{
    private const double ExpandedMenuWidth = 260;
    private const double CollapsedMenuWidth = 84;

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
        var animation = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        NavPanel.BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
