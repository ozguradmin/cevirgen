// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: ortak havuza giden satırlar ("Çevirileri iyileştirmeye katkı", <see cref="LinePool"/>) ve PS5'in Uzaktan
/// Oynatma bildirimi ipucu (<see cref="RemotePlayHint"/>).
/// </summary>
public sealed partial class AppController
{
    private readonly RemotePlayHint remotePlayHint = new();

    /// <summary>Havuza giden oyun: sunucudaki oyun listesinde adı geçiyorsa onun kimliği (<c>gta-6</c>) ve adı; bilinmiyorsa null.</summary>
    private LinePool.GameRef? PoolGame(RunningGame? game)
    {
        if (game?.Name is not { } name) return null;
        var known = remoteContent.Games.FirstOrDefault(item => item.Game.Matches(name))?.Game;
        return LinePool.GameRef.Create(known?.Id, name);
    }

    /// <summary>
    /// Ekranda gösterilen satır ve çevirisi havuza önerilir: yalnız İngilizce→Türkçe, deneme modunda değil ve satır hiçbir
    /// pakette yoksa (paket satırları zaten çevrilmiş). <paramref name="engine"/> null ise o an çeviren motor
    /// (<see cref="PoolEngineLabel"/>). Okuma iş parçacığından çağrılabilir.
    /// </summary>
    private void OfferLine(string english, string translation, string? engine, LinePool.Area area)
    {
        if (demoMode || !settings.ShareLines || settings.SourceLanguage != "en" || settings.TargetLanguage != "tr" || services.Pool is not { } pool) return;
        if (engine != LinePool.UserEngine && services.FindInPacks(english) is not null) return;
        try { pool.Offer(english, translation, engine ?? PoolEngineLabel(), area); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SubtitleLog.Write($"ortak havuz: satır eklenemedi: {error.GetType().Name}");
        }
    }

    /// <summary>Ek alanın ya da ekran yazısının yeni çevirisi: kullanıcının ek alanları ve hazır alanlar "extra", kendiliğinden bulunanlar "screen".</summary>
    private void OnRegionTranslated(WatchedArea area, string english, string translation)
    {
        lastAreaTranslations[area.Id] = (english, translation);
        OfferLine(english, translation, null, area.Auto ? LinePool.Area.Screen : LinePool.Area.Extra);
    }

    /// <summary>
    /// O an çeviren motorun havuzdaki adı: "free" (ücretsiz yerel model), "deepl", "gemini:&lt;model&gt;", "openai:&lt;model&gt;",
    /// "claude:&lt;model&gt;", "local:&lt;model&gt;", "custom:&lt;model&gt;". Çevrimiçi motor bir satırı çeviremeyip ücretsiz modele
    /// düştüyse de seçili motorun adı gider (hangi satırın düştüğü bilinmiyor).
    /// </summary>
    public string PoolEngineLabel()
    {
        if (!OnlineTranslationActive) return "free";
        var engine = settings.OnlineEngine;
        return engine switch
        {
            TranslationEngine.DeepL => "deepl",
            TranslationEngine.Local => "local:" + settings.LocalAiModel,
            TranslationEngine.Custom => "custom:" + settings.CustomAiModel,
            _ when engine.Provider() is { } provider => engine.ToString().ToLowerInvariant() + ":" + provider.Model(settings.ModelFor(provider)).Id,
            _ => engine.ToString().ToLowerInvariant(),
        };
    }

    // --- PS5 Uzaktan Oynatma bildirimi ---

    /// <summary>
    /// Canlı ekranda "PS5'in Uzaktan Oynatma bildirimini gizle" ipucu görünür mü: bu çeviri oturumunda karede bildirim
    /// okundu ve kullanıcı ipucunu kapatmadı.
    /// </summary>
    public bool ShowsRemotePlayHint => remotePlayHint.Visible;

    /// <summary>İpucunu bu oturum için kapatır; sonraki oturumda bildirim yine okunursa yeniden görünür.</summary>
    public void DismissRemotePlayHint()
    {
        if (remotePlayHint.Dismiss()) Changed(nameof(ShowsRemotePlayHint));
    }

    /// <summary>Okuma iş parçacığından: karede PS5'in Uzaktan Oynatma bildirimi okundu.</summary>
    private void OnRemotePlayNoticeSeen()
    {
        if (demoMode) return;
        post(() =>
        {
            if (!remotePlayHint.Seen()) return;
            SubtitleLog.Write("PS5'in Uzaktan Oynatma bildirimi okundu; gizleme ipucu gösteriliyor");
            Changed(nameof(ShowsRemotePlayHint));
        });
    }

    /// <summary>Çeviri oturumu başladı: ipucu sıfırlanır (yalnız bu oturumda bildirim okunursa görünür).</summary>
    private void ResetRemotePlayHint()
    {
        if (remotePlayHint.NewSession()) Changed(nameof(ShowsRemotePlayHint));
    }
}

/// <summary>
/// PS5'in Uzaktan Oynatma bildirimi ipucunun kuralı (arayüzden bağımsız; sınanır): hiç okunmadıysa görünmez; okununca görünür
/// (her oturumda yeniden, tek seferlik değil); kapatılınca yalnız o oturum için gizlenir.
/// </summary>
public sealed class RemotePlayHint
{
    /// <summary>Canlı ekrandaki bildirimin başlığı.</summary>
    public const string Headline = "PS5'in “Uzaktan Oynatma'ya bağlandı” penceresi ekranda kalıyor; altyazıyı kapatabilir.";
    /// <summary>PS5'teki ayarın tam yolu (Türkçe menü).</summary>
    public const string MenuPath = "Ayarlar › Sistem › Uzaktan Oynatma › “Uzaktan Oynatma Bağlantı Durumu Açılır Penceresini Gizle”";
    /// <summary>İngilizce PS5 menüsündeki adı.</summary>
    public const string MenuPathEnglish = "Settings › System › Remote Play › “Hide Remote Play Connection Status Pop-Up”";
    /// <summary>Ne yapılacağı (bildirimin ayrıntısı, kurulum ve Ayarlar › Cihazlar aynı metni kullanır).</summary>
    public const string Instruction = "Gizlemek için PS5'te " + MenuPath + " ayarını aç (İngilizce menüde: " + MenuPathEnglish + ").";
    /// <summary>İki hesap notu.</summary>
    public const string Accounts = "Bunu hem kendi ana hesabında hem Çevirgen'in bağlandığı ikinci hesapta yap: ayar her hesapta ayrıdır.";

    private bool seen;
    private bool dismissed;

    public bool Visible => seen && !dismissed;

    /// <summary>Bildirim okundu; görünürlük değiştiyse true.</summary>
    public bool Seen()
    {
        var before = Visible;
        seen = true;
        return Visible != before;
    }

    /// <summary>Kullanıcı kapattı (bu oturum için); görünürlük değiştiyse true.</summary>
    public bool Dismiss()
    {
        var before = Visible;
        dismissed = true;
        return Visible != before;
    }

    /// <summary>Yeni çeviri oturumu; görünürlük değiştiyse true.</summary>
    public bool NewSession()
    {
        var before = Visible;
        seen = false;
        dismissed = false;
        return Visible != before;
    }
}
