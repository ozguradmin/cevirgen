// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cevirgen.Core;

namespace Cevirgen.App.Services;

/// <summary>
/// Çevirgen: son oturumun son karesi. Çeviri durunca son kare ~1280 px genişliğe küçültülüp veri klasörüne JPEG olarak yazılır
/// (her seferinde üzerine). Canlı ekran ve Alanları düzenle, çeviri kapalıyken sahte bir sahne yerine bu gerçek kareyi gösterir;
/// dosya yoksa düz yer tutucu. Dosya yalnız bu bilgisayarda kalır.
/// </summary>
public static class LastFrameStore
{
    public const string FileName = "last-frame.jpg";
    public const int MaxWidth = 1280;

    /// <summary>Ekran görüntüsü aracı kullanıcının gerçek karesini okumasın / yazmasın.</summary>
    public static bool Disabled { get; set; }

    public static string FilePath => Path.Combine(AppPaths.Data, FileName);

    private static BitmapSource? cached;

    /// <summary>Kareyi arka planda küçültüp yazar. Tutarsız ya da boş kare yazılmaz; hata yalnız günlüğe.</summary>
    public static void Save(Frame? frame)
    {
        if (Disabled || frame is not { Width: > 0, Height: > 0, IsConsistent: true }) return;
        var image = Downscale(frame);
        cached = image;
        _ = Task.Run(() =>
        {
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(AppPaths.Data);
                var temp = FilePath + ".tmp";
                using (var file = File.Create(temp)) encoder.Save(file);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                SubtitleLog.Write($"son kare kaydedilemedi: {error.GetType().Name}");
            }
        });
    }

    /// <summary>Kayıtlı son kare; yoksa ya da okunamazsa null.</summary>
    public static BitmapSource? Load()
    {
        if (Disabled) return null;
        if (cached is not null) return cached;
        try
        {
            if (!File.Exists(FilePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(FilePath);
            image.EndInit();
            image.Freeze();
            return cached = image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            SubtitleLog.Write($"son kare okunamadı: {error.GetType().Name}");
            return null;
        }
    }

    /// <summary>Çevirgen (tanılama kaydı): kareyi en çok <paramref name="maxWidth"/> genişlikte JPEG baytlarına çevirir.</summary>
    public static byte[] Jpeg(Frame frame, int maxWidth)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(Downscale(frame, maxWidth)));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    /// <summary>BGRA kareyi en çok <see cref="MaxWidth"/> genişliğe küçültür (oran korunur); dondurulmuş, iş parçacıkları arası kullanılabilir.</summary>
    public static BitmapSource Downscale(Frame frame, int maxWidth = MaxWidth)
    {
        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null, frame.Pixels, frame.Stride);
        BitmapSource result = source;
        if (frame.Width > maxWidth)
        {
            var scale = (double)maxWidth / frame.Width;
            result = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }
        result.Freeze();
        return result;
    }
}
