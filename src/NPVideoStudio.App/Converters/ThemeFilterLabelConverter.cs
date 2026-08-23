using System.Globalization;
using Avalonia.Data.Converters;

namespace NPVideoStudio.App.Converters;

/// <summary>Displays the style gallery's theme filter ComboBox items - null (no filter) as "Sve teme", any real theme via <see cref="EnumLabelConverter"/>.</summary>
public sealed class ThemeFilterLabelConverter : IValueConverter
{
    public static readonly ThemeFilterLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? "Sve teme" : EnumLabelConverter.Instance.Convert(value, targetType, parameter, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
