// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Windows.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>Şeritteki bir bildirim: tür, önem, tek satır ileti, tek eylem ve isteğe bağlı ayrıntı.</summary>
/// <param name="Signature">İleti her saniye değişse de (görüntü kaç sn'dir yok) yapı aynı kalsın diye karşılaştırılan özet;
/// null ise iletinin kendisi.</param>
/// <param name="AutoExpand">İlk göründüğünde ayrıntısı açık gelir (TV uygulaması güncellenirken ilerleme kartı).</param>
public sealed record NoticeItem(LiveNoticeKind Kind, LiveNoticeLevel Level, string Message, string? ActionLabel = null, Action? Action = null,
                                Func<FrameworkElement>? Details = null, string? DismissLabel = null, Action? Dismiss = null,
                                bool ActionEnabled = true, string? Signature = null, bool AutoExpand = false);

/// <summary>
/// Canlı ekranın bildirim şeridi (başlığın hemen altında, TV önizlemesinin üstünde): en önemli bildirim tek satırda — durum
/// noktası ve kısa etiket ("Hata", "Yapılacak", "Bilgi"; renkli sol çubuk yok, simge yok), ileti, isteğe bağlı "Ayrıntılar",
/// "+2 uyarı daha" ve tek eylem düğmesi. "+N uyarı daha" diğerlerini aynı biçimde şeridin içinde açar. Önizleme şeride göre
/// küçülür; sayfa kaymaz. Sıra ve önem Shell'de (<see cref="LiveNotices"/>).
/// </summary>
public sealed class NoticeStrip : Border
{
    private readonly StackPanel rows = new();
    private readonly HashSet<LiveNoticeKind> expanded = [];
    private readonly HashSet<LiveNoticeKind> autoExpanded = [];
    private readonly Dictionary<LiveNoticeKind, TextBlock> messages = [];
    private bool showAll;
    private string signature = "";
    private IReadOnlyList<NoticeItem> items = [];

    /// <summary>Ekran görüntüsü aracı: diğer bildirimler açık ("+N uyarı daha"'ya basılmış gibi).</summary>
    internal static bool PreviewShowAll { get; set; }

    public NoticeStrip()
    {
        showAll = PreviewShowAll;
        SetResourceReference(BackgroundProperty, "surfaceBrush");
        SetResourceReference(BorderBrushProperty, "borderBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Palette.Radius.GetValueOrDefault("control", 6));
        Padding = Sp.T("s", "0", "xs", "0");
        Child = rows;
        Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(this, "Bildirimler");
    }

    /// <summary>Eylem düğmesi sarı (ana) mı: ekranda başka sarı düğme yokken ve bildirim bilgi değilken.</summary>
    public bool PrimaryAllowed { get; set; }

    /// <summary>Açık bildirimlerin üstündeki en önemli bildirimin önemi (şerit boşsa null).</summary>
    public LiveNoticeLevel? TopLevel => items.Count == 0 ? null : items[0].Level;

    /// <summary>Bildirimleri gösterir; yapı değişmediyse yalnız iletiler yenilenir (odak ve açık ayrıntılar korunur).</summary>
    public void Show(IReadOnlyList<NoticeItem> next)
    {
        items = next;
        foreach (var item in next.Where(item => item.AutoExpand && item.Details is not null))
            if (autoExpanded.Add(item.Kind)) expanded.Add(item.Kind);
        autoExpanded.IntersectWith(next.Where(item => item.AutoExpand).Select(item => item.Kind));
        var key = string.Join("|", next.Select(item => $"{item.Kind}:{item.Signature ?? item.Message}:{item.ActionLabel}:{item.ActionEnabled}:{item.DismissLabel}"))
                  + $"#{showAll}#{string.Join(",", expanded.Order())}#{PrimaryAllowed}";
        if (key == signature)
        {
            foreach (var item in next)
                if (messages.TryGetValue(item.Kind, out var text) && text.Text != item.Message) text.Text = item.Message;
            return;
        }
        signature = key;
        Rebuild();
    }

    private void Rebuild()
    {
        // Odak şeritteyse aynı bildirimin aynı düğmesine geri döner.
        var focused = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
        var focusTag = focused is not null && IsAncestorOf(focused) ? focused.Tag as string : null;
        rows.Children.Clear();
        messages.Clear();
        expanded.IntersectWith(items.Select(item => item.Kind));
        if (items.Count <= 1) showAll = false;
        Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Count == 0) return;
        var visible = showAll ? items : items.Take(1);
        var first = true;
        foreach (var item in visible)
        {
            if (!first)
            {
                var line = new Border { Height = 1, Margin = Sp.T("0", "0", "xs", "0") };
                line.SetResourceReference(BackgroundProperty, "hairlineBrush");
                rows.Children.Add(line);
            }
            rows.Children.Add(Row(item, first ? items.Count : 0));
            if (expanded.Contains(item.Kind) && item.Details?.Invoke() is { } details)
            {
                // Ayrıntı iletinin hizasında başlar (etiket sütununun genişliği); "96" boşluk belirteci değildir, sayı olarak verilir.
                var pad = Sp.T("0", "0", "s", "s");
                details.Margin = new Thickness(LabelWidth, 0, pad.Right, pad.Bottom);
                rows.Children.Add(details);
            }
            first = false;
        }
        if (focusTag is not null && FindTagged(rows, focusTag) is { } again) again.Focus();
    }

    private const double LabelWidth = 96;

    private FrameworkElement Row(NoticeItem item, int total)
    {
        var grid = new Grid { MinHeight = 48 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Durum: nokta + kısa etiket (renk tek modelden: hata kırmızı, yapılacak sarı-turuncu, bilgi gri).
        var state = LiveNotices.State(item.Level);
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(DeviceStatus.Dot(state, 8));
        var name = new TextBlock { Text = LiveNotices.Label(item.Level), FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, item.Level switch
        {
            LiveNoticeLevel.Blocking => "liveTextBrush",
            LiveNoticeLevel.Action => "warnBrush",
            _ => "mutedBrush",
        });
        label.Children.Add(name);
        grid.Children.Add(label);

        var message = new TextBlock
        {
            Text = item.Message, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, MaxHeight = 40, Margin = Sp.T("0", "xs", "m", "xs"),
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        message.SetResourceReference(TextBlock.FontFamilyProperty, "TextFont");
        message.ToolTip = item.Message;
        messages[item.Kind] = message;
        Grid.SetColumn(message, 1);
        grid.Children.Add(message);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (item.Details is not null)
        {
            var open = expanded.Contains(item.Kind);
            tools.Children.Add(Link(open ? "Ayrıntıları gizle" : "Ayrıntılar", $"details:{item.Kind}", () =>
            {
                if (!expanded.Remove(item.Kind)) expanded.Add(item.Kind);
                signature = "";
                Show(items);
            }));
        }
        if (LiveNotices.MoreText(total) is { } more)
            tools.Children.Add(Link(showAll ? "Diğerlerini gizle" : more, "more", () =>
            {
                showAll = !showAll;
                signature = "";
                Show(items);
            }));
        if (item.DismissLabel is { } dismissLabel && item.Dismiss is { } dismiss)
            tools.Children.Add(Link(dismissLabel, $"dismiss:{item.Kind}", dismiss));
        if (item.ActionLabel is { } actionLabel && item.Action is { } action)
        {
            var button = new Button
            {
                Content = actionLabel, MinHeight = 32, Padding = Sp.T("s", "0", "s", "0"), Margin = Sp.T("xs", "xs", "0", "xs"),
                IsEnabled = item.ActionEnabled, Tag = $"action:{item.Kind}", VerticalAlignment = VerticalAlignment.Center,
            };
            button.SetResourceReference(StyleProperty, PrimaryAllowed && item.Level != LiveNoticeLevel.Info && total > 0 ? "PrimaryButton" : "OutlineButton");
            button.Click += (_, _) => action();
            tools.Children.Add(button);
        }
        Grid.SetColumn(tools, 2);
        grid.Children.Add(tools);
        return grid;
    }

    private static Button Link(string text, string tag, Action click)
    {
        var link = new Button { Content = text, Tag = tag, Margin = Sp.T("0", "0", "m", "0") };
        link.SetResourceReference(StyleProperty, "TextLink");
        link.SetResourceReference(Control.ForegroundProperty, "mutedBrush");
        link.Click += (_, _) => click();
        return link;
    }

    private static FrameworkElement? FindTagged(DependencyObject root, string tag)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element && element.Tag as string == tag) return element;
            if (FindTagged(child, tag) is { } found) return found;
        }
        return null;
    }
}
