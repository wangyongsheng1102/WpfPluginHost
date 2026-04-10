using System.Windows;

namespace Plugin.PostgreCompare.Dialogs;

public partial class WslDistroInputDialog : Window
{
    public WslDistroInputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => DistroTextBox.Focus();
    }

    public string? DistroName => DistroTextBox.Text?.Trim();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DistroTextBox.Text))
        {
            MessageBox.Show(
                this,
                "ディストリビューション名を入力してください。",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
