// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.App.Controls;
using CouchSubs.App.Views;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>
/// Enmera'nın kendi uygulamalarının reklamları (Mac AdCatalog ile aynı). Üçüncü taraf reklam ağı, izleme ve kişisel
/// veri yok: liste uygulamanın içinde (design/yeni-arayuz/reklamlar.json ve reklam/*.png; Mac aynı dosyayı okur),
/// tıklanınca yalnız mağaza bağlantısı tarayıcıda açılır. Dosya yoksa ya da bozuksa reklam bandı görünmez.
/// </summary>
public static class AdCatalog
{
    public const string Home = "https://github.com/";

    private static IReadOnlyList<BannerAd> banners = Load();

    /// <summary>Bantta sırayla dönen reklamlar (Cebbin, Gramla, Bakkal).</summary>
    public static IReadOnlyList<BannerAd> Banners
    {
        get => banners;
        set
        {
            banners = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    /// <summary>reklamlar.json'u uygulamanın diline göre okur (Türkçe değilse İngilizce).</summary>
    public static IReadOnlyList<BannerAd> Load()
    {
        try
        {
            using var stream = typeof(AdCatalog).Assembly.GetManifestResourceStream("reklamlar.json");
            if (stream is null) return [];
            using var document = JsonDocument.Parse(stream);
            var language = Localizer.Current.Language == "tr" ? "tr" : "en";
            var list = new List<BannerAd>();
            foreach (var ad in document.RootElement.GetProperty("ads").EnumerateArray())
            {
                string Text(string name) =>
                    ad.GetProperty(name).TryGetProperty(language, out var value) ? value.GetString() ?? "" : ad.GetProperty(name).GetProperty("en").GetString() ?? "";
                string? Link(string name) => ad.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                list.Add(new BannerAd(ad.GetProperty("id").GetString() ?? "", Text("title"), Text("text"), Icon(ad.GetProperty("icon").GetString()),
                    Link("appStore"), Link("googlePlay")));
            }
            return list;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Açılır reklamlar (reklamlar.json › popups): görsel reklam/ klasöründen yüklenir; dosya yoksa, açılamıyorsa ya da
    /// boyutu sıfırsa o reklam atlanır. Hiçbiri yüklenemezse liste boş ve açılır reklam açılmaz.
    /// </summary>
    public static IReadOnlyList<PopupAd> LoadPopups()
    {
        try
        {
            using var stream = typeof(AdCatalog).Assembly.GetManifestResourceStream("reklamlar.json");
            if (stream is null) return [];
            using var reader = new StreamReader(stream);
            var list = new List<PopupAd>();
            foreach (var entry in PopupAdPolicy.Parse(reader.ReadToEnd()))
                if (Picture(entry.Image) is { PixelWidth: > 0, PixelHeight: > 0 } picture) list.Add(new PopupAd(picture, entry.Link));
            return list;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            return [];
        }
    }

    // --- Çevirgen: sunucudan gelen reklamlar (AppController.RemoteAds / RemotePopups) ---

    /// <summary>
    /// Denetleyicinin uzaktan içeriğini izler: reklam bandı ve açılır reklam sunucudakilerle değişir. Sunucuda reklam
    /// yoksa uygulamaya gömülü liste (reklamlar.json) kullanılır. Görseli açılamayan reklam atlanır.
    /// </summary>
    public static void Follow(AppController controller)
    {
        void Apply()
        {
            try
            {
                var remote = FromRemote(controller.RemoteAds);
                Banners = remote.Count > 0 ? remote : Load();
                var popups = PopupsFromRemote(controller.RemotePopups);
                PopupAdWindow.Ads = popups.Count > 0 ? popups : LoadPopups();
            }
            catch (Exception error)
            {
                // Reklam hiçbir koşulda uygulamayı düşürmemeli.
                SubtitleLog.Write($"uzaktan reklamlar uygulanamadı: {error.GetType().Name}: {error.Message}");
            }
        }
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(AppController.RemoteAds) or nameof(AppController.RemotePopups))) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Apply();
            else dispatcher.BeginInvoke(Apply);
        };
        if (controller.RemoteAds.Count > 0 || controller.RemotePopups.Count > 0) Apply();
    }

    /// <summary>Sunucudaki reklamlar bant reklamına: simge diskteki önbellek dosyasından; açılamazsa reklam atlanır.</summary>
    public static IReadOnlyList<BannerAd> FromRemote(IReadOnlyList<ResolvedAd> ads)
    {
        var list = new List<BannerAd>();
        foreach (var item in ads)
            if (FileImage(item.IconFile, 96) is { } icon)
                list.Add(new BannerAd(item.Ad.Id, item.Ad.Title, item.Ad.Text, icon, null, null, item.Ad.Link.AbsoluteUri));
        return list;
    }

    /// <summary>Sunucudaki açılır reklamlar: görsel diskteki önbellek dosyasından; açılamazsa ya da boyutu sıfırsa atlanır.</summary>
    public static IReadOnlyList<PopupAd> PopupsFromRemote(IReadOnlyList<ResolvedPopup> popups)
    {
        var list = new List<PopupAd>();
        foreach (var item in popups)
            if (FileImage(item.ImageFile, null) is { PixelWidth: > 0, PixelHeight: > 0 } picture) list.Add(new PopupAd(picture, item.Popup.Link));
        return list;
    }

    private static BitmapSource? FileImage(string path, int? decodeWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeWidth is { } width) image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UriFormatException or NotSupportedException or InvalidOperationException
                                          or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            SubtitleLog.Write($"reklam görseli açılamadı: {Path.GetFileName(path)} ({error.GetType().Name})");
            return null;
        }
    }

    private static BitmapSource? Picture(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Cevirgen;component/Assets/reklam/{file}", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UriFormatException or NotSupportedException or InvalidOperationException
                                          or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    /// <summary>Uygulama simgesi (reklam/*.png, uygulamaya Resource olarak gömülü).</summary>
    private static ImageSource? Icon(string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Cevirgen;component/Assets/reklam/{file}", UriKind.Absolute);
            image.DecodePixelWidth = 96;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UriFormatException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
