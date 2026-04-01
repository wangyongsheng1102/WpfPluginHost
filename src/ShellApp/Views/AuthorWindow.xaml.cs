using System.Windows;
using ShellApp.Services;
using ShellApp.ViewModels;

namespace ShellApp.Views;

public partial class AuthorWindow : Window
{
    public AuthorWindow()
    {
        InitializeComponent();
        AppIconLoader.ApplyTo(this);
        DataContext = new AuthorWindowViewModel(Close);
    }
}
