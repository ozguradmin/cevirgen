// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CouchSubs.App.Controls;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;
using TextBox = CouchSubs.Core.TextBox;
using Frame = CouchSubs.Core.Frame;

namespace CouchSubs.App.Views;

/// <summary>
/// Canlı ekranın TV önizlemesi (taslak: Main.dc.html › "TV görüntüsü"): 1920 × 1080'in gerçek oranında son oyun karesi
/// (yoksa düz sahne), %90 güvenli alan çizgisi ve üstünde TV'de şu an görünen Türkçe satırlar. Kare oranı korunarak sığdırılır;
/// satırlar kullanıcının gerçek altyazı görünümüyle çizilir ve karenin genişliğine ölçeklenir (<see cref="LivePreviewLayout"/>;
/// yazı boyu 1920 genişlikli TV'nin pikseli): yazı boyu, kalınlık, kenarlık, zemin ve yerleşim (üstünde / örtsün / sabit);
/// üstteki eski satırlar (above) küçük ve soluk. "güvenli alan" ve "ALTYAZI GİZLİ" etiketleri güvenli alanın dışındaki
/// bantta durur, oyunun yazılarının üstüne binmez. Kare saniyede en çok 4 kez çizilir.
/// </summary>
public sealed class LivePreview : ContentControl
{
    private readonly Grid stage = new() { ClipToBounds = true };
    private readonly Image image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel lines = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Rectangle safeArea = new() { StrokeThickness = 1, StrokeDashArray = [4, 4], IsHitTestVisible = false };
    private readonly TextBlock safeLabel = new() { Text = "güvenli alan %90", IsHitTestVisible = false };
    private readonly TextBlock hiddenTag = new() { Text = "ALTYAZI GİZLİ", Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Grid overlay = new();
    private readonly SampleScene scene = new();
    private WriteableBitmap? bitmap;
    private long lastDraw;
    private IReadOnlyList<SubtitleStack.Line> shown = [];
    private SubtitleStyle style = new();
    private TextBox? english;

    public LivePreview()
    {
        Focusable = false;
        stage.Background = PerdeColors.Frozen(PerdeColors.Rgb(0x3C, 0x42, 0x4C));
        stage.Children.Add(scene);
        stage.Children.Add(image);
        safeArea.Stroke = PerdeColors.Frozen(Color.FromArgb(0x47, 0xFF, 0xFF, 0xFF));
        stage.Children.Add(safeArea);
        safeLabel.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        safeLabel.Foreground = PerdeColors.Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        safeLabel.HorizontalAlignment = HorizontalAlignment.Left;
        safeLabel.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(safeLabel);
        hiddenTag.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        hiddenTag.Foreground = PerdeColors.Frozen(PerdeColors.Rgb(0xFF, 0xD2, 0x3F));
        hiddenTag.HorizontalAlignment = HorizontalAlignment.Right;
        hiddenTag.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(hiddenTag);
        stage.Children.Add(lines);
        stage.Children.Add(overlay);
        stage.SizeChanged += (_, _) => Layout();
        var frame = new Border { BorderThickness = new Thickness(1), Child = stage, SnapsToDevicePixels = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "borderBrush");
        Content = new AspectBox { Child = frame, VerticalAlignment = VerticalAlignment.Top };
    }

    /// <summary>Önizlemenin üstünde duran içerik (boş hâldeki "Çeviriyi başlat" çağrısı).</summary>
    public UIElement? Overlay
    {
        get => overlay.Children.Count > 0 ? overlay.Children[0] : null;
        set
        {
            overlay.Children.Clear();
            if (value is not null) overlay.Children.Add(value);
        }
    }

    /// <summary>Kare var mı (yoksa düz sahne görünür).</summary>
    public bool HasFrame => image.Source is not null;

    /// <summary>Kareyi çizer (saniyede en çok 4 kez); null ise son kare kalır.</summary>
    public void SetFrame(Frame? frame)
    {
        if (frame is null) return;
        var now = Environment.TickCount64;
        if (now - lastDraw < 250 && image.Source is not null) return;
        lastDraw = now;
        if (bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
            bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);
        var resized = !ReferenceEquals(image.Source, bitmap);
        image.Source = bitmap;
        // Kare gelince düz sahne kalkar; 16:9 olmayan karenin yanlarında koyu bant kalır (TV de böyle gösterir).
        scene.Visibility = Visibility.Collapsed;
        stage.Background = Brushes.Black;
        if (resized) Layout();
    }

    /// <summary>TV'de görünen satırlar (yukarıdan aşağı), görünüm ve İngilizce satırın karedeki yeri.</summary>
    public void SetLines(IReadOnlyList<SubtitleStack.Line> visible, SubtitleStyle look, TextBox? englishBox, bool hidden)
    {
        shown = visible;
        style = look;
        english = englishBox;
        hiddenTag.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        lines.Opacity = hidden ? 0.35 : 1;
        Layout();
    }

    private void Layout()
    {
        var width = stage.ActualWidth;
        var height = stage.ActualHeight;
        if (width <= 0 || height <= 0) return;
        // Oyun karesinin önizlemedeki yeri (oranı korunarak sığdırılır). Türkçe satırlar karenin genişliğine göre ölçeklenir:
        // kullanıcının yazı boyu 1920 genişlikli TV'nin pikselidir; karedeki İngilizce de kare → önizleme oranıyla küçülür.
        // Böylece önizlemedeki İngilizce/Türkçe oranı TV'dekiyle aynıdır.
        var (x, y, w, h) = image.Source is BitmapSource frame
            ? LivePreviewLayout.FrameRect(frame.PixelWidth, frame.PixelHeight, width, height)
            : (0d, 0d, width, height);
        image.Margin = new Thickness(x, y, width - x - w, height - y - h);
        var scale = LivePreviewLayout.Scale(w);
        safeArea.Margin = new Thickness(x + w * 0.05, y + h * 0.05, width - x - w + w * 0.05, height - y - h + h * 0.05);
        // Etiketler güvenli alan çizgisinin dışındaki %5'lik bantta durur: oyunun sol üstteki görev yazısının ya da altyazının
        // üstüne binmez. Bant çok darsa (küçük önizleme) gizlenir.
        var band = h * 0.05;
        safeLabel.FontSize = Math.Clamp(band * 0.62, 8, 11);
        safeLabel.Margin = new Thickness(x + w * 0.05, y + h * 0.95 + Math.Max(0, (band - safeLabel.FontSize * 1.3) / 2), 0, 0);
        safeLabel.Visibility = band >= 12 ? Visibility.Visible : Visibility.Collapsed;
        hiddenTag.FontSize = Math.Clamp(band * 0.62, 9, 12);
        hiddenTag.Margin = new Thickness(0, y + Math.Max(0, (band - hiddenTag.FontSize * 1.3) / 2), width - x - w + w * 0.05, 0);

        lines.Children.Clear();
        foreach (var line in shown)
        {
            var current = line.Role == SubtitleStack.Role.Current;
            var visual = SubtitleLine.Make(line.Wrapped.Text, style, scale, current, line.Wrapped.Scale);
            visual.Margin = new Thickness(0, line == shown[0] ? 0 : 4 * scale, 0, 0);
            visual.Opacity = line.Role switch { SubtitleStack.Role.Current => 1, SubtitleStack.Role.Previous => 0.78, _ => 0.6 } * (line.Fading ? 0.5 : 1);
            lines.Children.Add(visual);
        }
        var anchor = LivePreviewLayout.Anchor(style, english);
        var fullBand = style.BackgroundShape == SubtitleBackgroundShape.Band;
        var left = x + (fullBand ? 0 : w * anchor.Left);
        var right = width - x - w + (fullBand ? 0 : w * anchor.Right);
        // DesiredSize kenar boşluğunu da içerir: önceki yerleşimin üst boşluğu ölçüye karışmasın.
        lines.Margin = new Thickness(left, 0, right, 0);
        lines.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
        var blockHeight = lines.DesiredSize.Height;
        double top;
        if (anchor.Top is { } fromTop) top = h * fromTop;
        else if (anchor.Center is { } center) top = h * center - blockHeight / 2;
        else top = h * (1 - (anchor.Bottom ?? 0.07)) - blockHeight;
        // TV'deki keepOnScreen: blok karenin dışına taşmaz.
        top = y + Math.Clamp(top, 0, Math.Max(0, h - blockHeight));
        lines.VerticalAlignment = VerticalAlignment.Top;
        lines.Margin = new Thickness(left, top, right, 0);
    }

    /// <summary>Kare yokken düz sahne (taslaktaki gökyüzü, ufuk ve iki gölge; degrade yok).</summary>
    private sealed class SampleScene : Grid
    {
        public SampleScene()
        {
            IsHitTestVisible = false;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(48, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(7, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(45, GridUnitType.Star) });
            Add(new Border { Background = PerdeColors.Frozen(PerdeColors.Rgb(0x75, 0x87, 0x9A)) }, 0);
            Add(new Border { Background = PerdeColors.Frozen(PerdeColors.Rgb(0x55, 0x60, 0x6E)) }, 1);
            var shapes = new Grid();
            shapes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Star) });
            shapes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18, GridUnitType.Star) });
            shapes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Star) });
            shapes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star) });
            shapes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29, GridUnitType.Star) });
            shapes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28, GridUnitType.Star) });
            shapes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8, GridUnitType.Star) });
            shapes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36, GridUnitType.Star) });
            shapes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28, GridUnitType.Star) });
            var tall = new Border { Background = PerdeColors.Frozen(PerdeColors.Rgb(0x2E, 0x33, 0x3B)) };
            SetColumn(tall, 1);
            SetRow(tall, 1);
            SetRowSpan(tall, 2);
            shapes.Children.Add(tall);
            var narrow = new Border { Background = PerdeColors.Frozen(PerdeColors.Rgb(0x26, 0x2A, 0x31)) };
            SetColumn(narrow, 3);
            SetRow(narrow, 2);
            shapes.Children.Add(narrow);
            SetRowSpan(shapes, 3);
            Children.Add(shapes);
        }

        private void Add(Border part, int row)
        {
            SetRow(part, row);
            Children.Add(part);
        }
    }
}

/// <summary>
/// Canlı ekranın altyazı yığını: TV'ye giden olaylar (<see cref="AppController.StreamEvent"/>; altyazı yalnız TV'de olsa da
/// gelir) TV uygulamasıyla aynı yığına (<see cref="SubtitleStack"/>) verilir; böylece önizlemede ve dökümün "● ekranda"
/// işaretinde TV'dekiyle aynı satırlar görünür. Değişince <see cref="Changed"/>.
/// </summary>
internal sealed class LiveStage
{
    private readonly AppController controller;
    private readonly SubtitleStack stack = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new();

    public LiveStage(AppController controller)
    {
        this.controller = controller;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Notify();
        };
    }

    /// <summary>Yığın değişti (yeni satır, satır kalktı ya da soldu).</summary>
    public event Action? Changed;

    /// <summary>Oyundaki İngilizce satırın son bilinen yeri.</summary>
    public TextBox? EnglishBox { get; private set; }

    private double Now => clock.Elapsed.TotalSeconds;

    /// <summary>Şu an görünen satırlar, yukarıdan aşağı.</summary>
    public IReadOnlyList<SubtitleStack.Line> Lines => stack.Lines(Now);

    public void Attach()
    {
        controller.StreamEvent -= OnEvent;
        controller.StreamHidden -= OnHidden;
        controller.StreamEvent += OnEvent;
        controller.StreamHidden += OnHidden;
    }

    public void Detach()
    {
        controller.StreamEvent -= OnEvent;
        controller.StreamHidden -= OnHidden;
        timer.Stop();
    }

    /// <summary>Bir altyazı olayı (yayın penceresiyle aynı olaylar; ekran yazısı ve ek alanlar önizlemede gösterilmez).</summary>
    public void OnEvent(SubtitleEvent e)
    {
        stack.StackPrevious = controller.Settings.Style.StackPrevious;
        switch (e.Kind)
        {
            case SubtitleEventKind.Subtitle:
                stack.Show(e.Text, e.DurationMs, e.Paragraphs, Now, e.Replaces, e.Above);
                if (e.Box is { } box) EnglishBox = box;
                break;
            case SubtitleEventKind.Above:
                stack.SetAbove(e.Above ?? [], Now);
                break;
            case SubtitleEventKind.Clear:
                stack.Clear(Now);
                break;
            default:
                return;
        }
        Notify();
    }

    private void OnHidden()
    {
        stack.Hide();
        Notify();
    }

    private void Notify()
    {
        timer.Stop();
        if (stack.NextChange(Now) is { } next)
        {
            timer.Interval = TimeSpan.FromSeconds(Math.Max(0.01, next - Now));
            timer.Start();
        }
        Changed?.Invoke();
    }
}
