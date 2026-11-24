// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cevirgen.Core;

namespace Cevirgen.App.Services;

/// <summary>
/// Deneme karelerini çizer: Çevirgen'in kendi gece şehri sahnesi (<see cref="Controls.PerdeBrushes.Scene"/>; özgün çizim),
/// sol üstte görev yazısı, altta İngilizce altyazı. Kareler gerçek hattan geçer; yazıyı yazı tanıma okur. Ekran görüntüsü
/// aracı da bu kareyi kullanır (depodaki görsellerde oyun görüntüsü yoktur).
/// </summary>
internal static class DemoFrameRenderer
{
    private const double W = DemoFrames.Width;
    private const double H = DemoFrames.Height;

    /// <summary>Sahnenin tonu satır gruplarıyla değişir: örtüsüz, daha koyu gece, hafif sıcak şafak.</summary>
    private static readonly Color[] Tints = [Rgb(0, 0, 0, 0), Rgb(0.02, 0.03, 0.09, 0.32), Rgb(0.55, 0.34, 0.20, 0.10)];

    private static readonly Typeface Bold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>
    /// <paramref name="line"/>: gösterilen altyazının sırası (null: altyazısız kare). <paramref name="scene"/>: arka planın
    /// tonu. Akışın iş parçacığından çağrılır; çizim arayüz iş parçacığında yapılır. Uygulama kapanıyorsa null.
    /// </summary>
    public static Frame? Render(int? line, int scene)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return null;
        try
        {
            return dispatcher.CheckAccess() ? Draw(line, scene) : dispatcher.Invoke(() => Draw(line, scene));
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static Frame Draw(int? line, int scene)
    {
        var count = DemoScript.Lines.Count;
        return Draw(line is { } index ? [DemoScript.Lines[((index % count) + count) % count].English] : [], scene);
    }

    /// <summary>Verilen İngilizce satırlarla kare (yukarıdan aşağı; sonuncusu altyazının yerinde). Ekran görüntüsü aracı da kullanır.</summary>
    internal static Frame Draw(IReadOnlyList<string> english, int scene)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            DrawScene(context, scene);
            // Ölçüler 1080p oyundaki gibi (karede 720p): GTA 6 altyazısı 1080p'de ~25 px (burada 17), görev yazısı ~24 px
            // (burada 16). Türkçe (32 px) İngilizceden belirgin büyük durur. Canlı ekrandaki önizleme Türkçeyi aynı oranla çizdiği için İngilizce ile Türkçe TV'deki gibi görünür.
            DrawText(context, DemoScript.Objective.English, size: 16, x: 48, top: 0.08);
            for (var i = 0; i < english.Count; i++)
                DrawText(context, english[i], size: 17, x: null, top: DemoFrames.SubtitleBaseline - (english.Count - 1 - i) * 28.0 / H);
        }
        var bitmap = new RenderTargetBitmap(DemoFrames.Width, DemoFrames.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var stride = DemoFrames.Width * 4;
        var pixels = new byte[stride * DemoFrames.Height];
        bitmap.CopyPixels(pixels, stride, 0);
        return new Frame(pixels, DemoFrames.Width, DemoFrames.Height, stride);
    }

    /// <summary>Gece şehri: lacivert gök, ince ay, iki katman bina silüeti, sarı pencereler ve sudaki yansımaları.</summary>
    private static void DrawScene(DrawingContext context, int scene)
    {
        var all = new Rect(0, 0, W, H);
        context.DrawRectangle(Controls.PerdeBrushes.Scene(), null, all);
        var tint = Tints[((scene % Tints.Length) + Tints.Length) % Tints.Length];
        if (tint.A > 0) context.DrawRectangle(Fill(tint), null, all);
    }

    /// <summary>
    /// Yazı, okunaklı olsun diye yarı saydam koyu kutunun üstünde beyaz çizilir (oyunlardaki altyazı gibi).
    /// <paramref name="top"/>: satırın taban çizgisinin üstten oranı. <paramref name="x"/>: sola yaslı yazının başladığı
    /// yer (ortalıysa null).
    /// </summary>
    private static void DrawText(DrawingContext context, string text, double size, double? x, double top)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, Brushes.White, 1.0);
        var originX = x ?? (W - formatted.WidthIncludingTrailingWhitespace) / 2;
        var originY = (H * top) - formatted.Baseline;
        var pad = size * 0.45;
        context.DrawRoundedRectangle(Fill(Rgb(0, 0, 0, 0.62)), null,
            new Rect(originX - pad, originY - (pad * 0.6), formatted.WidthIncludingTrailingWhitespace + (pad * 2), formatted.Height + (pad * 1.2)), 8, 8);
        context.DrawText(formatted, new Point(originX, originY));
    }

    private static Color Rgb(double red, double green, double blue, double alpha = 1) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), (byte)Math.Round(red * 255), (byte)Math.Round(green * 255), (byte)Math.Round(blue * 255));

    private static SolidColorBrush Fill(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
