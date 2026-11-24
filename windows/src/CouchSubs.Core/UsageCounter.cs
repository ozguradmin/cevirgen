// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CouchSubs.Core;

/// <summary>
/// Anonim kullanım sayımı (Ayarlar › Anonim sayım, varsayılan açık): Çevirgen'in kendi sunucusuna (<see cref="CevirgenApi"/>)
/// <c>POST /v1/ping</c> ile yalnız rastgele kurulum kimliği (UUID v4, veri klasöründeki <see cref="IdFileName"/>), uygulama
/// sürümü, platform (ps5, xbox, pc, capture, unknown) ve çeviri sürüyor mu gider. Ad, hesap, oyun, altyazı gitmez; sunucu
/// IP adresini saklamaz. Kapatılınca kimlik dosyası silinir, yeniden açılınca yenisi oluşturulur. Gönderim hatası sessizdir
/// (yalnız günlük); hiçbir yöntem hata fırlatmaz. Google'a ya da CouchSubs sunucularına hiçbir şey gitmez.
/// </summary>
public sealed partial class UsageCounter : IDisposable
{
    /// <summary>İlk açılışta ve Ayarlar'da gösterilecek açıklama.</summary>
    public const string Disclosure = DisclosureBody + " Ayarlardan kapatabilirsin.";

    /// <summary>Ne gönderildiği ve ne saklanmadığı (açıklamanın "kapatabilirsin" cümlesinden önceki kısmı).</summary>
    public const string DisclosureBody =
        "Çevirgen kaç kişinin kullandığını görmek için 5 dakikada bir rastgele bir kimlik, sürüm ve platform gönderir. " +
        "Adın, oyunun, altyazıların ya da IP adresin saklanmaz.";

    /// <summary>İlk açılıştaki platform sorusunda düz yazı (orada anahtar yok; kapatmak yalnız Ayarlar › Uygulama'da).</summary>
    public const string OnboardingDisclosure = DisclosureBody + " Ayarlar › Uygulama'dan kapatabilirsin.";

    public const bool EnabledByDefault = true;
    public const string IdFileName = "install-id";
    /// <summary>Uygulama açıkken bu aralıkla; çeviri başlayınca/durunca hemen (sunucu 6 dakikayı "çevrimiçi" sayar).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public static readonly IReadOnlySet<string> Platforms = new HashSet<string> { "ps5", "xbox", "pc", "capture", "unknown" };

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex UuidV4();

    [GeneratedRegex("[^0-9A-Za-z.+-]")]
    private static partial Regex VersionJunk();

    private readonly object gate = new();
    private readonly CevirgenEndpoints endpoints;
    private readonly string idFile;
    private readonly string version;
    private readonly HttpClient http;
    private bool enabled;

    /// <param name="idFile">Kurulum kimliğinin dosyası (%LOCALAPPDATA%\Cevirgen\install-id); ilk gönderimde oluşturulur.</param>
    /// <param name="handler">HTTP katmanı; testlerde sahtesi verilir. Verilmezse çerezsiz bir katman kurulur.</param>
    public UsageCounter(Uri baseUrl, string idFile, string appVersion, bool enabled = EnabledByDefault, HttpMessageHandler? handler = null)
        : this([baseUrl], idFile, appVersion, enabled, handler) { }

    /// <param name="baseUrls">Sunucunun adresleri, sırayla (asıl adres, yedek); bkz. <see cref="CevirgenEndpoints"/>.</param>
    public UsageCounter(IReadOnlyList<Uri> baseUrls, string idFile, string appVersion, bool enabled = EnabledByDefault, HttpMessageHandler? handler = null)
    {
        endpoints = new CevirgenEndpoints(baseUrls);
        this.idFile = idFile;
        version = CleanVersion(appVersion);
        this.enabled = enabled;
        http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false }) { Timeout = CevirgenApi.Timeout };
        if (!enabled) DeleteId();
    }

    public bool Enabled
    {
        get { lock (gate) return enabled; }
    }

    /// <summary>Kapatınca kimlik dosyası silinir; açınca bir sonraki gönderimde yenisi oluşturulur.</summary>
    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            enabled = value;
            if (!value) DeleteId();
        }
    }

    /// <summary>Açıksa kurulum kimliği (yoksa oluşturulur); kapalıysa null.</summary>
    public string? InstallId
    {
        get
        {
            lock (gate) return enabled ? ReadOrCreateId() : null;
        }
    }

    /// <summary>Tek sinyal gönderir; kapalıysa hiçbir şey yapmaz. Sunucu 2xx döndüyse true; hata fırlatmaz.</summary>
    public async Task<bool> PingAsync(string platform, bool playing, CancellationToken cancellationToken = default)
    {
        string? id;
        lock (gate) id = enabled ? ReadOrCreateId() : null;
        if (id is null) return false;
        try
        {
            var body = Body(id, version, platform, playing);
            using var response = await endpoints.SendAsync(http, baseUrl => new HttpRequestMessage(HttpMethod.Post, CevirgenApi.PingUrl(baseUrl))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;
            SubtitleLog.Write($"anonim sayım gönderilemedi: HTTP {(int)response.StatusCode}");
            return false;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException
                                          or InvalidOperationException)
        {
            SubtitleLog.Write($"anonim sayım gönderilemedi: {error.GetType().Name}");
            return false;
        }
    }

    /// <summary>Sunucunun beklediği gövde: <c>{"id","v","p","playing"}</c>. Bilinmeyen platform "unknown" olur.</summary>
    public static string Body(string id, string version, string platform, bool playing) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id, ["v"] = version, ["p"] = Platforms.Contains(platform) ? platform : "unknown", ["playing"] = playing,
        });

    /// <summary>Sunucunun kabul ettiği sürüm metni (harf, rakam, . - +; en çok 24); boşsa "0.0.0".</summary>
    public static string CleanVersion(string? version)
    {
        var clean = VersionJunk().Replace(version?.Trim() ?? "", "");
        if (clean.Length > 24) clean = clean[..24];
        return clean.Length == 0 ? "0.0.0" : clean;
    }

    public static bool IsValidId(string? id) => id is not null && UuidV4().IsMatch(id);

    /// <summary>Yeni rastgele UUID v4 (küçük harf).</summary>
    public static string NewId() => Guid.NewGuid().ToString("D").ToLowerInvariant();

    private string? ReadOrCreateId()
    {
        try
        {
            if (File.Exists(idFile) && File.ReadAllText(idFile).Trim() is var saved && IsValidId(saved)) return saved;
            var id = NewId();
            Directory.CreateDirectory(Path.GetDirectoryName(idFile)!);
            File.WriteAllText(idFile, id);
            return id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SubtitleLog.Write($"anonim sayım kimliği yazılamadı: {error.GetType().Name}");
            return null;
        }
    }

    private void DeleteId()
    {
        try
        {
            if (File.Exists(idFile)) File.Delete(idFile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SubtitleLog.Write($"anonim sayım kimliği silinemedi: {error.GetType().Name}");
        }
    }

    public void Dispose() => http.Dispose();
}
