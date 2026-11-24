// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>
/// Çevirgen ekranlarının (Görünüm, Çeviri, Geçmiş ve sözlük, Alanları düzenle) koddan kurulan ortak parçaları: başlıklar, mono
/// etiket, ipucu yazısı, kıl çizgi, düğmeler, bölümlü seçim ve − / + adım düğmesi. Renkler tema kaynaklarından (açık ve koyu
/// tema aynı kodla); boşluklar tasarim.json belirteçleriyle (<see cref="Sp"/>).
/// </summary>
internal static class ScreenKit
{
    /// <summary>Ekran başlığı (Archivo Condensed 30; taslak h1).</summary>
    public static TextBlock Title(string text, double size = 30)
    {
        var title = new TextBlock { Text = text };
        title.SetResourceReference(FrameworkElement.StyleProperty, "ScreenTitle");
        title.FontSize = size;
        AutomationProperties.SetHeadingLevel(title, size >= 30 ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2);
        return title;
    }

    /// <summary>Mono büyük harf etiket (taslak .lbl).</summary>
    public static TextBlock Label(string text, string brush = "mutedBrush")
    {
        var label = new TextBlock { Text = text.ToUpper(new System.Globalization.CultureInfo("tr-TR")) };
        label.SetResourceReference(FrameworkElement.StyleProperty, "MonoLabel");
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return label;
    }

    /// <summary>Gri açıklama (sarar).</summary>
    public static TextBlock Muted(string text, double size = 14)
    {
        var hint = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = Math.Round(size * 1.5),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
        return hint;
    }

    /// <summary>Düz yazı (tema rengi).</summary>
    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, bool wrap = true)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        return block;
    }

    /// <summary>Mono sayı / zaman kodu (taslak .tc).</summary>
    public static TextBlock Mono(string text, double size = 12, string brush = "mutedBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size };
        block.SetResourceReference(FrameworkElement.StyleProperty, "Timecode");
        block.FontSize = size;
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>Kıl çizgi (1 px, kenarlık rengi).</summary>
    public static Border Rule(bool vertical = false)
    {
        var rule = vertical ? new Border { Width = 1 } : new Border { Height = 1 };
        rule.SetResourceReference(Border.BackgroundProperty, "borderBrush");
        return rule;
    }

    /// <summary>Üstünde kıl çizgi olan satır kutusu.</summary>
    public static Border Row(UIElement child, string padding = "0,s")
    {
        var row = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = Sp.T(padding), Child = child };
        row.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        return row;
    }

    /// <summary>Düğme (stil adıyla: OutlineButton, PrimaryButton, TextLink …).</summary>
    public static Button Button(string text, string style, RoutedEventHandler click, string? name = null)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        button.Click += click;
        if (name is not null) AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>
    /// Bölümlü seçim (taslaktaki Üstünde / Örtsün / Sabit): kenarlıklı grup, seçili bölüm ters renkte. Seçilince
    /// <paramref name="pick"/>; <paramref name="selected"/> -1 ise hiçbiri seçili değil.
    /// </summary>
    public static Border Segmented(string name, IReadOnlyList<string> options, int selected, Action<int> pick, double minWidth = 0)
    {
        var group = $"{name}-{Guid.NewGuid():N}";
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected, MinWidth = minWidth };
            button.SetResourceReference(FrameworkElement.StyleProperty, "SegmentButton");
            button.MinWidth = minWidth;
            button.Checked += (_, _) => pick(index);
            row.Children.Add(button);
        }
        var border = new Border { Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        border.SetResourceReference(FrameworkElement.StyleProperty, "SegmentGroup");
        border.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(border, name);
        return border;
    }

    /// <summary>Tek satır metin kutusu (yer tutucu yazıyla).</summary>
    public static Grid Field(System.Windows.Controls.TextBox box, string placeholder)
    {
        // Kutunun iç payı s; yer tutucu yazının başladığı yere hizalanır (WPF payı iki kez uygular: çerçevede ve metnin içinde).
        box.Padding = Sp.T("s", "0", "s", "0");
        var hint = new TextBlock { Text = placeholder, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(box.Padding.Left * 2 + 3, 0, 0, 0), FontSize = box.FontSize };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "faintBrush");
        void Show() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Show();
        Show();
        var grid = new Grid();
        grid.Children.Add(box);
        grid.Children.Add(hint);
        return grid;
    }

    /// <summary>Okunurluk sonucunun rengi: okunur yeşil, zor turuncu, okunmaz kırmızı.</summary>
    public static string VerdictBrush(Legibility verdict) => verdict switch
    {
        Legibility.Readable => "successBrush",
        Legibility.Hard => "warnBrush",
        _ => "liveTextBrush",
    };
}

/// <summary>
/// − değer + adım düğmesi (taslak .stepper): kenarlıklı grup, 36 px kare düğmeler, ortada mono değer. Değer ve düğmelerin
/// etkinliği sonradan güncellenir (odak düğmede kalır).
/// </summary>
internal sealed class Stepper : Border
{
    private readonly TextBlock value;
    private readonly Button minus;
    private readonly Button plus;

    public Stepper(string minusName, string plusName, Action down, Action up)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 72 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        minus = Step("−", minusName, down);
        plus = Step("+", plusName, up);
        value = ScreenKit.Mono("", 14, "textBrush");
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        Grid.SetColumn(plus, 2);
        grid.Children.Add(minus);
        grid.Children.Add(value);
        grid.Children.Add(plus);
        Child = grid;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        SetResourceReference(BorderBrushProperty, "controlBorderBrush");
        HorizontalAlignment = HorizontalAlignment.Right;
    }

    private static Button Step(string glyph, string name, Action act)
    {
        var button = new Button { Content = glyph };
        button.SetResourceReference(StyleProperty, "GhostButton");
        button.Width = 36;
        button.Height = 36;
        button.MinHeight = 36;
        button.Padding = new Thickness(0);
        button.FontSize = 18;
        button.SetResourceReference(Control.ForegroundProperty, "textBrush");
        button.Click += (_, _) => act();
        AutomationProperties.SetName(button, name);
        return button;
    }

    public void Show(string text, bool canDown, bool canUp)
    {
        value.Text = text;
        minus.IsEnabled = canDown;
        plus.IsEnabled = canUp;
        AutomationProperties.SetItemStatus(this, text);
    }
}

/// <summary>
/// Kullanıcının gerçek altyazı görünümüyle bir Türkçe satır: zemin kutusu (koyuluk ve renk ayardaki gibi; zeminsizde pay
/// yok), içinde siyah kenarlıklı yazı (kalınlık ayardaki TV pikseli). <paramref name="scale"/>: TV pikselinin ekrandaki
/// boyu (1920 genişlikli TV → önizleme). Canlı önizleme ve Görünüm'deki okunurluk testi kullanır.
/// </summary>
internal static class SubtitleLine
{
    public static Border Make(string text, SubtitleStyle style, double scale, bool current = true, double wrapScale = 1)
    {
        var font = Math.Max(1, OverlayPlacement.FontSize(style.FontSize) * scale * (current ? 1 : 0.8) * wrapScale);
        var outline = style.OutlinePx <= 0 ? 0 : (current ? style.OutlinePx : Math.Max(1, Math.Round(style.OutlinePx * 0.8, MidpointRounding.AwayFromZero))) * scale;
        var line = new OutlinedText
        {
            Text = text,
            FontSize = font,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
            Fill = new SolidColorBrush(SpeakerText.Rgb(style.TextRgb)),
            Stroke = Brushes.Black,
            StrokeThickness = outline,
        };
        line.SetResourceReference(OutlinedText.FontFamilyProperty, "TextFont");
        var alpha = LivePreviewLayout.BackgroundAlpha(style) * (current ? 1 : 0.83);
        var band = style.BackgroundShape == SubtitleBackgroundShape.Band;
        return new Border
        {
            Background = alpha <= 0 ? Brushes.Transparent : new SolidColorBrush(SpeakerText.Rgb(style.BackgroundRgb, (byte)Math.Round(alpha * 255))),
            CornerRadius = new CornerRadius(band ? 0 : 10 * scale),
            // Zeminsiz görünümde kutu payı yok (satırlar taslaktaki gibi sıkı durur).
            Padding = alpha <= 0 ? new Thickness(0) : current ? new Thickness(26 * scale, 10 * scale, 26 * scale, 10 * scale) : new Thickness(20 * scale, 6 * scale, 20 * scale, 6 * scale),
            HorizontalAlignment = band ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            Child = line,
        };
    }
}
