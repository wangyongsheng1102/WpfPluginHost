using System.Windows;

namespace ShellApp.Services;

public sealed class ThemeService
{
    private const string DarkThemePath = "Themes/Theme.Dark.xaml";
    private const string LightThemePath = "Themes/Theme.Light.xaml";

    public bool IsDarkTheme { get; private set; } = false;

    public void ApplyTheme(bool isDarkTheme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var target = isDarkTheme ? DarkThemePath : LightThemePath;

        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString.Equals(DarkThemePath, StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.Equals(LightThemePath, StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = new ResourceDictionary { Source = new Uri(target, UriKind.Relative) };
        }
        else
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = new Uri(target, UriKind.Relative) });
        }

        IsDarkTheme = isDarkTheme;
    }
}
