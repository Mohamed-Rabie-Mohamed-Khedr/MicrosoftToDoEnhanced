using System.Windows;
using System.Windows.Media;

namespace MicrosoftToDoEnhanced.Themes;

public enum AppTheme
{
    Light,
    Dark
}

public static class ThemeManager
{
    private const string LightSource = "Themes/LightTheme.xaml";
    private const string DarkSource = "Themes/DarkTheme.xaml";
    private const string DefaultAccentHex = "#0078D4";

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static string CurrentAccentHex { get; private set; } = DefaultAccentHex;

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

        ApplyAccent(CurrentAccentHex);
    }

    public static void Toggle() =>
        ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    public static void ApplyAccentColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor) || !TryParseColor(hexColor, out _))
            return;

        CurrentAccentHex = hexColor;
        ApplyAccent(hexColor);
    }

    private static void ApplyAccent(string hexColor)
    {
        var app = Application.Current;
        if (app is null || !TryParseColor(hexColor, out var color))
            return;

        app.Resources["AccentBrush"] = new SolidColorBrush(color);
        app.Resources["AccentHoverBrush"] = new SolidColorBrush(Lighten(color, 0.12));
        app.Resources["AccentPressedBrush"] = new SolidColorBrush(Darken(color, 0.12));
        app.Resources["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B));
        app.Resources["AccentColor"] = color;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
        }

        color = default;
        return false;
    }

    private static Color Lighten(Color color, double amount) =>
        Blend(color, Colors.White, amount);

    private static Color Darken(Color color, double amount) =>
        Blend(color, Colors.Black, amount);

    private static Color Blend(Color color, Color target, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(color.R, target.R), Mix(color.G, target.G), Mix(color.B, target.B));
    }
}
