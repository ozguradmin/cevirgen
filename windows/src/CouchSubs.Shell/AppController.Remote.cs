// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen: uzaktan ayarlar (reklam bandı, açılır reklam, duyuru, oyun kapakları) ve anonim kullanım sayımı. Arayüzden bağımsızdır:
/// görünümler <see cref="RemoteAds"/>, <see cref="RemotePopups"/>, <see cref="CurrentAnnouncement"/> ve
/// <see cref="AnonymousCount"/>'u okur; değişiklikler <see cref="AppController.PropertyChanged"/> (ve duyuru için
/// <see cref="AnnouncementChanged"/>) ile arayüz iş parçacığında bildirilir. Ağ işleri arka planda yürür, açılışı
/// bekletmez ve arayüze hata fırlatmaz.
/// </summary>
public sealed partial class AppController
{
    /// <summary>İlk açılışta ve Ayarlar'daki "Anonim sayım" satırında gösterilecek açıklama.</summary>
    public const string AnonymousCountDisclosure = UsageCounter.Disclosure;
    /// <summary>İlk açılıştaki platform sorusunun düz yazısı: açıklama ve "Ayarlar › Uygulama'dan kapatabilirsin." (anahtar yok).</summary>
    public const string AnonymousCountOnboardingText = UsageCounter.OnboardingDisclosure;
    /// <summary>Kapatılan duyuruların kimlikleri (veri klasöründe); kapatılan duyuru geri gelmez.</summary>
    public const string DismissedAnnouncementsFileName = "dismissed-announcements.json";
    private const int MaxDismissedAnnouncements = 200;

    private RemoteContent remoteContent = RemoteContent.Empty;
    private Announcement? currentAnnouncement;
    private readonly HashSet<string> dismissedAnnouncements = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource remoteStop = new();
    private readonly object remoteGate = new();
    private Task? remoteRefresh;
    private Timer? remoteTimer;
    private Timer? countTimer;

    /// <summary>Duyuru değişti (yeni geldi, sunucudan kalktı ya da kapatıldı); null ise gösterilecek duyuru yok.</summary>
    public event Action<Announcement?>? AnnouncementChanged;

    /// <summary>Alt reklam bandının reklamları (simgesi diske inmiş olanlar, sunucudaki sırayla). Değişince bildirilir.</summary>
    public IReadOnlyList<ResolvedAd> RemoteAds => remoteContent.Ads;

    /// <summary>Açılır reklamlar (görseli diske inmiş olanlar). Ne zaman gösterileceği <see cref="PopupAdPolicy"/>'de.</summary>
    public IReadOnlyList<ResolvedPopup> RemotePopups => remoteContent.Popups;

    /// <summary>
    /// Oyun seçicisinin sunucudaki oyunları (<c>games</c>): adları, takma adları ve diske inmiş kapakları (inmediyse null).
    /// Kullanıcının kendi kapağı bunlardan önce gelir (<see cref="GameTiles"/>).
    /// </summary>
    public IReadOnlyList<ResolvedGame> RemoteGames => remoteContent.Games;

    /// <summary>Gösterilecek duyuru: sunucudaki duyuru, kullanıcı kapatmadıysa. Yoksa null.</summary>
    public Announcement? CurrentAnnouncement => currentAnnouncement;

    /// <summary>Duyuruyu kapatır; kimliği diske yazılır, aynı duyuru bir daha gösterilmez.</summary>
    public void DismissAnnouncement(string id)
    {
        if (string.IsNullOrEmpty(id) || !dismissedAnnouncements.Add(id)) return;
        SaveDismissedAnnouncements();
        SubtitleLog.Write($"duyuru kapatıldı: {SubtitleLog.Excerpt(id)}");
        UpdateAnnouncement();
    }

    /// <summary>Gösterilen duyurunun bağlantısını tarayıcıda açar (bağlantısı yoksa hiçbir şey olmaz).</summary>
    public void OpenAnnouncement()
    {
        if (currentAnnouncement?.Link is { } link) OpenInBrowser(link);
    }

    /// <summary>
    /// Sunucudan şimdi bakar (açılışta ve saatte bir kendiliğinden yapılır). Ağ yoksa son geçerli içerik kalır. Sürmekte
    /// olan bir bakış varsa onu bekler. Hata fırlatmaz.
    /// </summary>
    public Task RefreshRemoteConfigAsync()
    {
        if (services.RemoteConfig is not { } client) return Task.CompletedTask;
        lock (remoteGate)
        {
            if (remoteRefresh is { IsCompleted: false } running) return running;
            return remoteRefresh = Task.Run(() => RefreshRemoteAsync(client));
        }
    }

    private async Task RefreshRemoteAsync(RemoteConfigClient client)
    {
        try
        {
            var token = remoteStop.Token;
            if (await client.FetchAsync(token).ConfigureAwait(false) is not { } config) return;
            var content = await client.ResolveAsync(config, download: true, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested) post(() => ApplyRemote(content));
        }
        catch (Exception error)
        {
            // Uzaktan içerik hiçbir koşulda uygulamayı düşürmemeli (kapanışta iptal, disk, beklenmeyen yanıt).
            SubtitleLog.Write($"uzaktan ayarlar uygulanamadı: {error.GetType().Name}: {error.Message}");
        }
    }

    // --- Anonim sayım ---

    /// <summary>
    /// Anonim kullanım sayımı açık mı (varsayılan açık). Kapatılınca sinyaller durur ve kurulum kimliği silinir; yeniden
    /// açılınca yeni kimlik oluşturulur. Açıklama: <see cref="AnonymousCountDisclosure"/>.
    /// </summary>
    public bool AnonymousCount
    {
        get => settings.AnonymousCount;
        set => Settings = settings with { AnonymousCount = value };
    }

    /// <summary>Sayımda gönderilen platform (ps5, xbox, pc, capture); bkz. <see cref="UsagePlatform"/>.</summary>
    public string CountPlatform => UsagePlatform.For(consoleKind, settings);

    private void StartCevirgenServices()
    {
        LoadDismissedAnnouncements();
        if (services.RemoteConfig is { } client)
        {
            // Önbellekteki son içerik hemen (yalnız disk: küçük JSON ve görsel dosyaları); ağdan bakış arka planda.
            try
            {
                if (client.LoadCached() is { } cached) ApplyRemote(client.ResolveAsync(cached, download: false).GetAwaiter().GetResult());
            }
            catch (Exception error)
            {
                SubtitleLog.Write($"uzaktan ayar önbelleği okunamadı: {error.GetType().Name}: {error.Message}");
            }
            remoteTimer = new Timer(_ => _ = RefreshRemoteConfigAsync(), null, TimeSpan.Zero, RemoteConfigClient.RefreshInterval);
        }
        if (services.Counter is { } counter)
        {
            counter.SetEnabled(settings.AnonymousCount);
            if (settings.AnonymousCount) StartCounting();
        }
    }

    private void StopCevirgenServices()
    {
        remoteStop.Cancel();
        remoteTimer?.Dispose();
        countTimer?.Dispose();
        countTimer = null;
    }

    private void AnonymousCountChanged(bool enabled)
    {
        SubtitleLog.Write(enabled ? "anonim sayım açıldı" : "anonim sayım kapatıldı (kimlik silindi)");
        if (services.Counter is not { } counter) return;
        counter.SetEnabled(enabled);
        if (enabled) StartCounting();
        else
        {
            countTimer?.Dispose();
            countTimer = null;
        }
    }

    /// <summary>Hemen bir sinyal, sonra <see cref="UsageCounter.Interval"/> aralıkla.</summary>
    private void StartCounting()
    {
        countTimer?.Dispose();
        countTimer = new Timer(_ => PingNow(), null, TimeSpan.Zero, UsageCounter.Interval);
    }

    /// <summary>Çeviri başladı ya da durdu: sayım açıksa hemen bir sinyal (oynuyor bilgisi güncellensin).</summary>
    private void RunningChangedForCount()
    {
        if (countTimer is not null) PingNow();
    }

    private void PingNow()
    {
        if (services.Counter is not { Enabled: true } counter) return;
        // Deneme modu (uygulamanın örnek kareleri) oyun sayılmaz.
        var playing = isRunning && !demoMode;
        var platform = CountPlatform;
        _ = Task.Run(() => counter.PingAsync(platform, playing));
    }

    // --- İçerik ve duyuru ---

    private void ApplyRemote(RemoteContent content)
    {
        var old = remoteContent;
        remoteContent = content;
        if (!old.Ads.SequenceEqual(content.Ads)) Changed(nameof(RemoteAds));
        if (!old.Popups.SequenceEqual(content.Popups)) Changed(nameof(RemotePopups));
        if (!old.Games.SequenceEqual(content.Games)) Changed(nameof(RemoteGames));
        if (!Equals(old.Support, content.Support)) Changed(nameof(Support));
        UpdateAnnouncement();
    }

    private void UpdateAnnouncement()
    {
        var next = remoteContent.Announcement is { } announcement && !dismissedAnnouncements.Contains(announcement.Id) ? announcement : null;
        if (Equals(next, currentAnnouncement)) return;
        currentAnnouncement = next;
        Changed(nameof(CurrentAnnouncement));
        AnnouncementChanged?.Invoke(next);
    }

    private string? DismissedAnnouncementsFile =>
        services.DataFolder is { } folder ? Path.Combine(folder, DismissedAnnouncementsFileName) : null;

    private void LoadDismissedAnnouncements()
    {
        if (DismissedAnnouncementsFile is not { } file) return;
        try
        {
            if (!File.Exists(file)) return;
            foreach (var id in JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [])
                if (!string.IsNullOrEmpty(id)) dismissedAnnouncements.Add(id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            SubtitleLog.Write($"kapatılan duyurular okunamadı: {error.GetType().Name}");
        }
    }

    private void SaveDismissedAnnouncements()
    {
        if (DismissedAnnouncementsFile is not { } file) return;
        try
        {
            // Sınırsız büyümesin: en çok son 200 kimlik (eski duyurular sunucudan çoktan kalkmıştır).
            var ids = dismissedAnnouncements.ToList();
            if (ids.Count > MaxDismissedAnnouncements) ids = ids[^MaxDismissedAnnouncements..];
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(ids));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SubtitleLog.Write($"kapatılan duyurular yazılamadı: {error.GetType().Name}");
        }
    }
}
