// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Shell;

/// <summary>
/// Canlı ekrandaki bildirimin önemi. Şeritte en önemli bildirim görünür; diğerleri "+N uyarı daha" ile açılır.
/// <list type="bullet">
/// <item><see cref="Blocking"/> (kırmızı, "Hata"): çeviri çalışamıyor ya da anlamsız çıkıyor; kullanıcı bir şey yapmadan düzelmez.</item>
/// <item><see cref="Action"/> (sarı-turuncu, "Yapılacak"): çeviri sürüyor ama bir şey eksik (TV uygulaması, izin, seçim).</item>
/// <item><see cref="Info"/> (gri, "Bilgi"): bilmekte yarar var; bir şey yapmak gerekmez.</item>
/// </list>
/// </summary>
public enum LiveNoticeLevel { Blocking, Action, Info }

/// <summary>
/// Canlı ekranın bildirimleri. Sıra önemlidir: aynı önemdeki bildirimler bu sırayla dizilir (önce gelen daha önemli).
/// </summary>
public enum LiveNoticeKind
{
    // ── Hata: çeviri çalışamıyor ──
    /// <summary>Ücretsiz çeviri motoru bu bilgisayarda açılamadı.</summary>
    EngineFailed,
    /// <summary>Ücretsiz çeviri seçili ama modeli inmedi (başka motor seçiliyse yedek olduğu için "Yapılacak").</summary>
    ModelMissing,
    /// <summary>Oyun dilinin Windows yazı tanıması kurulu değil: çeviri anlamsızlaşır.</summary>
    OcrMissing,
    /// <summary>Konsola bağlanılamıyor: Xbox girişi/seçimi eksik ya da oturum yeniden denemeyi bıraktı.</summary>
    Console,
    /// <summary>Çevirgen: okunacak pencere (Bilgisayar, Diğer) açık değil ya da simge durumunda; buradan başka pencere seçilir.</summary>
    WindowMissing,
    /// <summary>Çeviri sürerken görüntü 5 sn'dir gelmiyor.</summary>
    PictureStopped,

    // ── Yapılacak ──
    /// <summary>TV: eşleşmedi, uygulaması bağlı değil, eski ya da güncelleniyor.</summary>
    Tv,
    /// <summary>Windows Güvenlik Duvarı izni gerekiyor.</summary>
    Firewall,
    /// <summary>Çevrimiçi motor şu an çeviremiyor; satırlar bu arada ücretsiz çeviriyle geliyor.</summary>
    OnlineEngine,
    /// <summary>Oyunun altyazısı kapalı görünüyor (görüntü geliyor, satır yok).</summary>
    SubtitlesOff,
    /// <summary>Paketten tanınan oyunun kayıtlı alanları var: kullanılsın mı?</summary>
    AreaProfileOffer,

    // ── Bilgi ──
    /// <summary>Oyunda menü açık; altyazı yok.</summary>
    MenuOpen,
    /// <summary>Önceki çalışma beklenmedik biçimde kapandı (kayıt yalnız bu bilgisayarda).</summary>
    Crash,
    /// <summary>Oyunun kayıtlı alanları kullanılıyor ("Geri al").</summary>
    AreaProfileActive,
    /// <summary>Ekran kartı kullanılamadı; ücretsiz çeviri işlemcide.</summary>
    GpuFallback,
    /// <summary>Çevirgen: karede PS5'in "Uzaktan Oynatma'ya bağlandı" bildirimi okundu; nasıl gizleneceği.</summary>
    RemotePlayPopup,
    /// <summary>PS5'te oyunu ana hesapla başlat ipucu.</summary>
    MainAccountTip,
    /// <summary>Otomatik ekran yazısı deneysel; Ayarlar'dan açılabilir.</summary>
    AutoScreenText,
    /// <summary>Kurulum bitmedi: konsolsuz deneme modu.</summary>
    Demo,
}

/// <summary>Bildirimlerin hesabı için durum (denetleyiciden ve arayüzün bildiği kilitlenme kaydından).</summary>
public sealed record LiveNoticeState
{
    public bool Running { get; init; }
    public bool DemoMode { get; init; }
    public bool CanStart { get; init; } = true;
    public bool EngineFailed { get; init; }
    public bool ModelMissing { get; init; }
    /// <summary>Seçili motor ücretsiz çeviri (model eksikse çeviri hiç yapılamaz).</summary>
    public bool FreeEngine { get; init; } = true;
    public bool OcrMissing { get; init; }
    public bool ConsoleNeedsLink { get; init; }
    public bool ConsoleOffersReconnect { get; init; }
    public HomeProblemKind Problem { get; init; }
    public bool TvNeedsWork { get; init; }
    public FirewallPrompt Firewall { get; init; }
    public bool OnlineIssue { get; init; }
    public bool AreaProfileOffered { get; init; }
    public bool AreaProfileActive { get; init; }
    public bool AskingGame { get; init; }
    public bool CrashPending { get; init; }
    public bool GpuFallback { get; init; }
    public bool MainAccountTip { get; init; }
    public bool RemotePlayPopup { get; init; }
    public bool AutoScreenTextNotice { get; init; }
    /// <summary>Çevirgen: seçilen pencere bulunamıyor (çeviri sürerken).</summary>
    public bool WindowMissing { get; init; }
    /// <summary>Çevirgen: görüntü bir pencereden okunuyor (Bilgisayar, Diğer). Oyun arka plana geçince (tam ekran oyun küçülür)
    /// görüntünün durması beklenen hâldir; "görüntü gelmiyor" uyarısı çıkmaz (eskiden "PS5 görüntüsü gelmiyor" diyordu).</summary>
    public bool ReadsWindow { get; init; }
}

/// <summary>Canlı ekranın bildirimleri: hangileri görünür, önemleri ve sıraları (arayüzden bağımsız; sınanır).</summary>
public static class LiveNotices
{
    /// <summary>
    /// Bildirimin önemi. Model eksikliği yalnız ücretsiz çeviri seçiliyken çeviriyi durdurur; başka motorda yedek olduğu için
    /// "Yapılacak"tır.
    /// </summary>
    public static LiveNoticeLevel Level(LiveNoticeKind kind, bool freeEngine = true) => kind switch
    {
        LiveNoticeKind.ModelMissing => freeEngine ? LiveNoticeLevel.Blocking : LiveNoticeLevel.Action,
        <= LiveNoticeKind.PictureStopped => LiveNoticeLevel.Blocking,
        <= LiveNoticeKind.AreaProfileOffer => LiveNoticeLevel.Action,
        _ => LiveNoticeLevel.Info,
    };

    /// <summary>Önem sırası: önce hatalar, sonra yapılacaklar, sonra bilgiler; aynı önemde <see cref="LiveNoticeKind"/> sırası.</summary>
    public static IReadOnlyList<LiveNoticeKind> Order(IEnumerable<LiveNoticeKind> kinds, bool freeEngine = true) =>
        [.. kinds.Distinct().OrderBy(kind => Level(kind, freeEngine)).ThenBy(kind => (int)kind)];

    /// <summary>Görünen bildirimler, en önemlisi başta. Şeritte ilki gösterilir.</summary>
    public static IReadOnlyList<LiveNoticeKind> For(LiveNoticeState state)
    {
        var running = state.Running && !state.DemoMode;
        var kinds = new List<LiveNoticeKind>();
        if (state.EngineFailed) kinds.Add(LiveNoticeKind.EngineFailed);
        if (state.ModelMissing) kinds.Add(LiveNoticeKind.ModelMissing);
        if (state.OcrMissing) kinds.Add(LiveNoticeKind.OcrMissing);
        if (state.ConsoleNeedsLink || state.ConsoleOffersReconnect) kinds.Add(LiveNoticeKind.Console);
        // Sessizlik nedenleri yalnız çeviri sürerken (HomeProblem zaten öyle hesaplar).
        // Pencere açık değilse görüntünün gelmemesi onun sonucudur; yalnız pencere bildirimi çıkar.
        if (running && state.WindowMissing) kinds.Add(LiveNoticeKind.WindowMissing);
        if (running)
            switch (state.Problem)
            {
                case HomeProblemKind.PictureStopped when !state.WindowMissing && !state.ReadsWindow: kinds.Add(LiveNoticeKind.PictureStopped); break;
                case HomeProblemKind.SubtitlesOff: kinds.Add(LiveNoticeKind.SubtitlesOff); break;
                case HomeProblemKind.MenuOpen: kinds.Add(LiveNoticeKind.MenuOpen); break;
            }
        if (state.TvNeedsWork) kinds.Add(LiveNoticeKind.Tv);
        if (state.Firewall != FirewallPrompt.None) kinds.Add(LiveNoticeKind.Firewall);
        if (running && state.OnlineIssue) kinds.Add(LiveNoticeKind.OnlineEngine);
        // Oyun sorulurken alan önerisi beklemede kalır (seçim kartı açık).
        if (state.Running && !state.AskingGame)
        {
            if (state.AreaProfileOffered) kinds.Add(LiveNoticeKind.AreaProfileOffer);
            else if (state.AreaProfileActive) kinds.Add(LiveNoticeKind.AreaProfileActive);
        }
        if (state.CrashPending) kinds.Add(LiveNoticeKind.Crash);
        if (state.GpuFallback) kinds.Add(LiveNoticeKind.GpuFallback);
        // Bildirim yalnız okunduysa (ve bu oturumda kapatılmadıysa) görünür; deneme karelerinde okunmaz.
        if (state.RemotePlayPopup && !state.DemoMode) kinds.Add(LiveNoticeKind.RemotePlayPopup);
        if (state.MainAccountTip) kinds.Add(LiveNoticeKind.MainAccountTip);
        if (state.AutoScreenTextNotice) kinds.Add(LiveNoticeKind.AutoScreenText);
        if (!state.CanStart && !state.Running && !state.DemoMode) kinds.Add(LiveNoticeKind.Demo);
        return Order(kinds, state.FreeEngine);
    }

    /// <summary>Şeritteki kısa etiket: "Hata", "Yapılacak", "Bilgi".</summary>
    public static string Label(LiveNoticeLevel level) => level switch
    {
        LiveNoticeLevel.Blocking => "Hata",
        LiveNoticeLevel.Action => "Yapılacak",
        _ => "Bilgi",
    };

    /// <summary>Önemin durum rengi (tek renk modeli, <see cref="DeviceStates"/>): hata kırmızı, yapılacak sarı-turuncu, bilgi gri.</summary>
    public static DeviceState State(LiveNoticeLevel level) => level switch
    {
        LiveNoticeLevel.Blocking => DeviceState.Error,
        LiveNoticeLevel.Action => DeviceState.Waiting,
        _ => DeviceState.Off,
    };

    /// <summary>"+2 uyarı daha" (gösterilenin dışında kalan bildirim sayısı); kalan yoksa null.</summary>
    public static string? MoreText(int count) => count <= 1 ? null : $"+{count - 1} uyarı daha";
}
