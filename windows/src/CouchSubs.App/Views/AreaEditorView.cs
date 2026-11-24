// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;
using Frame = CouchSubs.Core.Frame;

namespace CouchSubs.App.Views;

/// <summary>
/// Alanları düzenle (taslak: design/cevirgen/arayuz-taslagi/Areas.dc.html): pencerenin tamamını kaplayan düzenleme hâli.
/// Üstte "çevirgen · Alanları düzenle · oyunun adı", "Varsayılana dön", "Vazgeç" ve "Bitti". Solda son oyun karesi (yoksa düz
/// bir yer tutucu sahne) ve üstünde kutular: altyazı alanı (sarı) ve ek alanlar (kesik çizgili); fareyle taşınır, köşe ve
/// kenarlardan boyutlanır, seçili kutu ok tuşlarıyla kayar (Shift büyük adım, Ctrl boyut), Delete ek alanı siler. Altında
/// "+ Kutu ekle" ve (oyunun hazır alanları varsa) "Oyundan otomatik bul". Sağda her kutunun adı, ne okuduğu, ek alanda
/// "Sürekli / Kısayolla" ve yüzde koordinatları. "Bitti" alanları kaydeder (oyunun profiline; <see cref="AppController.ApplyEditedAreas"/>).
/// </summary>
public sealed class AreaEditorView : UserControl
{
    /// <summary>Ekran görüntüsü aracı: açılışta seçili kutu.</summary>
    internal static string? PreviewSelected { get; set; }

    private readonly AppController controller;
    private readonly AreaEditModel model;
    private readonly Action close;
    private readonly AreaSurface surface;
    private readonly StackPanel list = new();
    private readonly Button autoFind;
    private readonly Button add;
    private readonly string? game;

    /// <param name="close">Düzenleme bitince (kaydedildi ya da vazgeçildi) çağrılır.</param>
    public AreaEditorView(AppController controller, Action close)
    {
        this.controller = controller;
        this.close = close;
        controller.NoteScreen("areas");
        game = controller.AreaEditGame;
        model = new AreaEditModel(controller.Settings.Areas, controller.Settings.KeepAreaCentered);
        if (PreviewSelected is { } chosen) model.Select(chosen);
        surface = new AreaSurface(model, controller.LastFrame);
        surface.Changed += ShowList;
        add = ScreenKit.Button("+ Kutu ekle", "OutlineButton", (_, _) =>
        {
            model.Add();
            Refresh();
            surface.Focus();
        });
        autoFind = ScreenKit.Button("Oyundan otomatik bul", "OutlineButton", (_, _) =>
        {
            model.AutoFind(game);
            Refresh();
        });

        var root = new DockPanel { LastChildFill = true };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 560 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        body.Children.Add(BuildStage());
        var rule = ScreenKit.Rule(vertical: true);
        Grid.SetColumn(rule, 1);
        body.Children.Add(rule);
        var aside = BuildAside();
        Grid.SetColumn(aside, 2);
        body.Children.Add(aside);
        root.Children.Add(body);
        Content = root;
        Focusable = false;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                close();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => surface.Focus();
        Refresh();
    }

    private FrameworkElement BuildHeader()
    {
        var bar = new DockPanel { Margin = Sp.T("edge", "0", "edge", "0"), MinHeight = 64, LastChildFill = false };
        var mark = new Wordmark { VerticalAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "0", "l", "0") };
        DockPanel.SetDock(mark, Dock.Left);
        bar.Children.Add(mark);
        var title = ScreenKit.Text($"Alanları düzenle · {game ?? "Genel alanlar"}", 15, FontWeights.SemiBold, wrap: false);
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        DockPanel.SetDock(title, Dock.Left);
        bar.Children.Add(title);
        var done = ScreenKit.Button("Bitti", "PrimaryButton", (_, _) =>
        {
            controller.ApplyEditedAreas(model.ToAreaSet());
            close();
        });
        done.MinHeight = 36;
        done.Padding = Sp.T("m", "0", "m", "0");
        done.FontSize = 14;
        done.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(done, Dock.Right);
        bar.Children.Add(done);
        var cancel = ScreenKit.Button("Vazgeç", "OutlineButton", (_, _) => close());
        cancel.VerticalAlignment = VerticalAlignment.Center;
        cancel.Margin = Sp.T("0", "0", "xs", "0");
        DockPanel.SetDock(cancel, Dock.Right);
        bar.Children.Add(cancel);
        var reset = ScreenKit.Button("Varsayılana dön", "OutlineButton", (_, _) =>
        {
            model.ResetToDefaults();
            Refresh();
        });
        reset.VerticalAlignment = VerticalAlignment.Center;
        reset.Margin = Sp.T("0", "0", "xs", "0");
        DockPanel.SetDock(reset, Dock.Right);
        bar.Children.Add(reset);
        var frame = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        return frame;
    }

    private FrameworkElement BuildStage()
    {
        var stack = new DockPanel { Margin = Sp.T("edge", "l", "edge", "l"), LastChildFill = true };
        var hint = ScreenKit.Muted(controller.LastFrame is null
            ? "Kutuyu tutup taşı, köşesinden çekip büyüt. Çevirgen yalnız kutuların içini okur. Henüz oyun karesi yok; çeviri başlayınca son kare burada görünür."
            : "Kutuyu tutup taşı, köşesinden çekip büyüt. Çevirgen yalnız kutuların içini okur. Seçili kutu ok tuşlarıyla kayar (Shift: büyük adım, Ctrl: boyut).", 14);
        hint.Margin = Sp.T("0", "0", "0", "s");
        DockPanel.SetDock(hint, Dock.Top);
        stack.Children.Add(hint);
        // Kare kalan yüksekliğe sığar; düğmeler hemen altında durur (pencerenin dibine yapışmaz).
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = Sp.T("0", "s", "0", "0") };
        buttons.Children.Add(add);
        autoFind.Margin = Sp.T("xs", "0", "0", "0");
        buttons.Children.Add(autoFind);
        var frame = new Border { BorderThickness = new Thickness(1), Child = surface };
        frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        var aspect = new AspectBox { Ratio = surface.Ratio, Child = frame, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        var column = new StackPanel();
        column.Children.Add(aspect);
        column.Children.Add(buttons);
        var area = new Grid();
        area.Children.Add(column);
        area.SizeChanged += (_, _) =>
        {
            buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            aspect.MaxHeight = Math.Max(120, area.ActualHeight - buttons.DesiredSize.Height - buttons.Margin.Top);
            aspect.Width = Math.Min(area.ActualWidth, aspect.MaxHeight * surface.Ratio);
        };
        stack.Children.Add(area);
        return stack;
    }

    private FrameworkElement BuildAside()
    {
        var stack = new DockPanel { Margin = Sp.T("l", "l", "l", "l"), LastChildFill = true };
        var heading = ScreenKit.Label(game is null ? "Alanlar" : "Bu oyunun alanları");
        heading.Margin = Sp.T("0", "0", "0", "xs");
        DockPanel.SetDock(heading, Dock.Top);
        stack.Children.Add(heading);
        var note = ScreenKit.Muted(game is null
            ? "Oyun bilinmiyor: alanlar genel ayar olarak saklanır. Oyun tanınınca o oyunun alanları ayrı saklanır."
            : "Alanlar her oyun için ayrı saklanır. Oyun değişince Çevirgen o oyunun alanlarını kendisi yükler.", 13);
        note.Margin = Sp.T("0", "s", "0", "0");
        DockPanel.SetDock(note, Dock.Bottom);
        stack.Children.Add(note);
        AutomationProperties.SetName(list, "Alanlar");
        stack.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = list,
        });
        return stack;
    }

    private void Refresh()
    {
        surface.InvalidateVisual();
        ShowList();
    }

    /// <summary>Sağdaki liste: her kutunun işareti, numarası ve adı, ne okuduğu, ek alanda okuma zamanı, koordinatlar ve Sil.</summary>
    private void ShowList()
    {
        add.IsEnabled = model.CanAdd;
        autoFind.Visibility = AreaEditModel.CanAutoFind(game) ? Visibility.Visible : Visibility.Collapsed;
        // Ad yazılırken liste yeniden kurulmaz (odak kutuda kalsın); kutudan çıkınca kurulur.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox typing && list.IsAncestorOf(typing)) return;
        list.Children.Clear();
        foreach (var box in model.Boxes)
        {
            var selected = box.Id == model.Selected;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Border { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top, Margin = Sp.T("0", "xxs", "0", "0") };
            if (box.Main) swatch.Background = new SolidColorBrush(AreaSurface.Amber);
            else
            {
                swatch.BorderThickness = new Thickness(2);
                swatch.SetResourceReference(Border.BorderBrushProperty, "textBrush");
            }
            grid.Children.Add(swatch);
            var body = new StackPanel { Margin = Sp.T("s", "0", "s", "0") };
            var name = $"{model.Number(box)} · {model.DisplayName(box)}";
            if (selected && !box.Main)
            {
                // Seçili ek alanın adı yerinde değişir ("2 · " numarası önünde kalır).
                var field = new System.Windows.Controls.TextBox { Text = box.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, MinHeight = 34 };
                AutomationProperties.SetName(field, "Kutunun adı");
                var id = box.Id;
                field.TextChanged += (_, _) =>
                {
                    model.Rename(id, field.Text);
                    surface.InvalidateVisual();
                };
                field.LostFocus += (_, _) => ShowList();
                var named = new DockPanel();
                var number = ScreenKit.Text($"{model.Number(box)} ·", 15, FontWeights.SemiBold, wrap: false);
                number.VerticalAlignment = VerticalAlignment.Center;
                number.Margin = Sp.T("0", "0", "xs", "0");
                DockPanel.SetDock(number, Dock.Left);
                named.Children.Add(number);
                named.Children.Add(ScreenKit.Field(field, $"Kutu {model.Number(box)}"));
                body.Children.Add(named);
            }
            else
            {
                body.Children.Add(ScreenKit.Text(name, 15, FontWeights.SemiBold, wrap: false));
            }
            var what = ScreenKit.Muted(box.Main ? MainDescription() : Describe(box), 13);
            what.LineHeight = 19;
            what.Margin = Sp.T("0", "line", "0", "0");
            body.Children.Add(what);
            if (!box.Main && !box.Live && ShortcutKeys(box) is { } keys)
            {
                var shortcut = ScreenKit.Mono(keys, 12, "textBrush");
                shortcut.Margin = Sp.T("0", "xxs", "0", "0");
                body.Children.Add(shortcut);
            }
            if (!box.Main)
            {
                var mode = ScreenKit.Segmented("Ne zaman okunsun", ["Sürekli", "Kısayolla"], box.Live ? 0 : 1, index =>
                {
                    model.SetLive(box.Id, index == 0);
                    Dispatcher.BeginInvoke(ShowList);
                });
                mode.Margin = Sp.T("0", "xs", "0", "0");
                body.Children.Add(mode);
                if (!box.Live)
                {
                    var where = ScreenKit.Label("Sonuç nerede");
                    where.Margin = Sp.T("0", "s", "0", "xxs");
                    body.Children.Add(where);
                    var target = ScreenKit.Segmented("Sonuç nerede", ["TV", "Bilgisayar", "İkisi"], (int)box.Target,
                        index => model.SetTarget(box.Id, (ResultTarget)index), minWidth: 64);
                    body.Children.Add(target);
                }
            }
            var coordinates = ScreenKit.Mono(AreaEditModel.Coordinates(box.Region), 12);
            coordinates.Margin = Sp.T("0", "xs", "0", "0");
            body.Children.Add(coordinates);
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            if (!box.Main)
            {
                var remove = ScreenKit.Button("Sil", "OutlineButton", (_, _) =>
                {
                    model.Remove(box.Id);
                    Refresh();
                }, $"{model.DisplayName(box)} alanını sil");
                remove.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);
            }
            var row = new Border { Child = grid, Padding = Sp.T("xs", "item", "xs", "item"), BorderThickness = new Thickness(0, 1, 0, 0), Cursor = Cursors.Hand };
            row.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
            if (selected) row.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
            else row.Background = Brushes.Transparent;
            var chosen = box.Id;
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && Ancestor<ButtonBase>(source) is not null) return;
                if (e.OriginalSource is DependencyObject text && Ancestor<System.Windows.Controls.TextBox>(text) is not null) return;
                model.Select(chosen);
                Refresh();
            };
            AutomationProperties.SetName(row, $"{name}{(selected ? ", seçili" : "")}");
            list.Children.Add(row);
        }
    }

    /// <summary>Altyazı alanının açıklaması, Türkçenin yerine göre (Görünüm › Konum).</summary>
    private string MainDescription() => "Konuşmalar. Türkçe " + controller.Settings.Style.Placement switch
    {
        SubtitlePlacement.Cover => "İngilizcenin üstüne, onu örterek çıkar.",
        SubtitlePlacement.Below => "İngilizcenin altında çıkar.",
        SubtitlePlacement.Fixed => "ekranda sabit bir yerde çıkar.",
        _ => "İngilizcenin üstünde çıkar.",
    };

    /// <summary>Ek alanın ne zaman okunduğu: sürekli ya da kısayolla (ilk üç alanın kısayolu kullanıcının tuşlarıyla).</summary>
    private string Describe(AreaBox box)
    {
        if (box.Live) return "Çeviri sürerken saniyede bir okunur; çevirisi yazının yanında kalır.";
        if (model.ShortcutSlot(box) is null) return "Kısayolu yok: yalnız ilk üç ek alanın kısayolu vardır.";
        return ShortcutKeys(box) is null ? "Kısayolu atanmamış (Ayarlar › Kısayollar)." : "Kısayola basınca bir kez okunur:";
    }

    /// <summary>Kısayolla okunan alanın tuşları ("Num 1 · Ctrl+Shift+1"); kısayolu yoksa null.</summary>
    private string? ShortcutKeys(AreaBox box)
    {
        if (model.ShortcutSlot(box) is not { } slot) return null;
        var action = slot switch { 1 => HotkeyAction.ExtraRegion1, 2 => HotkeyAction.ExtraRegion2, _ => HotkeyAction.ExtraRegion3 };
        var keys = HotkeyTable.KeysFor(controller.Settings.Hotkeys, action);
        return keys.Count == 0 ? null : string.Join(" · ", keys.Select(key => key.Label));
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T) node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}

/// <summary>
/// Alan düzenleyicisinin yüzeyi: oyun karesi (yoksa yer tutucu sahne) ve kutular; fare ve klavyeyle düzenleme. Çizim
/// <see cref="OnRender"/>'da; kutu kuralları <see cref="AreaEditModel"/> ve <see cref="RegionEdit"/>'te.
/// </summary>
internal sealed class AreaSurface : FrameworkElement
{
    public static readonly Color Amber = Color.FromRgb(0xFF, 0xD2, 0x3F);
    private static readonly Color Light = Color.FromRgb(0xEC, 0xED, 0xEF);
    private static readonly Color Ink = Color.FromRgb(0x0F, 0x10, 0x14);
    /// <summary>Tutma payı (px).</summary>
    private const double Grab = 8;

    private readonly AreaEditModel model;
    private readonly BitmapSource? image;
    private (string Id, CropRegion Start, RegionEdit.Handle Handle, Point From)? drag;

    public AreaSurface(AreaEditModel model, Frame? frame)
    {
        this.model = model;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        if (frame is { Width: > 0, Height: > 0 })
        {
            var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);
            bitmap.Freeze();
            image = bitmap;
        }
        AutomationProperties.SetName(this, "Oyun karesi ve okuma alanları. Ok tuşları seçili kutuyu kaydırır.");
    }

    /// <summary>Kutu taşındı ya da boyutlandı (sağdaki koordinatlar yenilenir).</summary>
    public event Action? Changed;

    /// <summary>Karenin oranı (kare yoksa 16:9).</summary>
    public double Ratio => image is { PixelHeight: > 0 } bitmap ? (double)bitmap.PixelWidth / bitmap.PixelHeight : 16.0 / 9.0;

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        if (image is not null) dc.DrawImage(image, new Rect(size));
        else DrawPlaceholder(dc, size);
        // Seçili kutu en üstte çizilir.
        foreach (var box in model.Boxes.Where(box => box.Id != model.Selected).Append(model.SelectedBox).OfType<AreaBox>())
            DrawBox(dc, box, size, box.Id == model.Selected);
    }

    /// <summary>Kare yokken düz sahne (taslaktaki gökyüzü, ufuk ve bir gölge; degrade yok).</summary>
    private static void DrawPlaceholder(DrawingContext dc, Size size)
    {
        dc.DrawRectangle(Fill(Color.FromRgb(0x3C, 0x42, 0x4C)), null, new Rect(size));
        dc.DrawRectangle(Fill(Color.FromRgb(0x75, 0x87, 0x9A)), null, new Rect(0, 0, size.Width, size.Height * 0.48));
        dc.DrawRectangle(Fill(Color.FromRgb(0x55, 0x60, 0x6E)), null, new Rect(0, size.Height * 0.48, size.Width, size.Height * 0.07));
        dc.DrawRectangle(Fill(Color.FromRgb(0x2E, 0x33, 0x3B)), null, new Rect(size.Width * 0.14, size.Height * 0.28, size.Width * 0.18, size.Height * 0.44));
    }

    private void DrawBox(DrawingContext dc, AreaBox box, Size size, bool selected)
    {
        var r = Rect(box.Region, size);
        var color = box.Main ? Amber : Light;
        var pen = new Pen(Fill(color), 2);
        if (!box.Main && !selected) pen.DashStyle = new DashStyle([3, 2], 0);
        pen.Freeze();
        dc.DrawRectangle(Fill(Color.FromArgb((byte)(box.Main ? 0x1A : 0x10), color.R, color.G, color.B)), pen, r);
        // Etiket: "1 · Altyazı", kutunun üstünde (yer yoksa altında).
        var text = new FormattedText($"{model.Number(box)} · {model.DisplayName(box)}", CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Fill(Ink),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (TryFindResource("MonoFont") is FontFamily mono)
            text = new FormattedText(text.Text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
                new Typeface(mono, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 12, Fill(Ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var tagHeight = text.Height + 4;
        var tagTop = r.Top - tagHeight - 1 >= 0 ? r.Top - tagHeight - 1 : r.Bottom + 1;
        var tag = new Rect(Math.Min(r.Left - 1, size.Width - text.Width - 16), tagTop, text.Width + 16, tagHeight);
        dc.DrawRectangle(Fill(color), null, tag);
        dc.DrawText(text, new Point(tag.Left + 8, tag.Top + 2));
        if (!selected) return;
        var handle = new Pen(Fill(Ink), 1);
        handle.Freeze();
        foreach (var (x, y) in RegionEdit.Handles(box.Region))
            dc.DrawRectangle(Fill(color), handle, new Rect(x * size.Width - 5, y * size.Height - 5, 10, 10));
    }

    private static Rect Rect(CropRegion region, Size size) =>
        new(region.Left * size.Width, region.Top * size.Height, Math.Max(1, (region.Right - region.Left) * size.Width), Math.Max(1, (region.Bottom - region.Top) * size.Height));

    private static SolidColorBrush Fill(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Noktadaki kutu ve tutamacı: önce seçili kutu, sonra üstteki (sonra eklenen) kutular.</summary>
    private (AreaBox Box, RegionEdit.Handle Handle)? Hit(Point point)
    {
        var size = RenderSize;
        var (x, y) = (point.X / size.Width, point.Y / size.Height);
        var order = new[] { model.SelectedBox }.OfType<AreaBox>().Concat(model.Boxes.Where(box => box.Id != model.Selected).Reverse());
        foreach (var box in order)
            if (RegionEdit.HandleAt(box.Region, x, y, Grab / size.Width, Grab / size.Height) is { } handle) return (box, handle);
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        if (Hit(point) is not { } hit) return;
        model.Select(hit.Box.Id);
        drag = (hit.Box.Id, hit.Box.Region, hit.Handle, point);
        CaptureMouse();
        InvalidateVisual();
        Changed?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        if (drag is { } active && IsMouseCaptured)
        {
            var size = RenderSize;
            model.Drag(active.Id, active.Start, active.Handle, (point.X - active.From.X) / size.Width, (point.Y - active.From.Y) / size.Height);
            InvalidateVisual();
            Changed?.Invoke();
            return;
        }
        Cursor = Hit(point)?.Handle switch
        {
            null => Cursors.Arrow,
            RegionEdit.Handle.None => Cursors.SizeAll,
            RegionEdit.Handle.Left or RegionEdit.Handle.Right => Cursors.SizeWE,
            RegionEdit.Handle.Top or RegionEdit.Handle.Bottom => Cursors.SizeNS,
            RegionEdit.Handle.Left | RegionEdit.Handle.Top or RegionEdit.Handle.Right | RegionEdit.Handle.Bottom => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW,
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        drag = null;
        Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        (int X, int Y)? step = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => null,
        };
        if (step is { } move)
        {
            model.Nudge(move.X, move.Y, shift, ctrl);
            InvalidateVisual();
            Changed?.Invoke();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && model.SelectedBox is { Main: false } box)
        {
            model.Remove(box.Id);
            InvalidateVisual();
            Changed?.Invoke();
            e.Handled = true;
            return;
        }
        // Sekme tuşu yerine N: sıradaki kutu seçilir (Tab odak sırasını bozmasın).
        if (e.Key == Key.N)
        {
            var boxes = model.Boxes;
            var index = boxes.ToList().FindIndex(item => item.Id == model.Selected);
            model.Select(boxes[(index + 1) % boxes.Count].Id);
            InvalidateVisual();
            Changed?.Invoke();
            e.Handled = true;
        }
    }
}
