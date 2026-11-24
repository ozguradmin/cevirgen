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
/// (yoksa düz sahne), %90 güvenli alan çizgisi ve üstünde TV'de şu an görünen Türkçe satırlar. Satırlar kullanıcının gerçek
/// altyazı görünümüyle çizilir, önizleme genişliğine ölçeklenir (<see cref="LivePreviewLayout"/>): yazı boyu, kalınlık,
/// kenarlık, zemin ve yerleşim (üstünde / örtsün / sabit); üstteki eski satırlar (above) küçük ve soluk. Kare saniyede en
/// çok 4 kez çizilir.
/// </summary>
public sealed class LivePreview : ContentControl
{
    private readonly Grid stage = new() { ClipToBounds = true };
    private readonly Image image = new() { Stretch = Stretch.UniformToFill };
    private readonly StackPanel lines = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Rectangle safeArea = new() { StrokeThickness = 1, StrokeDashArray = [4, 4], IsHitTestVisible = false };
    private readonly TextBlock safeLabel = new() { Text = "güvenli alan %90", IsHitTestVisible = false };
    private readonly TextBlock hiddenTag = new() { Text = "ALTYAZI GİZLİ", Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Grid overlay = new();
    private WriteableBitmap? bitmap;
    private long lastDraw;
    private IReadOnlyList<SubtitleStack.Line> shown = [];
    private SubtitleStyle style = new();
    private TextBox? english;

    public LivePreview()
    {
        Focusable = false;
        stage.Background = PerdeColors.Frozen(PerdeColors.Rgb(0x3C, 0x42, 0x4C));
        stage.Children.Add(new SampleScene());
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
        image.Source = bitmap;
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
        var scale = LivePreviewLayout.Scale(width);
        safeArea.Margin = new Thickness(width * 0.05, height * 0.05, width * 0.05, height * 0.05);
        safeLabel.Margin = new Thickness(width * 0.054, height * 0.056, 0, 0);
        safeLabel.FontSize = Math.Max(9, 18 * scale);
        hiddenTag.Margin = new Thickness(0, height * 0.056, width * 0.054, 0);
        hiddenTag.FontSize = Math.Max(10, 20 * scale);

        lines.Children.Clear();
        foreach (var line in shown) lines.Children.Add(MakeLine(line, width, scale));
        var anchor = LivePreviewLayout.Anchor(style, english);
        var band = style.BackgroundShape == SubtitleBackgroundShape.Band;
        var left = band ? 0 : width * anchor.Left;
        var right = band ? 0 : width * anchor.Right;
        // DesiredSize kenar boşluğunu da içerir: önceki yerleşimin üst boşluğu ölçüye karışmasın.
        lines.Margin = new Thickness(left, 0, right, 0);
        lines.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
        var blockHeight = lines.DesiredSize.Height;
        double top;
        if (anchor.Top is { } fromTop) top = height * fromTop;
        else if (anchor.Center is { } center) top = height * center - blockHeight / 2;
        else top = height * (1 - (anchor.Bottom ?? 0.07)) - blockHeight;
        // TV'deki keepOnScreen: blok ekranın dışına taşmaz.
        top = Math.Clamp(top, 0, Math.Max(0, height - blockHeight));
        lines.VerticalAlignment = VerticalAlignment.Top;
        lines.Margin = new Thickness(left, top, right, 0);
    }

    /// <summary>Bir satır: zemin kutusu (koyuluk ayardaki gibi) ve içinde kenarlıklı yazı; üst satırlar %80 boy, soluk.</summary>
    private FrameworkElement MakeLine(SubtitleStack.Line line, double width, double scale)
    {
        var current = line.Role == SubtitleStack.Role.Current;
        var wrapped = line.Wrapped;
        var font = Math.Max(1, LivePreviewLayout.FontPx(style, width, current) * wrapped.Scale);
        var text = new OutlinedText
        {
            Text = wrapped.Text,
            FontSize = font,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.SemiBold,
            Fill = new SolidColorBrush(SpeakerText.Rgb(style.TextRgb)),
            Stroke = Brushes.Black,
            StrokeThickness = LivePreviewLayout.OutlinePx(style, width, current),
        };
        text.SetResourceReference(OutlinedText.FontFamilyProperty, "TextFont");
        var alpha = LivePreviewLayout.BackgroundAlpha(style) * (current ? 1 : 0.83);
        var band = style.BackgroundShape == SubtitleBackgroundShape.Band;
        return new Border
        {
            Background = alpha <= 0 ? Brushes.Transparent : new SolidColorBrush(SpeakerText.Rgb(style.BackgroundRgb, (byte)Math.Round(alpha * 255))),
            CornerRadius = new CornerRadius(band ? 0 : 10 * scale),
            // Zeminsiz görünümde kutu payı yok (satırlar taslaktaki gibi sıkı durur).
            Padding = alpha <= 0 ? new Thickness(0) : current ? new Thickness(26 * scale, 10 * scale, 26 * scale, 10 * scale) : new Thickness(20 * scale, 6 * scale, 20 * scale, 6 * scale),
            Margin = new Thickness(0, line == shown[0] ? 0 : 4 * scale, 0, 0),
            HorizontalAlignment = band ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            Opacity = line.Role switch { SubtitleStack.Role.Current => 1, SubtitleStack.Role.Previous => 0.78, _ => 0.6 } * (line.Fading ? 0.5 : 1),
            Child = text,
        };
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
