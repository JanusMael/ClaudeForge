using System.Globalization;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Formats a byte count as a short human-readable string: "512 B", "4.3 KB", "1.2 MB", "1.7 GB".
/// Accepts <see cref="long"/>, <see cref="int"/>, or <see cref="double"/>; anything else → empty string.
/// </summary>
public sealed class BytesToHumanReadableConverter : IValueConverter
{
    /// <summary>Shared singleton for XAML lookups.</summary>
    public static readonly BytesToHumanReadableConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            var _ => -1,
        };
        if (bytes < 0)
        {
            return string.Empty;
        }

        return Format(bytes);
    }

    /// <summary>Public static entrypoint so ViewModels can format directly.</summary>
    public static string Format(long bytes)
    {
        const double KB = 1024;
        const double MB = 1024 * 1024;
        const double GB = 1024 * 1024 * 1024;

        return bytes switch
        {
            < (long)KB => $"{bytes} B",
            < (long)MB => $"{bytes / KB:F1} KB",
            < (long)GB => $"{bytes / MB:F1} MB",
            var _ => $"{bytes / GB:F2} GB",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}