// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cevirgen.Core;

/// <summary>
/// Çevirgen ortak havuzu ("Çevirileri iyileştirmeye katkı"; varsayılan açık, Ayarlar › Uygulama'dan kapatılabilir; ilk
/// açılışta açıkça söylenir): ekranda gerçekten gösterilen İngilizce satırlar, o satırın gösterilen Türkçesi, çeviren motorun
/// adı ("free", "deepl", "gemini:&lt;model&gt;", kullanıcının kendi düzeltmesi "user"), satırın geldiği yer (altyazı, ek alan,
/// ekran yazısı) ve biliniyorsa oyunun kimliğiyle adı biriktirilir; beş dakikada bir (ve uygulama kapanırken) Çevirgen'in
/// sunucusuna (<c>POST /v1/lines</c>, server/cevirgen-api) gönderilir. Kimlik olarak yalnız bu kuyruğa özgü rastgele bir UUID
/// gider (IP, kullanıcı adı, PSN bilgisi yok); kişisel bilgiye benzeyen satır (<see cref="PersonalText"/>), arayüz ve yazı
/// tanıma çöpü, PS5'in sistem yazıları (<see cref="PoolFilter"/>, <see cref="RemotePlayNotice"/>) hiç kuyruğa girmez. Sunucuya
/// ulaşılamazsa kuyruk diskte bekler, sonra sessizce yeniden denenir. Şimdilik yalnız toplanır; paketler sonra bunlardan
/// hazırlanacak (server/cevirgen-api/README.md).
/// Çevirgen'in LinePool'undan (AGPL-3.0) uyarlanmıştır: kuyruk, parça parça gönderim ve 429 beklemesi aynı.
/// </summary>
public sealed class LinePool : IDisposable
{
    /// <summary>Açık; Ayarlar › Uygulama'dan kapatılabilir (kapatınca kuyruk da silinir).</summary>
    public const bool EnabledByDefault = true;
    /// <summary>Sunucunun yolu (<see cref="CevirgenApi"/> adreslerine göre).</summary>
    public const string Path = "v1/lines";
    /// <summary>Kuyruğun dosyası (veri klasöründe).</summary>
    public const string FileName = "lines-queue.json";
    /// <summary>İstek başına en çok satır (sunucu 200'den fazlasını keser, gövdeyi 256 KB ile sınırlar).</summary>
    public const int BatchSize = 100;
    public const int MaxQueue = 3000;
    public const int MaxLength = 300;
    public const int MaxTranslation = 600;
    public const int MinWords = 2;
    /// <summary>Aynı satır kuyrukta beklerken kaç kez görüldüğü (sunucuda "görülme" sayısı) en çok bu kadar sayılır.</summary>
    public const int MaxSightings = 50;
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);
    /// <summary>Gönderilen satırlar o gün (UTC) hatırlanır: aynı satır aynı gün yeniden gitmez, ertesi gün bir kez daha gider.</summary>
    internal const int RememberedSent = 20000;
    /// <summary>Kuyruk en geç bu aralıkla diske yazılır (her satırda yazılmaz).</summary>
    internal static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    /// <summary>Satırın ekrandaki yeri: altyazı, kullanıcının ek alanı (hazır alanlar dahil) ya da kendiliğinden bulunan ekran yazısı.</summary>
    public enum Area { Subtitle, Extra, Screen }

    /// <summary>İlk açılıştaki açıklamanın ortak havuz cümlesi.</summary>
    public const string Disclosure =
        "Çevirileri iyileştirmek için ekranda okunan İngilizce satırlar ve çevirileri de oyunun adıyla gönderilir; kişisel bilgi süzülür.";

    /// <summary>Kullanıcının kendi düzeltmesinin motor adı.</summary>
    public const string UserEngine = "user";

    /// <summary>Satırın oyunu: uygulamanın bildiği kimlik (<c>gta-6</c>; sunucudaki oyun listesinden) ve ad.</summary>
    public sealed record GameRef(
        [property: JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id,
        [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title)
    {
        /// <summary>Geçerli kimlik ya da boş olmayan ad yoksa null.</summary>
        public static GameRef? Create(string? id, string? title)
        {
            var cleanId = RemoteGame.IsValidId(id) ? id : null;
            var cleanTitle = RunningGame.NormalizedName(title);
            return cleanId is null && cleanTitle is null ? null : new GameRef(cleanId, cleanTitle);
        }
    }

    /// <summary>Kuyruktaki satır (diskteki biçim). <see cref="Key"/> İngilizcenin anahtarıdır (<see cref="PackKey"/>).</summary>
    public sealed record Line(
        [property: JsonPropertyName("en")] string En,
        [property: JsonPropertyName("tr")] string Tr,
        [property: JsonPropertyName("engine")] string Engine,
        [property: JsonPropertyName("area")] string Area,
        [property: JsonPropertyName("at")] string At,
        [property: JsonPropertyName("n")] int N,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("game"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameRef? Game = null)
    {
        /// <summary>Kuyrukta aynı satır: aynı İngilizce, aynı motor, aynı oyun.</summary>
        internal string Identity => $"{Key}|{Engine}|{Game?.Id}|{Game?.Title}";
    }

    private sealed class State
    {
        [JsonPropertyName("install")] public string Install { get; set; } = Guid.NewGuid().ToString();
        [JsonPropertyName("queue")] public List<Line> Queue { get; set; } = [];
        [JsonPropertyName("sentDay")] public string SentDay { get; set; } = "";
        [JsonPropertyName("sent")] public List<string> Sent { get; set; } = [];
        /// <summary>Unix saniyesi; sunucu 429 ile beklemeyi istediyse.</summary>
        [JsonPropertyName("retryAfter")] public double? RetryAfter { get; set; }
    }

    private sealed record SentLine(
        [property: JsonPropertyName("en")] string En,
        [property: JsonPropertyName("tr")] string Tr,
        [property: JsonPropertyName("engine")] string Engine,
        [property: JsonPropertyName("area")] string Area,
        [property: JsonPropertyName("at")] string At,
        [property: JsonPropertyName("n")] int N);

    private sealed record Payload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("v")] string Version,
        [property: JsonPropertyName("game")] GameRef? Game,
        [property: JsonPropertyName("lines")] IReadOnlyList<SentLine> Lines);

    private readonly object gate = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly string file;
    private readonly string version;
    private readonly CevirgenEndpoints endpoints;
    private readonly HttpClient http;
    private readonly Func<DateTimeOffset> clock;
    private readonly State state;
    private readonly Dictionary<string, int> queued = [];
    private readonly HashSet<string> sentKeys;
    private DateTimeOffset lastSave = DateTimeOffset.MinValue;
    private bool dirty;
    private bool enabled;
    private GameRef? game;

    /// <param name="file">Kuyruk dosyası (%LOCALAPPDATA%\Cevirgen\lines-queue.json).</param>
    /// <param name="appVersion">Uygulama sürümü (sunucu <c>v</c> alanı).</param>
    /// <param name="baseUrls">Çevirgen sunucusunun adresleri (asıl ve yedek; <see cref="CevirgenApi.BaseUrls"/>).</param>
    /// <param name="handler">HTTP katmanı; testlerde sahtesi verilir.</param>
    public LinePool(string file, string appVersion, IReadOnlyList<Uri>? baseUrls = null, bool enabled = EnabledByDefault,
        HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        this.file = file;
        version = UsageCounter.CleanVersion(appVersion);
        this.enabled = enabled;
        endpoints = new CevirgenEndpoints(baseUrls ?? CevirgenApi.BaseUrls());
        http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        state = Load(file) ?? new State();
        state.Install = Guid.TryParse(state.Install, out var id) ? id.ToString() : Guid.NewGuid().ToString();
        // Eski ya da bozuk kayıtlar atılır.
        state.Queue.RemoveAll(line => string.IsNullOrWhiteSpace(line.En) || string.IsNullOrWhiteSpace(line.Tr) || string.IsNullOrEmpty(line.Key)
                                      || string.IsNullOrEmpty(line.Engine) || string.IsNullOrEmpty(line.Area));
        for (var i = 0; i < state.Queue.Count; i++) queued[state.Queue[i].Identity] = i;
        sentKeys = [.. state.Sent];
        if (!enabled) SetEnabled(false);
    }

    public IReadOnlyList<Line> Queued { get { lock (gate) return [.. state.Queue]; } }
    public string InstallId => state.Install;
    public bool IsEnabled { get { lock (gate) return enabled; } }

    /// <summary>Kapatılınca bekleyen satırlar da silinir (kuyruk dosyası dahil): kullanıcı vazgeçtiyse hiçbir şey gitmez.</summary>
    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            enabled = value;
            if (value) return;
            state.Queue.Clear();
            queued.Clear();
            state.RetryAfter = null;
            dirty = false;
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Oynanan oyun (bilinmiyorsa null). Bundan sonra kuyruğa giren satırlar bu oyunla gider.</summary>
    public GameRef? Game { get { lock (gate) return game; } }

    public void SetGame(GameRef? value)
    {
        lock (gate) game = value;
    }

    /// <summary>Ekranda gösterilen satır ve çevirisi. Kapalıysa, süzgece takılırsa ya da bugün zaten gittiyse bir şey yapmaz.</summary>
    /// <param name="engine">Çeviren motor ("free", "deepl", "gemini:&lt;model&gt;", "user").</param>
    public void Offer(string english, string translation, string engine, Area area)
    {
        var en = Clean(english);
        var tr = Clean(translation);
        var label = EngineLabel(engine);
        if (!Accepts(en) || !AcceptsTranslation(en, tr) || label is null || PackKey.Key(en) is not { } key) return;
        lock (gate)
        {
            if (!enabled) return;
            var now = clock();
            ResetSentIfNewDay(now);
            var line = new Line(en, tr, label, AreaName(area), Timestamp(now), 1, key, game);
            var identity = line.Identity;
            if (queued.TryGetValue(identity, out var index))
            {
                // Kuyrukta bekleyen satır yeniden göründü: görülme sayısı artar, çeviri en sonuncusu olur.
                var existing = state.Queue[index];
                state.Queue[index] = existing with { Tr = tr, At = line.At, N = Math.Min(MaxSightings, existing.N + 1) };
            }
            else
            {
                if (sentKeys.Contains(identity)) return;
                state.Queue.Add(line);
                queued[identity] = state.Queue.Count - 1;
                // Sınır aşılırsa en eski satırlar düşer.
                if (state.Queue.Count > MaxQueue)
                {
                    state.Queue.RemoveRange(0, state.Queue.Count - MaxQueue);
                    Reindex();
                }
            }
            dirty = true;
            if (now - lastSave >= SaveInterval) SaveLocked();
        }
    }

    /// <summary>
    /// Kuyruğu oyun oyun, en çok <see cref="BatchSize"/> satırlık parçalar hâlinde gönderir. 429'da sunucunun istediği kadar
    /// bekler; başka 4xx'te parça atılır (yeniden göndermek düzeltmez); ulaşılamazsa ya da 5xx'te kuyruk kalır.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                List<Line> batch;
                string body;
                lock (gate)
                {
                    if (!enabled || state.Queue.Count == 0) return;
                    if (state.RetryAfter is { } retry && clock().ToUnixTimeMilliseconds() / 1000.0 < retry) return;
                    var first = state.Queue[0].Game;
                    batch = [.. state.Queue.Where(line => line.Game == first).Take(BatchSize)];
                    body = JsonSerializer.Serialize(new Payload(state.Install, version, first,
                        [.. batch.Select(line => new SentLine(line.En, line.Tr, line.Engine, line.Area, line.At, line.N))]));
                }
                HttpResponseMessage response;
                try
                {
                    response = await endpoints.SendAsync(http, baseUrl => new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, Path))
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    return;
                }
                using (response)
                {
                    var status = (int)response.StatusCode;
                    lock (gate)
                    {
                        if (status is >= 200 and < 300 || status is >= 400 and < 500 && status != (int)HttpStatusCode.TooManyRequests)
                        {
                            // Gönderildi ya da sunucu bu parçayı kabul etmiyor (aynı satırlar bugün bir daha gitmesin).
                            if (status >= 400) SubtitleLog.Write($"ortak havuz: sunucu parçayı kabul etmedi (HTTP {status}), {batch.Count} satır atıldı");
                            state.RetryAfter = null;
                            Remove(batch);
                        }
                        else if (status == (int)HttpStatusCode.TooManyRequests)
                        {
                            var now = clock();
                            state.RetryAfter = (now + RetryDelay(response.Headers.RetryAfter, now)).ToUnixTimeMilliseconds() / 1000.0;
                            dirty = true;
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
        }
        finally
        {
            Save();
            sending.Release();
        }
    }

    /// <summary>Kuyruğu diske yazar (uygulama kapanırken de çağrılır).</summary>
    public void Save()
    {
        lock (gate) SaveLocked();
    }

    private void SaveLocked()
    {
        if (!dirty || !enabled) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, file, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        dirty = false;
        lastSave = clock();
    }

    private void ResetSentIfNewDay(DateTimeOffset now)
    {
        var day = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (state.SentDay == day) return;
        state.SentDay = day;
        state.Sent.Clear();
        sentKeys.Clear();
        dirty = true;
    }

    private void Remove(List<Line> batch)
    {
        var identities = batch.Select(line => line.Identity).ToHashSet();
        state.Queue.RemoveAll(line => identities.Contains(line.Identity));
        ResetSentIfNewDay(clock());
        foreach (var identity in identities) if (sentKeys.Add(identity)) state.Sent.Add(identity);
        if (state.Sent.Count > RememberedSent)
        {
            var dropped = state.Sent.Count - RememberedSent;
            sentKeys.ExceptWith(state.Sent.Take(dropped));
            state.Sent.RemoveRange(0, dropped);
        }
        Reindex();
        dirty = true;
    }

    private void Reindex()
    {
        queued.Clear();
        for (var i = 0; i < state.Queue.Count; i++) queued[state.Queue[i].Identity] = i;
    }

    private static State? Load(string file)
    {
        try { return JsonSerializer.Deserialize<State>(File.ReadAllText(file)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    /// <summary>Denetim karakterleri atılır, boşluklar sadeleşir.</summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>Sunucudaki etiket: küçük harf, [a-z0-9._:/-], en çok 48 karakter; boşsa null.</summary>
    public static string? EngineLabel(string? engine)
    {
        if (string.IsNullOrWhiteSpace(engine)) return null;
        var label = new string([.. engine.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or ':' or '/' or '-')]);
        if (label.Length > 48) label = label[..48];
        return label.Length == 0 ? null : label;
    }

    public static string AreaName(Area area) => area switch { Area.Extra => "extra", Area.Screen => "screen", _ => "subtitle" };

    /// <summary>
    /// Havuza uygun İngilizce satır: en az iki sözcük, en çok 300 karakter; arayüz ya da yazı tanıma çöpü, Türkçe, PS5'in
    /// sistem yazısı, yarım okuma (<see cref="PoolFilter"/>), PS5'in Uzaktan Oynatma bildirimi ya da kişisel bilgiye benzeyen
    /// yazı değil.
    /// </summary>
    public static bool Accepts(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaxLength) return false;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(word => word.EnumerateRunes().Any(Rune.IsLetter));
        return words >= MinWords && !LooksLikeNoise(text) && PoolFilter.Accepts(text) && !RemotePlayNotice.Matches(text) && !PersonalText.Looks(text);
    }

    /// <summary>Çeviri yazılabilir mi: boş değil, aslıyla aynı değil, en çok 600 karakter, kişisel bilgi yok.</summary>
    public static bool AcceptsTranslation(string english, string translation) =>
        translation.Length is > 0 and <= MaxTranslation && translation != english && !PersonalText.Looks(translation)
        && !RemotePlayNotice.Matches(translation);

    /// <summary>Yazı tanıma çöpü içeren ("ou$3e") ya da harften çok rakam ve simge taşıyan ("100/100 HP 25%") satır.</summary>
    public static bool LooksLikeNoise(string text)
    {
        if (PackKey.Canon(OcrNoise.Clean(text)) != PackKey.Canon(text)) return true;
        var visible = text.EnumerateRunes().Where(rune => !Rune.IsWhiteSpace(rune)).ToList();
        var letters = visible.Count(Rune.IsLetter);
        return letters * 2 < visible.Count;
    }

    /// <summary><c>Retry-After</c>: saniye ya da HTTP tarihi; yoksa bir dakika, en çok bir gün.</summary>
    public static TimeSpan RetryDelay(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        var delay = header switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => TimeSpan.FromSeconds(60),
        };
        return TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 86400));
    }

    /// <summary>"2026-11-11T03:17:00Z" (UTC, saniye hassasiyeti).</summary>
    public static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        http.Dispose();
        sending.Dispose();
    }
}
