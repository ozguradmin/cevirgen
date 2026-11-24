// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Cevirgen.App.Controls;

/// <summary>
/// Çevirgen adı (wordmark): küçük harf "çevirgen", Archivo Condensed 800; "ç" altyazı sarısı (taslak: üst çubuğun solu).
/// Kare logo (Assets/Cevirgen.ico) yalnız pencere, görev çubuğu ve tepsi simgesidir.
/// </summary>
public sealed class Wordmark : TextBlock
{
    public Wordmark()
    {
        SetResourceReference(StyleProperty, "WordmarkText");
        var accent = new Run("ç");
        accent.SetResourceReference(TextElement.ForegroundProperty, "amberBrush");
        Inlines.Add(accent);
        Inlines.Add(new Run("evirgen"));
        AutomationProperties.SetName(this, "Çevirgen");
        VerticalAlignment = VerticalAlignment.Center;
    }
}
