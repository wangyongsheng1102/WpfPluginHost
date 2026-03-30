using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using ShellApp.ViewModels;

namespace ShellApp.Views;

public partial class MainWindow : Window
{
    private const double ExpandedMenuWidth = 260;
    private const double CollapsedMenuWidth = 72;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // 极少数情况下 DragMove 会失败，忽略即可
            }
        }
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

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = this.Width + e.HorizontalChange;
        double newHeight = this.Height + e.VerticalChange;

        if (newWidth > this.MinWidth)
        {
            this.Width = newWidth;
        }

        if (newHeight > this.MinHeight)
        {
            this.Height = newHeight;
        }
    }
}
