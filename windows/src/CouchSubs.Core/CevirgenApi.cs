// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using System.Text.Json;

namespace CouchSubs.Core;

/// <summary>
/// Çevirgen'in kendi sunucusu (server/cevirgen-api; Cloudflare Worker). Uygulama yalnız iki istek yapar:
/// <c>GET /v1/config</c> (reklam, açılır reklam, duyuru; çerezsiz, kimliksiz düz istek) ve isteğe bağlı
/// <c>POST /v1/ping</c> (anonim kullanım sayımı, bkz. <see cref="UsageCounter"/>). Adres tek yerde: bu sabitler; derlemede
/// <c>-p:CevirgenApiUrl=…</c> (AssemblyMetadata) ya da <c>CEVIRGEN_API_URL</c> ortam değişkeni (yerel deneme) onları ezer.
/// Asıl adres kendi alan adıdır (<see cref="DefaultUrl"/>); ona ağ ya da TLS hatasıyla ulaşılamazsa (alan adının sertifikası
/// henüz hazır değil, DNS yayılmadı) aynı Worker'ın workers.dev adresine (<see cref="FallbackUrl"/>) geçilir
/// (<see cref="CevirgenEndpoints"/>).
/// </summary>
public static class CevirgenApi
{
    public const string DefaultUrl = "https://cevirgen.ozgurguler.tech";
    /// <summary>Aynı Worker'ın Cloudflare adresi: asıl adrese ulaşılamazsa kullanılır.</summary>
    public const string FallbackUrl = "https://cevirgen-api.ozgurglr256.workers.dev";
    public const string EnvironmentVariable = "CEVIRGEN_API_URL";
    public const string MetadataKey = "CevirgenApiUrl";
    /// <summary>Sunucuya giden her istek kısa sürede biter; ağ yavaşsa uygulama beklemez.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Kullanılacak adres: önce ortam değişkeni, sonra derlemedeki <c>CevirgenApiUrl</c>, yoksa <see cref="DefaultUrl"/>.
    /// Yalnız https kabul edilir (deneme için localhost/127.0.0.1 üzerinde http de olur); geçersiz değer atlanır.
    /// </summary>
    public static Uri BaseUrl(Func<string, string?>? environment = null, Assembly? assembly = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (Parse(environment(EnvironmentVariable)) is { } fromEnvironment) return fromEnvironment;
        var fromBuild = (assembly ?? Assembly.GetEntryAssembly())?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        return Parse(fromBuild) ?? new Uri(DefaultUrl + "/");
    }

    /// <summary>Adresi sona eğik çizgiyle döner (göreli yollar doğru birleşsin); geçersizse null.</summary>
    public static Uri? Parse(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Contains("$(") || !Uri.TryCreate(text, UriKind.Absolute, out var url)) return null;
        var allowed = url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp && url.IsLoopback;
        if (!allowed || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)) return null;
        return url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
    }

    /// <summary>
    /// Denenecek adresler, sırayla: ortam değişkeni ya da derlemedeki adres verildiyse yalnız o (yerel deneme yedeğe
    /// kaçmasın); yoksa asıl adres ve yedeği.
    /// </summary>
    public static IReadOnlyList<Uri> BaseUrls(Func<string, string?>? environment = null, Assembly? assembly = null)
    {
        var chosen = BaseUrl(environment, assembly);
        return chosen.AbsoluteUri == DefaultUrl + "/" ? [chosen, new Uri(FallbackUrl + "/")] : [chosen];
    }

    public static Uri ConfigUrl(Uri baseUrl) => new(baseUrl, "v1/config");
    public static Uri PingUrl(Uri baseUrl) => new(baseUrl, "v1/ping");
}

/// <summary>
/// Çevirgen sunucusunun adresleri ve sırayla deneme: istek önce son çalışan adrese gider; ağ hatası (DNS, bağlantı, TLS),
/// zaman aşımı ya da kenar hatası (5xx; Cloudflare 52x) olursa sıradaki adres denenir. Çalışan adres hatırlanır (sonraki
/// istekler beklemeden ona gider). Son adres de olmazsa onun hatası ya da yanıtı olduğu gibi döner.
/// </summary>
public sealed class CevirgenEndpoints
{
    private readonly IReadOnlyList<Uri> bases;
    private int preferred;

    public CevirgenEndpoints(IReadOnlyList<Uri> bases)
    {
        if (bases.Count == 0) throw new ArgumentException("En az bir adres gerekir.", nameof(bases));
        this.bases = bases;
    }

    public IReadOnlyList<Uri> Bases => bases;

    /// <summary>Şu an önce denenen adres.</summary>
    public Uri Current => bases[Volatile.Read(ref preferred)];

    /// <param name="request">Adres için yeni istek (her denemede yenisi; gövde yeniden gönderilebilsin).</param>
    public async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<Uri, HttpRequestMessage> request,
                                                     HttpCompletionOption option = HttpCompletionOption.ResponseContentRead,
                                                     CancellationToken cancellationToken = default)
    {
        var start = Volatile.Read(ref preferred);
        for (var attempt = 0; ; attempt++)
        {
            var index = (start + attempt) % bases.Count;
            var last = attempt == bases.Count - 1;
            try
            {
                var response = await http.SendAsync(request(bases[index]), option, cancellationToken).ConfigureAwait(false);
                if (!last && IsEdgeFailure(response.StatusCode))
                {
                    SubtitleLog.Write($"Çevirgen sunucusu {bases[index].Host} HTTP {(int)response.StatusCode}; yedek adres deneniyor");
                    response.Dispose();
                    continue;
                }
                Volatile.Write(ref preferred, index);
                return response;
            }
            catch (Exception error) when (!last && (error is HttpRequestException
                                                    || error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                SubtitleLog.Write($"Çevirgen sunucusuna ulaşılamadı ({bases[index].Host}: {error.GetType().Name}); yedek adres deneniyor");
            }
        }
    }

    /// <summary>Sunucunun değil araya giren kenarın (Cloudflare 52x, ağ geçidi) hatası: başka adres denenmeye değer.</summary>
    public static bool IsEdgeFailure(System.Net.HttpStatusCode status) => (int)status is >= 500 and <= 599;
}

/// <summary>Alt reklam bandının bir reklamı (sunucudaki <c>ads</c>): simge ve bağlantı yalnız https.</summary>
public sealed record RemoteAd(string Id, string Title, string Text, Uri Icon, Uri Link);

/// <summary>Günde bir açılan reklam (sunucudaki <c>popups</c>): yalnız görsel; görsel ve bağlantı yalnız https.</summary>
public sealed record RemotePopup(string Id, Uri Image, Uri Link);

/// <summary>Ana ekranın duyurusu (sunucudaki <c>announcement</c>); bağlantı isteğe bağlı, varsa https.</summary>
public sealed record Announcement(string Id, string Text, Uri? Link);

/// <summary>
/// Sunucunun <c>/v1/config</c> yanıtı. <see cref="Parse"/> bozuk ya da eksik girdiyi atlar: https olmayan simge, görsel
/// ya da bağlantı, boş başlık; kök nesne değilse yanıtın tamamı geçersizdir (null).
/// </summary>
public sealed record RemoteConfig(int Version, IReadOnlyList<RemoteAd> Ads, IReadOnlyList<RemotePopup> Popups, Announcement? Announcement)
{
    public static readonly RemoteConfig Empty = new(1, [], [], null);

    /// <summary>Yerel önbellek dosyasının adı (veri klasöründe): son geçerli yanıtın kendisi.</summary>
    public const string CacheFileName = "remote-config.json";

    public bool Equals(RemoteConfig? other) =>
        other is not null && Version == other.Version && Ads.SequenceEqual(other.Ads) && Popups.SequenceEqual(other.Popups)
        && Equals(Announcement, other.Announcement);

    public override int GetHashCode() => HashCode.Combine(Version, Ads.Count, Popups.Count, Announcement);

    public static RemoteConfig? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 1;
            var ads = new List<RemoteAd>();
            foreach (var ad in Items(root, "ads"))
            {
                var title = Text(ad, "title");
                if (string.IsNullOrWhiteSpace(title) || Https(Text(ad, "icon")) is not { } icon || Https(Text(ad, "link")) is not { } link) continue;
                ads.Add(new RemoteAd(Text(ad, "id") ?? icon.AbsoluteUri, title.Trim(), Text(ad, "text")?.Trim() ?? "", icon, link));
            }
            var popups = new List<RemotePopup>();
            foreach (var popup in Items(root, "popups"))
            {
                if (Https(Text(popup, "image")) is not { } image || Https(Text(popup, "link")) is not { } link) continue;
                popups.Add(new RemotePopup(Text(popup, "id") ?? image.AbsoluteUri, image, link));
            }
            Announcement? announcement = null;
            if (root.TryGetProperty("announcement", out var a) && a.ValueKind == JsonValueKind.Object
                && Text(a, "text") is { } text && !string.IsNullOrWhiteSpace(text))
            {
                var linkText = Text(a, "link");
                // Bağlantı varsa https olmalı; yoksa duyuru bağlantısız gösterilir.
                var announcementLink = Https(linkText);
                announcement = new Announcement(Text(a, "id") is { Length: > 0 } id ? id : text.Trim(), text.Trim(), announcementLink);
            }
            return new RemoteConfig(version, ads, popups, announcement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList()
            : [];

    /// <summary>Metin alanı; yönetim paneli düz metin yazar, eski biçimdeki {"tr": …, "en": …} nesnesinden Türkçesi alınır.</summary>
    private static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var language in new[] { "tr", "en" })
                if (value.TryGetProperty(language, out var localized) && localized.ValueKind == JsonValueKind.String)
                    return localized.GetString();
        return null;
    }

    /// <summary>Yalnız mutlak https adresi; değilse null.</summary>
    public static Uri? Https(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(url.Host) ? url : null;
}
