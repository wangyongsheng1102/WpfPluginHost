using System.Windows;
using ShellApp.ViewModels;

namespace ShellApp.Views;

public partial class AuthorWindow : Window
{
    public AuthorWindow()
    {
        InitializeComponent();
        DataContext = new AuthorWindowViewModel(Close);
    }
}
