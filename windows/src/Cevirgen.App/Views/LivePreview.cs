// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cevirgen.App.Controls;
using Cevirgen.App.Theme;
using Cevirgen.Core;
using Cevirgen.Shell;
using TextBox = Cevirgen.Core.TextBox;
using Frame = Cevirgen.Core.Frame;

namespace Cevirgen.App.Views;

/// <summary>
/// Canlı ekranın TV önizlemesi (taslak: Main.dc.html › "TV görüntüsü"): 1920 × 1080'in gerçek oranında son oyun karesi
/// (çeviri kapalıyken önceki oturumun son gerçek karesi; o da yoksa düz koyu yüzey; sahte sahne çizilmez), %90 güvenli alan çizgisi ve üstünde TV'de şu an görünen Türkçe satırlar. Kare oranı korunarak sığdırılır;
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
    private readonly TextBlock hiddenTag = new() { Text = "Altyazı gizli", Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Grid overlay = new();
    /// <summary>Çevirgen: ek alanların (ipucu, seçenekler) TV'deki Türkçesi, kendi yerlerinde.</summary>
    private readonly Canvas regionLayer = new() { IsHitTestVisible = false };
    private IReadOnlyList<(string Text, TextBox Box)> regionTexts = [];
    private WriteableBitmap? bitmap;
    private long lastDraw;
    private IReadOnlyList<SubtitleStack.Line> shown = [];
    private SubtitleStyle style = new();
    private TextBox? english;

    public LivePreview()
    {
        Focusable = false;
        // Kare yokken düz, koyu TV yüzeyi (iki temada da: TV ekranı koyudur).
        stage.Background = PerdeColors.Frozen(PerdeColors.Rgb(0x16, 0x18, 0x1D));
        stage.Children.Add(image);
        safeArea.Stroke = PerdeColors.Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        stage.Children.Add(safeArea);
        safeLabel.SetResourceReference(TextBlock.FontFamilyProperty, "TextFont");
        safeLabel.Foreground = PerdeColors.Frozen(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
        safeLabel.HorizontalAlignment = HorizontalAlignment.Left;
        safeLabel.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(safeLabel);
        hiddenTag.SetResourceReference(TextBlock.FontFamilyProperty, "TextFont");
        hiddenTag.FontWeight = FontWeights.SemiBold;
        hiddenTag.Foreground = PerdeColors.Frozen(PerdeColors.Rgb(0xFF, 0xD2, 0x3F));
        hiddenTag.HorizontalAlignment = HorizontalAlignment.Right;
        hiddenTag.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(hiddenTag);
        stage.Children.Add(lines);
        stage.Children.Add(regionLayer);
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

    /// <summary>Kare var mı (canlı ya da önceki oturumdan; yoksa düz yüzey görünür).</summary>
    public bool HasFrame => image.Source is not null;

    /// <summary>Canlı kare geldi mi (önceki oturumun karesi sayılmaz).</summary>
    public bool HasLiveFrame => bitmap is not null && ReferenceEquals(image.Source, bitmap);

    /// <summary>
    /// Çeviri kapalıyken önceki oturumun son gerçek karesi (<see cref="Services.LastFrameStore"/>); null ise düz yüzey. Canlı
    /// kare geldiyse ona dokunulmaz.
    /// </summary>
    public void SetStill(ImageSource? still)
    {
        if (HasLiveFrame) return;
        image.Source = still;
        stage.Background = still is null ? PerdeColors.Frozen(PerdeColors.Rgb(0x16, 0x18, 0x1D)) : Brushes.Black;
        Layout();
    }

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
        // 16:9 olmayan karenin yanlarında koyu bant kalır (TV de böyle gösterir).
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

    /// <summary>Çevirgen: ek alanların TV'de görünen çevirileri (karedeki yerleriyle).</summary>
    public void SetRegions(IReadOnlyList<(string Text, TextBox Box)> texts)
    {
        regionTexts = texts;
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
        // Çevirgen: ek alan çevirileri İngilizce yazının yerinde, küçük koyu kutuda (TV'deki gibi).
        regionLayer.Children.Clear();
        regionLayer.Opacity = lines.Opacity;
        foreach (var (text, box) in regionTexts)
        {
            var boxWidth = Math.Max(24, w * (box.Right - box.Left));
            // Önizleme ölçeğinde iç boşluk (TV'deki kutunun payı).
            var inset = Math.Max(1, h * 0.002);
            var label = new TextBlock
            {
                Text = text, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                FontSize = Math.Clamp(h * (box.Bottom - box.Top) * 0.7, 7, 16), LineHeight = double.NaN,
            };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "TextFont");
            var plate = new Border
            {
                Child = label, Background = PerdeColors.Frozen(Color.FromArgb(0xD9, 0x10, 0x10, 0x14)), Padding = new Thickness(inset * 2, inset, inset * 2, inset),
                MinWidth = boxWidth, MaxWidth = Math.Max(boxWidth, w * 0.35),
            };
            plate.Measure(new Size(plate.MaxWidth, double.PositiveInfinity));
            var plateWidth = plate.DesiredSize.Width;
            // Sağ yarıdaki yazı sağa yaslanır (tuş listesi), sol yarıdaki sola; kutu karenin dışına taşmaz.
            var plateLeft = (box.Left + box.Right) / 2 > 0.5 ? x + w * box.Right - plateWidth : x + w * box.Left;
            Canvas.SetLeft(plate, Math.Clamp(plateLeft, x, Math.Max(x, x + w - plateWidth)));
            Canvas.SetTop(plate, Math.Clamp(y + h * box.Top, y, Math.Max(y, y + h - plate.DesiredSize.Height)));
            regionLayer.Children.Add(plate);
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

    private readonly Dictionary<string, IReadOnlyList<(string Text, TextBox Box)>> regions = [];

    /// <summary>Çevirgen: ek alanların şu an TV'de görünen çevirileri (alan alan, karedeki yerleriyle).</summary>
    public IReadOnlyList<(string Text, TextBox Box)> Regions => [.. regions.Values.SelectMany(list => list)];

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

    /// <summary>Bir altyazı olayı (yayın penceresiyle aynı olaylar; ek alanlar da önizlemede, ekran yazısı gösterilmez).</summary>
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
            case SubtitleEventKind.Region when e.Id is { } id:
                if (e.Text.Length == 0) regions.Remove(id);
                else if (e.Blocks is { Count: > 0 } blocks)
                    regions[id] = [.. blocks.Where(block => block.Translation.Length > 0).Select(block => (block.Translation, block.Box))];
                else if (e.Box is { } regionBox) regions[id] = [(e.Text, regionBox)];
                break;
            case SubtitleEventKind.RegionClear:
                regions.Clear();
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
