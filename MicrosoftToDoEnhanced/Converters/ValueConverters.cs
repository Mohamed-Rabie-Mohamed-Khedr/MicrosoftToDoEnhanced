using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MicrosoftToDoEnhanced.Converters;

/// <summary>
/// Converts a hex color string ("#RRGGBB") into a SolidColorBrush.
/// Returns a neutral brush when the input is null/invalid.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { /* fall through to default */ }
        }
        return Application.Current.TryFindResource("TextTertiaryBrush") ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        return Binding.DoNothing;
    }
}

/// <summary>
/// Inverts a boolean. Used to show/hide panels depending on selection state.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : Binding.DoNothing;
}

/// <summary>
/// true -> Visible, false -> Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v ? v == Visibility.Visible : Binding.DoNothing;
}

/// <summary>
/// false -> Visible, true -> Collapsed.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v ? v != Visibility.Visible : Binding.DoNothing;
}

/// <summary>
/// null/false/empty -> Visible; otherwise Collapsed. Pass "Invert" as parameter to invert.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            bool b => !b,
            _ => false
        };
        var invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (isEmpty ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Returns a friendly relative timestamp ("Just now", "5 min ago", "Yesterday", date).
/// </summary>
public class RelativeDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime date) return string.Empty;
        var span = DateTime.Now - date;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
        return date.ToString("MMM d, yyyy", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Builds a compact attachment size label: "invoice.pdf · 245 KB".
/// Parameter "SizeOnly" returns just the formatted size.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not (int or long)) return string.Empty;
        var kb = System.Convert.ToDouble(value);
        if (kb < 1024) return $"{kb:0} KB";
        return $"{kb / 1024d:0.#} MB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Extracts initials (max 2 chars) from a display name for avatar circles.
/// </summary>
public class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
