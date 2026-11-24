// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using CouchSubs.Core;

namespace CouchSubs.App.Theme;

/// <summary>XAML'de ekran metni: <c>{t:T 'Start translating'}</c>. Metin Mac'le ortak katalogdan gelir.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Localizer.Current[key];
}

/// <summary>true → görünür; <c>ConverterParameter=invert</c> ile tersi.</summary>
public sealed class VisibleIf : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var shown = value switch
        {
            bool flag => flag,
            string text => text.Length > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "invert") shown = !shown;
        return shown ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Bool'u tersine çevirir (ör. IsEnabled="{Binding IsStarting, Converter={StaticResource Not}}").</summary>
public sealed class Not : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// Denetim köşesi; <c>{Binding ActualHeight, RelativeSource={RelativeSource Self}, Converter={StaticResource PillCorner}}</c>.
/// Çevirgen: hap biçimi yok, köşe en çok 6 (taslak: "radius 6px only on controls"); küçük noktalarda yüksekliğin yarısı.
/// </summary>
public sealed class PillCorner : IValueConverter
{
    /// <summary>Denetimlerin köşesi (tasarim.json › radius.control).</summary>
    public const double Control = 6;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new CornerRadius(value is double height && height > 0 ? Math.Min(height / 2, Control) : 0);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
