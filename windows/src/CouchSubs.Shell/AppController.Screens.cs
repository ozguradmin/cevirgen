// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen arayüzünün (2. bölüm) denetleyici işleri: Çeviri ekranının kör testi ve bağlantı denemesi, Alanları düzenle
/// ekranının kaydı. Arayüz iş parçacığında çağrılır.
/// </summary>
public sealed partial class AppController
{
    // --- Çeviri › Kör test ---

    /// <summary>Kör testin sütunları (karıştırılmamış): ücretsiz model ve kullanılabilen motorlar, üçe tamamlanmış.</summary>
    public IReadOnlyList<BlindTestColumn> BlindTestColumns() => BlindTest.Columns(settings.Engine, IsReady);

    /// <summary>
    /// Örnek satırları bu motorla çevirir (kör test): ücretsiz model ya da kayıtlı anahtarla bulut motoru / yerel sunucu.
    /// Yedek çevirmen verilmez (yoksa çeviremeyen motorun satırları ücretsiz modelden gelirdi). Motor hazır değilse ya da
    /// hiç çeviremezse sonuçta hata yazar. Satırlar sırayla çevrilir; süre satır başına ortalamadır.
    /// </summary>
    public async Task<BlindTestResult> TranslateSamplesAsync(TranslationEngine engine, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        ITextTranslator? translator;
        try
        {
            translator = engine == TranslationEngine.Free ? await Task.Run(MakeSampleFreeTranslator, cancellationToken) : MakeSampleOnlineTranslator(engine);
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            return new BlindTestResult([.. lines.Select(_ => (string?)null)], null, failed.Message);
        }
        if (translator is null)
            return new BlindTestResult([.. lines.Select(_ => (string?)null)], null,
                engine == TranslationEngine.Free ? "Ücretsiz model henüz inmedi." : "Bu motorun anahtarı yok.");
        var results = new List<string?>();
        var times = new List<long>();
        string? error = null;
        try
        {
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var watch = Stopwatch.StartNew();
                try
                {
                    var text = (await translator.TranslateAsync(line, cancellationToken)).Trim();
                    results.Add(text.Length == 0 ? null : text);
                    times.Add(watch.ElapsedMilliseconds);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    results.Add(null);
                    error ??= failure.Message;
                }
            }
        }
        finally
        {
            (translator as IDisposable)?.Dispose();
        }
        var average = times.Count == 0 ? (int?)null : (int)Math.Round(times.Average(), MidpointRounding.AwayFromZero);
        SubtitleLog.Write($"kör test: {engine} {results.Count(text => text is not null)}/{lines.Count} satır{(average is { } ms ? $", ort. {ms} ms" : "")}");
        return new BlindTestResult(results, average, results.All(text => text is null) ? error ?? "Çeviri gelmedi." : null);
    }

    private ITextTranslator? MakeSampleFreeTranslator()
    {
        var translation = services.Translation;
        if (!translation.IsSupported("en", "tr") || !translation.IsInstalled("en", "tr") || translation.EngineFailed) return null;
        return translation.Create("en", "tr");
    }

    private ITextTranslator? MakeSampleOnlineTranslator(TranslationEngine engine)
    {
        if (engine.StoreKey() is not { } storeKey) return null;
        var key = store.Load<string>(storeKey) ?? "";
        if (engine == TranslationEngine.Local)
            return settings.LocalAiServer is { } server ? services.MakeLocalAiTranslator(server, key, null, null, AiInstructionWithGlossary) : null;
        if (key.Length == 0) return null;
        return engine.Provider() is { } provider
            ? services.MakeAiTranslator(provider, key, provider.Model(settings.ModelFor(provider)).Id, null, null, AiInstructionWithGlossary,
                settings.GeminiPaidTier && provider == AiProvider.Gemini)
            : services.MakeOnlineTranslator(key, null);
    }

    /// <summary>Kör testin kazananı ya da listeden seçilen motor kullanılır (anahtarı yoksa yine seçilir; anahtar gelince çalışır).</summary>
    public void UseEngine(TranslationEngine engine)
    {
        if (settings.Engine == engine) return;
        Settings = settings.WithEngine(engine);
        SubtitleLog.Write($"çeviri motoru seçildi: {engine}");
    }

    // --- Alanları düzenle ---

    /// <summary>
    /// Alanları düzenle ekranının başlığındaki oyun: etkin alan profilinin oyunu, yoksa oynanan oyun; ikisi de yoksa null
    /// (alanlar genel ayar olarak kaydedilir).
    /// </summary>
    public string? AreaEditGame => AreaProfileGame ?? PlayedGame;

    /// <summary>
    /// "Bitti": düzenlenen alanlar okunan alanlar olur. Etkin profil varsa o profile, yoksa oynanan oyun biliniyorsa o oyunun
    /// yeni profiline yazılır (o oyunun başka kayıtlı profili varsa onun üzerine yazılmaz; alanlar genel kalır). Oyun
    /// bilinmiyorsa alanlar genel ayardır. Kayıt günlüğe yazılır.
    /// </summary>
    public void ApplyEditedAreas(AreaSet areas)
    {
        var next = settings.WithAreas(areas);
        var game = AreaEditGame;
        if (AreaProfileGame is { } active) next = next.WithSavedAreaProfile(active);
        else if (game is not null && AreaProfiles.Key(game, next.AreaSource) is { } key && next.FindAreaProfile(key) is null)
            next = next.WithSavedAreaProfile(game);
        Settings = next;
        SubtitleLog.Write($"alanlar düzenlendi: {areas.ExtraAreas.Count} ek alan{(next.ActiveAreaProfile is { } profile ? " · " + profile : "")}");
    }
}
