// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Cevirgen.Core;

namespace Cevirgen.App.Views;

/// <summary>
/// GTA 6 telefonunun yanındaki sohbet paneli (Mac/iPhone PhonePanelView, Android PhonePanel, TV app.js placePanel aynı
/// çizim): yuvarlak köşeli, yarı saydam koyu kart; üstte kişinin adı, altında mesajlar sırayla (karşı taraf solda krem,
/// oyuncu sağda amber balon, not ortada). Sığmayan en eski mesajlar üstten kırpılır, son mesajlar görünür kalır. Sağ altta
/// sade kanepe simgesi.
/// </summary>
internal static class PhonePanelView
{
    public static FrameworkElement Make(ScreenPhoneCard.ChatPanel panel, double width, double height, double fontSize, double radius)
    {
        fontSize = Math.Max(8, fontSize);
        var cardStyle = panel.Card.Style;
        var foreground = Parse(cardStyle?.Foreground, Colors.White);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (panel.Title is { } title)
        {
            var name = new TextBlock
            {
                Text = title.Translation,
                FontSize = fontSize * 1.05,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Parse(title.Style?.Foreground, foreground)),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, fontSize * 0.5),
            };
            Grid.SetRow(name, 0);
            grid.Children.Add(name);
        }
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < panel.Messages.Count; i++)
            list.Children.Add(Message(panel.Messages[i], width, fontSize, fontSize * Gap(panel.Messages, i)));
        // Sığmayan en eski mesajlar üstten kırpılır: liste alta yaslı, alan kırpılmış.
        var clip = new Border { ClipToBounds = true, Child = list };
        Grid.SetRow(clip, 1);
        grid.Children.Add(clip);
        var couch = Couch(fontSize, foreground);
        var root = new Grid();
        root.Children.Add(new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Parse(cardStyle?.Background, Colors.Black)) { Opacity = ScreenPhoneCard.PanelOpacity },
            Padding = new Thickness(fontSize * 0.6, fontSize * 0.55, fontSize * 0.6, fontSize * 1.3),
            Child = grid,
        });
        root.Children.Add(couch);
        root.IsHitTestVisible = false;
        return root;
    }

    /// <summary>Sohbet listesinin satırı (kişi adı, önizleme, bölüm başlığı): sola yaslı not; ad ve başlık kalın. Mac ile aynı.</summary>
    internal static bool IsListRow(ScreenTranslation.Item item) =>
        item.Panel == ScreenPhoneCard.PanelPart.Note && item.Look?.Align == "left";

    /// <summary>
    /// Satırın üstündeki boşluk (yazı boyuna oranla): önizleme, üstündeki kişi adının hemen altındadır (Mac ile aynı).
    /// İlk satırın üstündeki boşluk eskisi gibi kalır.
    /// </summary>
    internal static double Gap(IReadOnlyList<ScreenTranslation.Item> messages, int index)
    {
        if (index <= 0) return 0.45;
        var item = messages[index];
        var previous = messages[index - 1];
        if (IsListRow(item) && IsListRow(previous) && item.Look?.Bold != true && previous.Look?.Bold == true
            && previous.Style?.Foreground != ScreenPhoneCard.NoteColor.Foreground) return 0.08;
        return 0.45;
    }

    private static FrameworkElement Message(ScreenTranslation.Item item, double width, double fontSize, double gap)
    {
        var note = item.Panel == ScreenPhoneCard.PanelPart.Note;
        var mine = item.Panel == ScreenPhoneCard.PanelPart.Mine;
        var listed = IsListRow(item);
        var text = new TextBlock
        {
            Text = item.Translation,
            FontSize = listed ? fontSize * 0.92 : note ? fontSize * 0.8 : fontSize,
            FontWeight = listed ? (item.Look?.Bold == true ? FontWeights.Bold : FontWeights.Normal)
                : note ? FontWeights.Medium : FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Parse(item.Style?.Foreground, Colors.White)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = note && !listed ? TextAlignment.Center : TextAlignment.Left,
        };
        if (listed)
            return new Border { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, gap, 0, 0), Child = text };
        if (note) return new Border { Margin = new Thickness(0, gap, 0, 0), Child = text };
        return new Border
        {
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = width * 0.84,
            Margin = new Thickness(0, gap, 0, 0),
            Padding = new Thickness(fontSize * 0.6, fontSize * 0.32, fontSize * 0.6, fontSize * 0.32),
            CornerRadius = new CornerRadius(fontSize * 0.75),
            Background = new SolidColorBrush(Parse(item.Style?.Background, Colors.Gray)),
            Child = text,
        };
    }

    /// <summary>Sağ alttaki sade kanepe (<see cref="ScreenPhoneCard.CouchGlyph"/>, 24×16 ızgara).</summary>
    private static FrameworkElement Couch(double fontSize, Color color)
    {
        double w = fontSize * 0.9, h = fontSize * 0.6, sx = w / 24, sy = h / 16;
        var geometry = new GeometryGroup();
        foreach (var part in ScreenPhoneCard.CouchGlyph)
            geometry.Children.Add(new RectangleGeometry(new Rect(part.X * sx, part.Y * sy, part.Width * sx, part.Height * sy),
                                                        part.Radius * sx, part.Radius * sy));
        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(color) { Opacity = 0.35 },
            Width = w,
            Height = h,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, fontSize * 0.45, fontSize * 0.45),
        };
    }

    internal static Color Parse(string? hex, Color fallback) =>
        hex is { Length: 7 } && hex[0] == '#'
        && int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : fallback;
}
