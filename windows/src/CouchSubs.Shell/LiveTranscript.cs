// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>Döküm satırının sağındaki etiketin anlamı (renk seçimi için).</summary>
public enum CueLabelKind
{
    /// <summary>Kaynak: motor adı, "paket", "sözlük" (gri).</summary>
    Source,
    /// <summary>Kullanıcının düzeltmesi ("senin"; açık renk).</summary>
    Yours,
    /// <summary>Okuma hızı sınırın üstünde ("21 kr/sn · hızlı"; uyarı rengi).</summary>
    Fast,
    /// <summary>Satır şu an TV'de ("● ekranda"; sarı).</summary>
    OnScreen,
}

/// <summary>
/// Canlı ekrandaki dökümün bir satırı (taslak: Main.dc.html › Döküm). <paramref name="Current"/>: TV'deki en alttaki
/// (güncel) satır; Türkçesi sarı ve yarı kalın.
/// </summary>
public sealed record LiveCue(int Id, string Timecode, string English, string Turkish, string Label, CueLabelKind Kind, bool OnScreen, bool Current);

/// <summary>
/// Çevirgen: Canlı ekranın dökümü. Geçmiş kayıtlarından (en yenisi başta) ekranda gösterilecek satırları kurar: oturumun
/// başına göre zaman kodu (00:42:15,880), İngilizce, Türkçe ve tek etiket. Etiketin önceliği: şu an ekranda → hızlı (okuma
/// hızı 17'nin üstünde) → kaynak (senin / paket / sözlük / motor adı). Arayüzden bağımsızdır; test edilir.
/// </summary>
public static class LiveTranscript
{
    /// <summary>Dökümde en çok bu kadar satır (daha eskisi Geçmiş'te).</summary>
    public const int MaxCues = 60;

    /// <summary>"00:42:15,880" (SRT biçimi); eksi süre sıfır sayılır.</summary>
    public static string Timecode(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
        var hours = (int)offset.TotalHours;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{offset.Minutes:00}:{offset.Seconds:00},{offset.Milliseconds:000}");
    }

    /// <summary>
    /// Döküm satırları. <paramref name="sessionStart"/>: oturumun başı (UTC; zaman kodu buna göre; yoksa yerel saat).
    /// <paramref name="onScreen"/>: TV'de şu an görünen satırlar, yukarıdan aşağı (sonuncusu güncel satır).
    /// <paramref name="engineName"/>: motorun kısa adı ("Gemini", "Ücretsiz" …). <paramref name="glossaryTerms"/>: kullanıcı
    /// sözlüğünün İngilizce terimleri (satırda geçen terim varsa kaynak "sözlük").
    /// </summary>
    public static IReadOnlyList<LiveCue> Build(IReadOnlyList<SubtitleHistory.Entry> entries, DateTime? sessionStart,
        IReadOnlyList<string> onScreen, string engineName, IReadOnlyCollection<string>? glossaryTerms = null, int max = MaxCues,
        TimeZoneInfo? timeZone = null)
    {
        var visible = onScreen.Select(Plain).Where(line => line.Length > 0).ToList();
        var current = visible.Count > 0 ? visible[^1] : null;
        var marked = new HashSet<string>(StringComparer.Ordinal);
        var cues = new List<LiveCue>();
        foreach (var entry in entries.Take(Math.Max(0, max)))
        {
            var turkish = Plain(entry.Translation);
            // Aynı Türkçe iki kez geçtiyse yalnız en yenisi "ekranda" sayılır.
            var isOnScreen = turkish.Length > 0 && visible.Contains(turkish) && marked.Add(turkish);
            var isCurrent = isOnScreen && turkish == current;
            var (label, kind) = isOnScreen ? ("● ekranda", CueLabelKind.OnScreen)
                : entry.IsFast ? (string.Create(CultureInfo.InvariantCulture, $"{Math.Round(entry.Cps!.Value):0} kr/sn · hızlı"), CueLabelKind.Fast)
                : SourceLabel(entry, engineName, glossaryTerms);
            cues.Add(new LiveCue(entry.Id, Stamp(entry.Shown, sessionStart, timeZone), entry.Source, entry.Translation, label, kind, isOnScreen, isCurrent));
        }
        return cues;
    }

    /// <summary>Kaynak etiketi: düzeltme "senin", paket "paket", sözlük terimi geçen satır "sözlük", yoksa motorun adı.</summary>
    public static (string Label, CueLabelKind Kind) SourceLabel(SubtitleHistory.Entry entry, string engineName, IReadOnlyCollection<string>? glossaryTerms = null) =>
        entry.Origin switch
        {
            LineOrigin.Correction => ("senin", CueLabelKind.Yours),
            LineOrigin.Pack => ("paket", CueLabelKind.Source),
            _ when glossaryTerms is { Count: > 0 } && glossaryTerms.Any(term => ContainsTerm(entry.Source, term)) => ("sözlük", CueLabelKind.Source),
            _ => (engineName, CueLabelKind.Source),
        };

    /// <summary>Motorun dökümdeki kısa adı.</summary>
    public static string EngineLabel(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Gemini => "Gemini",
        TranslationEngine.DeepL => "DeepL",
        TranslationEngine.OpenAi => "OpenAI",
        TranslationEngine.Claude => "Claude",
        TranslationEngine.Local => "yerel model",
        _ => "ücretsiz",
    };

    /// <summary>Terim satırda bütün sözcük olarak geçiyor mu (büyük-küçük harf duyarsız).</summary>
    public static bool ContainsTerm(string line, string term)
    {
        term = term.Trim();
        if (term.Length == 0) return false;
        var start = 0;
        while ((start = line.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = start == 0 || !char.IsLetterOrDigit(line[start - 1]);
            var end = start + term.Length;
            var after = end >= line.Length || !char.IsLetterOrDigit(line[end]);
            if (before && after) return true;
            start = end;
        }
        return false;
    }

    private static string Stamp(DateTime shown, DateTime? sessionStart, TimeZoneInfo? timeZone)
    {
        if (sessionStart is { } start && shown >= start) return Timecode(shown - start);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(shown, DateTimeKind.Utc), timeZone ?? TimeZoneInfo.Local);
        return local.ToString("HH:mm:ss,fff", CultureInfo.InvariantCulture);
    }

    private static string Plain(string text) => text.Trim();
}
