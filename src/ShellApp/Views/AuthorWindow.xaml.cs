using System.Windows;

namespace ShellApp.Views;

public partial class AuthorWindow : Window
{
    public AuthorWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
