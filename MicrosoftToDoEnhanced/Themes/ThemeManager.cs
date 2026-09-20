using System.Windows;

namespace MicrosoftToDoEnhanced.Themes;

public enum AppTheme
{
    Light,
    Dark
}

/// <summary>
/// Swaps the active theme resource dictionary at runtime (UI-level concern only).
/// </summary>
public static class ThemeManager
{
    private const string LightSource = "Themes/LightTheme.xaml";
    private const string DarkSource = "Themes/DarkTheme.xaml";

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var source = theme == AppTheme.Dark ? DarkSource : LightSource;
        var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var existing = app.Resources.MergedDictionaries[i];
            if (existing.Source is not null && existing.Source.OriginalString.Contains("Themes/"))
                app.Resources.MergedDictionaries.RemoveAt(i);
        }

        app.Resources.MergedDictionaries.Insert(0, dictionary);
        CurrentTheme = theme;
    }

    public static void Toggle() =>
        ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
