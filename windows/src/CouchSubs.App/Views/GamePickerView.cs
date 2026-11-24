// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>
/// "Hangi oyunu oynuyorsun?" (Çevirgen): aranabilir dikey (2:3) karo ızgarası. Önce son oynananlar, sonra paketi kurulu
/// oyunlar, sunucudaki oyunlar ve popüler oyunlar (<see cref="GamePicker"/>); en sonda "Listede yok". Kapak varsa kapak
/// (kullanıcının kendi kapağı önce, sonra sunucudaki), yoksa koyu düz yüzeyde Archivo Condensed başlıklı yazı karosu
/// (degrade ve rastgele renk yok). Klavye: arama kutusunda ↓ ilk karoya, oklar karolar arasında, Enter seçer, Esc kapatır.
/// </summary>
public sealed class GamePickerView : Border
{
    private const double TileWidth = 124;
    private const double CoverHeight = TileWidth * 1.5;
    private const double Gap = 16;

    private readonly AppController controller;
    private readonly System.Windows.Controls.TextBox search = new() { MinHeight = 40, FontSize = 15 };
    private readonly TextBlock searchHint = new() { Text = "Oyun ara: ad ya da kısaltma (GTA 6, RDR2…)", FontSize = 15, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel grid = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly TextBlock count = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<GameTile> tiles = [];

    public GamePickerView(AppController controller)
    {
        this.controller = controller;
        SetResourceReference(BackgroundProperty, "surfaceBrush");
        SetResourceReference(BorderBrushProperty, "borderBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Palette.Radius.GetValueOrDefault("control", 6));
        Padding = Sp.T("l", "l", "l", "m");
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var root = new DockPanel { LastChildFill = true };
        var head = new StackPanel();
        var label = new TextBlock { Text = "Oynanan oyun" };
        label.SetResourceReference(StyleProperty, "MonoLabel");
        head.Children.Add(label);
        var title = new TextBlock { Text = "Hangi oyunu oynuyorsun?", Margin = Sp.T("0", "xxs", "0", "s") };
        title.SetResourceReference(StyleProperty, "ScreenTitle");
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level1);
        head.Children.Add(title);
        var box = new Grid();
        System.Windows.Automation.AutomationProperties.SetName(search, "Oyun ara");
        search.TextChanged += (_, _) => Refresh();
        search.PreviewKeyDown += OnSearchKey;
        box.Children.Add(search);
        searchHint.SetResourceReference(TextBlock.ForegroundProperty, "faintBrush");
        searchHint.Margin = Sp.T("s", "0", "0", "0");
        box.Children.Add(searchHint);
        head.Children.Add(box);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // Alt satır: satırların havuza gittiği bilgisi ve "Sorma".
        var foot = new DockPanel { Margin = Sp.T("0", "s", "0", "0") };
        var dontAsk = new Button { Content = "Bu oturumda sorma" };
        dontAsk.SetResourceReference(StyleProperty, "TextLink");
        dontAsk.SetResourceReference(Control.ForegroundProperty, "mutedBrush");
        dontAsk.Click += (_, _) => controller.DismissGame();
        dontAsk.Margin = Sp.T("m", "0", "0", "0");
        DockPanel.SetDock(dontAsk, Dock.Right);
        foot.Children.Add(dontAsk);
        count.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
        count.Margin = Sp.T("0", "0", "m", "0");
        DockPanel.SetDock(count, Dock.Left);
        foot.Children.Add(count);
        var note = new TextBlock
        {
            Text = "Paketi olmayan satırlar oyunun adıyla ortak havuza gider; o oyunun hazır çevirisi böylece daha erken gelir.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "faintBrush");
        foot.Children.Add(note);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        var rule = new Border { Height = 1, Margin = Sp.T("0", "s", "0", "0") };
        rule.SetResourceReference(BackgroundProperty, "hairlineBrush");
        DockPanel.SetDock(rule, Dock.Bottom);
        root.Children.Add(rule);

        grid.Margin = Sp.T("0", "m", "0", "0");
        scroll.Content = grid;
        root.Children.Add(scroll);
        Child = root;
        PreviewKeyDown += OnKey;
    }

    /// <summary>Kaydırma çubuğu ve kenar boşlukları (kartın iç genişliğine eklenir).</summary>
    private const double Chrome = 2 * 24 + 2 + 14;

    /// <summary>
    /// Kart, içine sığan tam sütun sayısına göre genişler (3–6 sütun; satır sonunda boşluk kalmaz) ve pencereye sığar.
    /// </summary>
    public void FitTo(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var room = width - Margin.Left - Margin.Right - Chrome;
        var columns = Math.Clamp((int)Math.Floor((room + Gap) / (TileWidth + Gap)), 3, 6);
        Width = Math.Min(width - Margin.Left - Margin.Right, columns * (TileWidth + Gap) + Chrome);
        MaxHeight = Math.Max(320, height - Margin.Top - Margin.Bottom);
    }

    /// <summary>Kart açıldı: arama temizlenir, karolar yenilenir, arama kutusu odak alır.</summary>
    public void Open()
    {
        search.Text = "";
        Refresh();
        scroll.ScrollToTop();
        Dispatcher.BeginInvoke(() => search.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Karolar (arama, son oynanan, kapaklar değişince).</summary>
    public void Refresh()
    {
        var typed = search.Text.Trim();
        searchHint.Visibility = typed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tiles = controller.GameTiles(typed);
        grid.Children.Clear();
        foreach (var tile in tiles)
        {
            var button = Tile(tile);
            button.Click += (_, _) => controller.ChooseGame(tile.Name);
            grid.Children.Add(button);
        }
        var custom = CustomTile(typed);
        custom.Click += (_, _) => ChooseTyped();
        grid.Children.Add(custom);
        count.Text = typed.Length == 0 ? $"{tiles.Count} oyun" : tiles.Count == 0 ? "Eşleşen oyun yok" : $"{tiles.Count} sonuç";
    }

    private void ChooseTyped()
    {
        if (search.Text.Trim() is { Length: > 0 } typed) controller.ChooseGame(typed);
        else search.Focus();
    }

    // ───── Karolar ─────

    private static Button Plain(string name)
    {
        var button = new Button { Width = TileWidth, Margin = new Thickness(0, 0, Gap, Gap), Cursor = Cursors.Hand, Template = Controls.GameCover.Plain() };
        button.SetResourceReference(FocusVisualStyleProperty, "FocusRingTile");
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button Tile(GameTile tile)
    {
        var button = Plain(tile.Name);
        var stack = new StackPanel();
        var frame = new Border { Width = TileWidth, Height = CoverHeight, BorderThickness = new Thickness(tile.Selected ? 2 : 1), ClipToBounds = true };
        if (tile.Selected) frame.SetResourceReference(Border.BorderBrushProperty, "amberBrush");
        else frame.BorderBrush = CoverInk.Edge;
        frame.Background = CoverInk.Panel;
        frame.Child = tile.CoverFile is { } file && LoadImage(file) is { } image
            ? new Image { Source = image, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : Typographic(tile.Name);
        stack.Children.Add(frame);
        var name = new TextBlock
        {
            Text = tile.Name, FontSize = 13, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36, Margin = Sp.T("0", "xs", "0", "0"),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        stack.Children.Add(name);
        var tag = tile.Selected ? "Şu an oynanan" : GamePicker.TagText(tile.Tag);
        var caption = new TextBlock { Text = tag ?? " ", FontSize = 12, Margin = Sp.T("0", "line", "0", "0") };
        caption.SetResourceReference(TextBlock.ForegroundProperty, tile.Selected ? "amberTextBrush" : "mutedBrush");
        stack.Children.Add(caption);
        button.Content = stack;
        button.ToolTip = tile.Name;
        AttachHover(button, frame, tile.Selected);
        return button;
    }

    /// <summary>Kapaksız karo: koyu düz yüzey, kıl çizgi ve altta Archivo Condensed başlık (temadan bağımsız, kapak gibi koyu).</summary>
    private static FrameworkElement Typographic(string name)
    {
        var panel = new Grid();
        var size = TitleSize(name);
        var text = new TextBlock
        {
            Text = name, FontSize = size, FontWeight = FontWeights.Bold, LineHeight = Math.Round(size * 1.05),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = CoverInk.Title, VerticalAlignment = VerticalAlignment.Bottom, Margin = Sp.T("s", "0", "xs", "s"), MaxHeight = CoverHeight - 40,
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        panel.Children.Add(text);
        var rule = new Border { Height = 1, Width = 28, Background = CoverInk.Rule, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = Sp.T("s", "s", "0", "0") };
        panel.Children.Add(rule);
        return panel;
    }

    /// <summary>
    /// Yazı karosunun punto boyu: uzun adlarda küçülür ve en uzun sözcük satıra sığar ("Redemption" heceden bölünmez).
    /// </summary>
    private static double TitleSize(string name)
    {
        const double room = TileWidth - 12 - 8 - 2;
        var size = name.Length > 28 ? 19.0 : 23.0;
        if (Application.Current?.TryFindResource("DisplayFont") is not FontFamily family) return size;
        var face = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = new FormattedText(word, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size,
                Brushes.White, 1).WidthIncludingTrailingWhitespace;
            if (width > room) size = Math.Max(14, Math.Floor(size * room / width));
        }
        return size;
    }

    /// <summary>"Listede yok": yazılan adla devam eder; bir şey yazılmadıysa arama kutusuna odaklanır.</summary>
    private static Button CustomTile(string typed)
    {
        var button = Plain("Listede yok");
        var stack = new StackPanel();
        var frame = new Border { Width = TileWidth, Height = CoverHeight, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "controlBorderBrush");
        frame.Background = Brushes.Transparent;
        var inside = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("s", "0", "s", "0") };
        var title = new TextBlock { Text = "Listede yok", FontSize = 21, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        inside.Children.Add(title);
        var hint = new TextBlock
        {
            Text = typed.Length > 0 ? $"“{typed}” adıyla devam et" : "Adını arama kutusuna yaz", FontSize = 12, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 64, Margin = Sp.T("0", "xs", "0", "0"),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "mutedBrush");
        inside.Children.Add(hint);
        frame.Child = inside;
        stack.Children.Add(frame);
        var caption = new TextBlock { Text = "Kendi adını yaz", FontSize = 13, FontWeight = FontWeights.Medium, Margin = Sp.T("0", "xs", "0", "0") };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "textBrush");
        stack.Children.Add(caption);
        button.Content = stack;
        AttachHover(button, frame, selected: false);
        return button;
    }

    private static void AttachHover(Button button, Border frame, bool selected)
    {
        if (selected) return;
        var normal = frame.BorderBrush;
        button.MouseEnter += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, "borderStrongBrush");
        button.MouseLeave += (_, _) =>
        {
            if (normal == CoverInk.Edge) frame.BorderBrush = CoverInk.Edge;
            else frame.SetResourceReference(Border.BorderBrushProperty, "controlBorderBrush");
        };
    }

    /// <summary>Kapak dosyası (en çok 2 MB, imzası denetlenmiş); okunamazsa null (yazı karosu).</summary>
    private static BitmapSource? LoadImage(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = (int)(TileWidth * 2);
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException
                                          or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    // ───── Klavye ─────

    private List<Button> Buttons => [.. grid.Children.OfType<Button>()];

    /// <summary>Satırdaki karo sayısı (ızgaranın genişliğinden).</summary>
    private int Columns => Math.Max(1, (int)((grid.ActualWidth + 0.5) / (TileWidth + Gap)));

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (GamePicker.EnterChoice(search.Text, tiles, controller.RemoteGames) is { } name) controller.ChooseGame(name);
                e.Handled = true;
                break;
            case Key.Down:
                if (Buttons.FirstOrDefault() is { } first) Focus(first);
                e.Handled = true;
                break;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            controller.DismissGame();
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is not Button current || grid.Children.IndexOf(current) is var index && index < 0) return;
        var buttons = Buttons;
        var columns = Columns;
        int? next = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => index - columns,
            Key.Down => Math.Min(index + columns, buttons.Count - 1),
            Key.Home => 0,
            Key.End => buttons.Count - 1,
            _ => null,
        };
        if (e.Key == Key.Enter)
        {
            current.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            e.Handled = true;
            return;
        }
        if (next is not { } target) return;
        e.Handled = true;
        if (target < 0)
        {
            search.Focus();
            return;
        }
        if (target < buttons.Count) Focus(buttons[target]);
    }

    private static void Focus(Button button)
    {
        // Oklar FocusCue'yu açar: odak halkası yalnız klavyeyle çizilir.
        button.Focus();
        button.BringIntoView();
    }
}

/// <summary>Kapak karolarının temadan bağımsız koyu renkleri (kapaklar gibi her temada koyu).</summary>
internal static class CoverInk
{
    public static readonly Brush Panel = Frozen(0x16, 0x18, 0x1E);
    public static readonly Brush Title = Frozen(0xEC, 0xED, 0xEF);
    public static readonly Brush Rule = Frozen(0x3A, 0x3F, 0x4B);
    public static readonly Brush Edge = Frozen(0x2A, 0x2E, 0x37);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
