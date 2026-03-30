using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Plugin.SampleB;

public partial class SampleBViewModel : ObservableObject
{
    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool HasPreview => PreviewImage != null;

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnPreviewImageChanged(ImageSource? value) => OnPropertyChanged(nameof(HasPreview));

    [RelayCommand]
    private void SelectImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter =
                "画像 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル (*.*)|*.*",
            Title = "画像の選択"
        };
        if (dlg.ShowDialog() != true)
            return;

        LoadImage(dlg.FileName);
    }

    private void LoadImage(string path)
    {
        StatusMessage = string.Empty;
        PreviewImage = null;
        try
        {
            if (!File.Exists(path))
            {
                StatusMessage = "ファイルが存在しません。";
                ImagePath = string.Empty;
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            ImagePath = path;
            PreviewImage = bmp;
        }
        catch (Exception ex)
        {
            ImagePath = string.Empty;
            StatusMessage = $"画像を読み込めません：{ex.Message}";
        }
    }
}
