// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace CouchSubs.Core;

/// <summary>Görseli diske inmiş reklam: <paramref name="IconFile"/> veri klasöründeki önbellek dosyası.</summary>
public sealed record ResolvedAd(RemoteAd Ad, string IconFile);

/// <summary>Görseli diske inmiş açılır reklam.</summary>
public sealed record ResolvedPopup(RemotePopup Popup, string ImageFile);

/// <summary>Destek alanı; simgesi diske inmişse <paramref name="IconFile"/> (yoksa null: simgesiz).</summary>
public sealed record ResolvedSupport(RemoteSupport Support, string? IconFile);

/// <summary>Oyun seçicisinin bir oyunu; kapağı diske inmişse <paramref name="CoverFile"/> (yoksa null: yazıyla karo).</summary>
public sealed record ResolvedGame(RemoteGame Game, string? CoverFile);

/// <summary>Arayüzün kullanacağı uzaktan içerik: görseli olmayan reklam listede yoktur; kapağı inmeyen oyun kapaksız kalır.</summary>
public sealed record RemoteContent(IReadOnlyList<ResolvedAd> Ads, IReadOnlyList<ResolvedPopup> Popups, Announcement? Announcement)
{
    public static readonly RemoteContent Empty = new([], [], null);

    /// <summary>Oyun seçicisinin oyunları (sunucudaki sırayla).</summary>
    public IReadOnlyList<ResolvedGame> Games { get; init; } = [];

    /// <summary>Sunucudaki destek alanı; yoksa null (uygulamanın kendi metni).</summary>
    public ResolvedSupport? Support { get; init; }

    public bool Equals(RemoteContent? other) =>
        other is not null && Ads.SequenceEqual(other.Ads) && Popups.SequenceEqual(other.Popups) && Equals(Announcement, other.Announcement)
        && Games.SequenceEqual(other.Games) && Equals(Support, other.Support);

    public override int GetHashCode() => HashCode.Combine(Ads.Count, Popups.Count, Announcement, Games.Count);
}

/// <summary>
/// Uzaktan ayarlar (<c>GET /v1/config</c>): uygulama güncellemesi gerekmeden reklam, açılır reklam, duyuru, oyun kapakları
/// ve destek alanı. İstek düz GET'tir: çerez, kimlik ya da kullanıcıya ait hiçbir bilgi gitmez. Son geçerli yanıt veri klasörüne yazılır
/// (<see cref="RemoteConfig.CacheFileName"/>); ağ yokken o kullanılır. Görseller yalnız https'ten, en çok 2 MB, yalnız
/// PNG/JPEG/GIF/WEBP olarak iner ve adresin SHA-256 özetiyle adlandırılıp <c>remote-assets</c> klasöründe saklanır;
/// görseli inmeyen reklam atlanır. Hiçbir yöntem hata fırlatmaz; sorunlar yalnız günlüğe yazılır.
/// </summary>
public sealed class RemoteConfigClient : IDisposable
{
    /// <summary>Açılışta ve saatte bir bakılır (sunucu yanıtı 5 dakika önbellekte tutar).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    public const long MaxImageBytes = 2 * 1024 * 1024;
    public const string AssetFolderName = "remote-assets";
    /// <summary>Yapılandırma yanıtı için üst sınır (yönetim panelinin yazdığı JSON küçüktür).</summary>
    public const long MaxConfigBytes = 256 * 1024;

    private readonly CevirgenEndpoints endpoints;
    private readonly string folder;
    private readonly HttpClient http;

    /// <param name="folder">Veri klasörü (%LOCALAPPDATA%\Cevirgen); önbellek dosyası ve görseller buraya yazılır.</param>
    /// <param name="handler">HTTP katmanı; testlerde sahtesi verilir. Verilmezse çerezsiz bir katman kurulur.</param>
    public RemoteConfigClient(Uri baseUrl, string folder, HttpMessageHandler? handler = null) : this([baseUrl], folder, handler) { }

    /// <param name="baseUrls">Sunucunun adresleri, sırayla (asıl adres, yedek); bkz. <see cref="CevirgenEndpoints"/>.</param>
    public RemoteConfigClient(IReadOnlyList<Uri> baseUrls, string folder, HttpMessageHandler? handler = null)
    {
        endpoints = new CevirgenEndpoints(baseUrls);
        this.folder = folder;
        http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = CevirgenApi.Timeout,
            MaxResponseContentBufferSize = MaxImageBytes + 1,
        };
    }

    public string CacheFile => Path.Combine(folder, RemoteConfig.CacheFileName);
    public string AssetFolder => Path.Combine(folder, AssetFolderName);

    /// <summary>Son geçerli yanıt (diskten); yoksa ya da bozuksa null.</summary>
    public RemoteConfig? LoadCached()
    {
        try
        {
            return File.Exists(CacheFile) ? RemoteConfig.Parse(File.ReadAllText(CacheFile)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sunucudan alır; geçerliyse önbelleğe yazar ve döner. Ağ hatası, 2xx dışı yanıt ya da bozuk JSON'da null.</summary>
    public async Task<RemoteConfig?> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await endpoints.SendAsync(http, baseUrl =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, CevirgenApi.ConfigUrl(baseUrl));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return request;
            }, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                SubtitleLog.Write($"uzaktan ayarlar alınamadı: HTTP {(int)response.StatusCode}");
                return null;
            }
            var bytes = await ReadLimitedAsync(response, MaxConfigBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null) return null;
            var json = Encoding.UTF8.GetString(bytes);
            if (RemoteConfig.Parse(json) is not { } config)
            {
                SubtitleLog.Write("uzaktan ayarlar okunamadı (geçersiz JSON)");
                return null;
            }
            WriteAtomic(CacheFile, bytes);
            return config;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidOperationException)
        {
            SubtitleLog.Write($"uzaktan ayarlar alınamadı: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// Görsellerin yerel dosyalarını bulur; <paramref name="download"/> ise eksikleri indirir. Görseli olmayan reklam ve
    /// açılır reklam listeye girmez. İndirme yapıldıysa artık kullanılmayan görseller silinir.
    /// </summary>
    public async Task<RemoteContent> ResolveAsync(RemoteConfig config, bool download, CancellationToken cancellationToken = default)
    {
        var ads = new List<ResolvedAd>();
        foreach (var ad in config.Ads)
            if (await ImageAsync(ad.Icon, download, cancellationToken).ConfigureAwait(false) is { } file) ads.Add(new ResolvedAd(ad, file));
        var popups = new List<ResolvedPopup>();
        foreach (var popup in config.Popups)
            if (await ImageAsync(popup.Image, download, cancellationToken).ConfigureAwait(false) is { } file) popups.Add(new ResolvedPopup(popup, file));
        // Kapak inmezse oyun yine listede (adıyla karo); kapak sonraki bakışta yeniden denenir.
        var games = new List<ResolvedGame>();
        foreach (var game in config.Games)
            games.Add(new ResolvedGame(game, game.Cover is { } cover ? await ImageAsync(cover, download, cancellationToken).ConfigureAwait(false) : null));
        // Destek alanı simgesiz de gösterilir; simge inince eklenir.
        ResolvedSupport? support = config.Support is { } remote
            ? new ResolvedSupport(remote, remote.Icon is { } icon ? await ImageAsync(icon, download, cancellationToken).ConfigureAwait(false) : null)
            : null;
        if (download) Prune(config);
        return new RemoteContent(ads, popups, config.Announcement) { Games = games, Support = support };
    }

    /// <summary>
    /// Görselin önbellekteki dosyası; yoksa ve <paramref name="download"/> ise indirir. Olmazsa null. Reklam, açılır reklam ve oyun
    /// kapakları aynı kurallarla (yalnız https, en çok 2 MB, PNG/JPEG/GIF/WEBP imzası) iner.
    /// </summary>
    public async Task<string?> ImageAsync(Uri url, bool download = true, CancellationToken cancellationToken = default)
    {
        if (url.Scheme != Uri.UriSchemeHttps) return null;
        var path = Path.Combine(AssetFolder, ImageFileName(url));
        try
        {
            if (File.Exists(path)) return path;
            if (!download) return null;
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            // Yönlendirme https dışına çıkarsa (HttpClient zaten indirmez) görsel kullanılmaz.
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
            {
                SubtitleLog.Write($"reklam görseli inmedi: HTTP {(int)response.StatusCode} {url.Host}");
                return null;
            }
            var bytes = await ReadLimitedAsync(response, MaxImageBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null || !IsImage(bytes))
            {
                SubtitleLog.Write($"reklam görseli kullanılmadı (2 MB'tan büyük ya da görsel değil): {url.Host}");
                return null;
            }
            WriteAtomic(path, bytes);
            return path;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidOperationException)
        {
            SubtitleLog.Write($"reklam görseli inmedi: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    /// <summary>Adresin SHA-256 özeti (ilk 32 onaltılık hane) ve bilinen uzantı; aynı adres hep aynı dosyaya gider.</summary>
    public static string ImageFileName(Uri url)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))[..32];
        var extension = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        return hash + (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" ? extension : ".img");
    }

    /// <summary>PNG, JPEG, GIF ya da WEBP imzası.</summary>
    public static bool IsImage(ReadOnlySpan<byte> data) =>
        data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        || data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
        || data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)
        || data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8);

    /// <summary>Yanıtı en çok <paramref name="limit"/> bayt okur; aşarsa null.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, long limit, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > limit) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private void Prune(RemoteConfig config)
    {
        try
        {
            if (!Directory.Exists(AssetFolder)) return;
            var keep = config.Ads.Select(ad => ad.Icon).Concat(config.Popups.Select(popup => popup.Image))
                .Concat(config.Games.Select(game => game.Cover).OfType<Uri>())
                .Concat(config.Support?.Icon is { } icon ? [icon] : Array.Empty<Uri>()).Select(ImageFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(AssetFolder))
                if (!keep.Contains(Path.GetFileName(file))) File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }

    public void Dispose() => http.Dispose();
}
