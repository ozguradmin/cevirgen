// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen eklentileri: kullanıcı düzeltmeleri ("Düzelt"), kullanıcı sözlüğü, oturum özeti (okuma hızı, gecikme),
/// SRT dışa aktarma ve altyazıları gizle/göster. Arayüzden bağımsızdır: yeni görünümler bu yöntemleri çağırır,
/// değişiklikler <see cref="AppController.PropertyChanged"/> ile bildirilir. Hepsi arayüz iş parçacığında çağrılmalıdır.
/// </summary>
public sealed partial class AppController
{
    private readonly UserCorrections corrections;
    private readonly UserGlossary userGlossary;
    /// <summary>Son çeviri oturumunun başladığı an (UTC); bu çalışmada hiç başlamadıysa null.</summary>
    private DateTime? sessionStartedAt;
    /// <summary>Bu oturumda TV'ye giden satırların gecikmeleri (ms).</summary>
    private readonly List<int> sessionLatencies = [];
    private bool subtitlesHidden;

    private static (UserCorrections, UserGlossary) LoadUserData(string? folder)
    {
        var corrections = UserCorrections.Load(folder is null ? null : Path.Combine(folder, UserCorrections.FileName));
        var glossary = UserGlossary.Load(folder is null ? null : Path.Combine(folder, UserGlossary.FileName));
        // Ücretsiz motor ve DeepL terimleri genel sözlük üstünden korur; kullanıcı sözlüğü onun üstüne eklenir.
        GlossaryStore.UserTerms = glossary.AsGlossary;
        return (corrections, glossary);
    }

    // --- Düzeltmeler ("Düzelt") ---

    /// <summary>Kullanıcının düzeltmeleri, en son değişen başta. Değişince bu özellik bildirilir.</summary>
    public IReadOnlyList<UserCorrections.Correction> Corrections => corrections.All;

    /// <summary>
    /// Satırın Türkçesini kullanıcının çevirisiyle değiştirir (yoksa ekler). Satır yeniden okununca paket, önbellek ve
    /// çeviri motoru yerine bu gösterilir (günlükte "senin"). <paramref name="english"/> geçmişteki kaynak satır olabilir;
    /// baştaki konuşan adı atılır. Geçersizse (İngilizce boş ya da Türkçe boş) false.
    /// </summary>
    public bool SaveCorrection(string english, string turkish)
    {
        if (!corrections.Set(english, turkish)) return false;
        SubtitleLog.Write($"düzeltme kaydedildi: {SubtitleLog.Excerpt(english)} → {SubtitleLog.Excerpt(turkish)}");
        Changed(nameof(Corrections));
        return true;
    }

    /// <summary>Satırın düzeltmesini siler; yoksa false.</summary>
    public bool RemoveCorrection(string english)
    {
        if (!corrections.Remove(english)) return false;
        SubtitleLog.Write($"düzeltme silindi: {SubtitleLog.Excerpt(english)}");
        Changed(nameof(Corrections));
        return true;
    }

    /// <summary>Bu İngilizce satırın kayıtlı düzeltmesi (adlı satırda adsız cümleninki); yoksa null.</summary>
    public UserCorrections.Correction? CorrectionFor(string english) => corrections.Get(english);

    // --- Kullanıcı sözlüğü ---

    /// <summary>Kullanıcı sözlüğünün terimleri, alfabe sırasıyla. Değişince bu özellik bildirilir.</summary>
    public IReadOnlyList<UserGlossary.Term> GlossaryTerms => userGlossary.Terms;

    /// <summary>
    /// Terimi ekler ya da günceller: <see cref="GlossaryRule.Keep"/> ("çevirme") terim İngilizce kalır;
    /// <see cref="GlossaryRule.Always"/> ("hep böyle") her zaman <paramref name="turkish"/> olur (gerekir). Yapay zekâ
    /// motorunda çeviri sürüyorsa hat yeni talimatla yeniden kurulur; ücretsiz motor ve DeepL bir sonraki satırda uygular.
    /// Geçersizse false.
    /// </summary>
    public bool SaveGlossaryTerm(string english, GlossaryRule rule, string? turkish = null)
    {
        if (!userGlossary.Set(english, rule, turkish)) return false;
        SubtitleLog.Write($"sözlük: {SubtitleLog.Excerpt(english)} → {UserGlossary.Label(rule)}{(rule == GlossaryRule.Always ? " " + SubtitleLog.Excerpt(turkish ?? "") : "")}");
        GlossaryChanged();
        return true;
    }

    /// <summary>Terimi siler; yoksa false.</summary>
    public bool RemoveGlossaryTerm(string english)
    {
        if (!userGlossary.Remove(english)) return false;
        SubtitleLog.Write($"sözlükten silindi: {SubtitleLog.Excerpt(english)}");
        GlossaryChanged();
        return true;
    }

    /// <summary>Yapay zekâ motorlarına giden ek talimat: Ayarlardaki ek talimat ve kullanıcı sözlüğünün kuralları.</summary>
    internal string? AiInstructionWithGlossary => userGlossary.CombinedInstruction(settings.AiInstruction);

    private void GlossaryChanged()
    {
        Changed(nameof(GlossaryTerms));
        // Yapay zekâ talimatı çevirmen kurulurken verilir; çeviri sürüyorsa çevirmen yenilenir.
        if (isRunning && pipeline is not null && OnlineTranslationActive && settings.OnlineEngine != TranslationEngine.DeepL) MakePipeline();
    }

    // --- Oturum özeti ve okuma hızı ---

    /// <summary>Son çeviri oturumunun başladığı an (UTC); bu çalışmada çeviri hiç başlamadıysa null.</summary>
    public DateTime? SessionStartedAt => sessionStartedAt;

    /// <summary>Son oturumun satırları (en yenisi başta); oturum yoksa bütün geçmiş.</summary>
    public IReadOnlyList<SubtitleHistory.Entry> SessionHistory =>
        sessionStartedAt is { } start ? history.EntriesSince(start) : [.. history.Entries];

    /// <summary>
    /// Son oturumun özeti: satır sayısı, düzeltmeden gelen satırlar, ortalama okuma hızı (CPS), 17'nin üstündeki "hızlı"
    /// satırlar ve ortalama gecikme. Geçmiş değişince bu özellik de bildirilir.
    /// </summary>
    public SessionStats SessionStats => SessionStats.From(SessionHistory, sessionLatencies);

    private void BeginSessionStats()
    {
        // Geçmiş kayıtları gerçek saatle yazılır (SubtitleHistory.Add); oturumun başı da öyle.
        sessionStartedAt = DateTime.UtcNow;
        sessionLatencies.Clear();
        Changed(nameof(SessionStartedAt), nameof(SessionStats));
    }

    private void NoteLatency(int totalMs)
    {
        sessionLatencies.Add(totalMs);
        Changed(nameof(SessionStats));
    }

    /// <summary>Satır ekrandan kalktı: geçmişteki kaydına kalkış anı ve okuma hızı yazılır.</summary>
    private void MarkLeftInHistory(string source, int visibleMs)
    {
        if (!history.MarkLeft(source, visibleMs)) return;
        Changed(nameof(History), nameof(SessionStats));
        SaveHistory();
    }

    // --- SRT ---

    /// <summary>
    /// Son oturumu (oturum yoksa bütün geçmişi) SRT dosyası olarak <paramref name="path"/>'e yazar: yalnız Türkçe ya da
    /// iki dilli (her ipucunda İngilizce, sonra Türkçe). Zamanlar satırların gerçek ekrana geliş/kalkış anlarıdır,
    /// oturumun başına göre. Yazılan ipucu sayısını döner; dosya yazılamazsa IOException/UnauthorizedAccessException atar.
    /// </summary>
    /// <param name="session">Geçmiş ekranında seçilen oturum (zamanlar onun başına göre); verilmezse son oturum.</param>
    public int ExportSrt(string path, SrtExport.Kind kind = SrtExport.Kind.Turkish, HistorySession? session = null)
    {
        var count = session is { } chosen ? SrtExport.Write(path, chosen.Entries, kind, chosen.Origin)
            : SrtExport.Write(path, SessionHistory, kind, sessionStartedAt);
        SubtitleLog.Write($"SRT yazıldı ({(kind == SrtExport.Kind.Bilingual ? "iki dilli" : "Türkçe")}): {count} satır");
        return count;
    }

    // --- Altyazıları gizle/göster ---

    /// <summary>
    /// Altyazılar gizli mi (Ctrl+H; çeviri sürer). TV'ye <c>visibility</c> olayı gider; bilgisayardaki altyazı penceresi
    /// de gizlenir. Gizliyken gelen satırlar da görünmez; gösterilince o an ekranda olan satır görünür.
    /// </summary>
    public bool SubtitlesHidden => subtitlesHidden;

    public void ToggleSubtitlesHidden() => SetSubtitlesHidden(!subtitlesHidden);

    public void SetSubtitlesHidden(bool hidden)
    {
        if (subtitlesHidden == hidden) return;
        subtitlesHidden = hidden;
        server.SubtitlesHidden = hidden;
        SubtitleLog.Write(hidden ? "altyazılar gizlendi (çeviri sürüyor)" : "altyazılar yeniden gösteriliyor");
        Changed(nameof(SubtitlesHidden));
    }
}
