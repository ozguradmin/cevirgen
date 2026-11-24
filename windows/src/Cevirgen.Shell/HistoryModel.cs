// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>Geçmişteki bir oyun oturumu: satırları (en yenisi başta), başlangıcı ve bitişi (UTC).</summary>
/// <param name="Origin">Zaman kodlarının sıfırı: oturumun başladığı an (bilinmiyorsa ilk satırın ekrana geldiği an).</param>
public sealed record HistorySession(DateTime Origin, IReadOnlyList<SubtitleHistory.Entry> Entries)
{
    /// <summary>İlk satırın ekrana geldiği an.</summary>
    public DateTime First => Entries.Count == 0 ? Origin : Entries.Min(entry => entry.Shown);

    /// <summary>Son satırın ekrandan kalktığı (bilinmiyorsa geldiği) an.</summary>
    public DateTime Last => Entries.Count == 0 ? Origin : Entries.Max(entry => entry.LeftAt ?? entry.Shown);

    public TimeSpan Duration => Last > Origin ? Last - Origin : TimeSpan.Zero;

    public int Corrections => Entries.Count(entry => entry.IsCorrection);
}

/// <summary>Geçmiş ekranındaki bir ipucu (altyazı dosyasındaki gibi): sıra numarası, zaman aralığı, iki dil, okuma hızı.</summary>
/// <param name="Number">Oturumdaki sırası (ekrana geliş sırasıyla, 1'den).</param>
/// <param name="Range">"00:42:13,154 → 00:42:16,020".</param>
/// <param name="Source">Kaynak etiketi: "paket", "senin" ya da motorun adı.</param>
public sealed record HistoryCue(int Number, SubtitleHistory.Entry Entry, string Range, long StartMs, long EndMs, string Source)
{
    public double? Cps => Entry.Cps;
    public bool Fast => Entry.IsFast;
    /// <summary>Satırın ekranda kaldığı süre (ms).</summary>
    public long DurationMs => Math.Max(0, EndMs - StartMs);
}

/// <summary>
/// Çevirgen: Geçmiş ve sözlük ekranının mantığı (taslak: History.dc.html). Geçmiş oturumlara bölünür (iki satır arası
/// <see cref="SessionGap"/>'ten uzunsa ya da çeviri yeniden başlatıldıysa yeni oturum); her oturumun satırları SRT'deki
/// gibi numaralanır ve zamanlanır (<see cref="SrtExport"/> ile aynı kurallar: kalkışı bilinmeyen satır okunacak kadar
/// kalır, hiçbir satır sonrakinin başlangıcını geçmez). Arama metinde ve zaman kodunda yapılır. Düzeltme kutusu yazarken
/// karakter sayısını ve okuma hızını gösterir. Arayüzden bağımsızdır; test edilir.
/// </summary>
public static class HistoryModel
{
    /// <summary>İki satır arasında bundan uzun boşluk varsa ayrı oturum sayılır.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Geçmişi oturumlara böler, en yeni oturum başta. <paramref name="currentStart"/>: bu çalışmadaki son çeviri oturumunun
    /// başladığı an; ondan sonraki satırlar tek oturumdur ve zaman kodları o andan sayılır.
    /// </summary>
    public static IReadOnlyList<HistorySession> Sessions(IReadOnlyList<SubtitleHistory.Entry> entries, DateTime? currentStart = null)
    {
        var ordered = entries.OrderBy(entry => entry.Shown).ThenBy(entry => entry.Id).ToList();
        var sessions = new List<HistorySession>();
        var group = new List<SubtitleHistory.Entry>();
        DateTime? origin = null;
        var start = currentStart?.ToUniversalTime();
        void Close()
        {
            if (group.Count == 0) return;
            sessions.Add(new HistorySession(origin ?? group[0].Shown, [.. group.AsEnumerable().Reverse()]));
            group.Clear();
            origin = null;
        }
        foreach (var entry in ordered)
        {
            var current = start is { } begin && entry.Date >= begin;
            var previousCurrent = group.Count > 0 && start is { } b && group[^1].Date >= b;
            if (group.Count > 0 && (current != previousCurrent || (!current && entry.Shown - (group[^1].LeftAt ?? group[^1].Shown) > SessionGap)))
                Close();
            if (group.Count == 0) origin = current ? start : null;
            group.Add(entry);
        }
        Close();
        sessions.Reverse();
        return sessions;
    }

    /// <summary>Oturumun ipuçları, en yenisi başta (numaralar ekrana geliş sırasıyla).</summary>
    public static IReadOnlyList<HistoryCue> Cues(HistorySession session, string engineLabel)
    {
        var list = session.Entries.Where(entry => entry.Translation.Trim().Length > 0)
            .OrderBy(entry => entry.Shown).ThenBy(entry => entry.Id).ToList();
        var origin = session.Origin.ToUniversalTime();
        long Offset(DateTime at) => (long)Math.Round((at.ToUniversalTime() - origin).TotalMilliseconds);
        var cues = new List<HistoryCue>();
        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            var from = Math.Max(0, Offset(entry.Shown));
            var to = entry.LeftAt is { } left ? Offset(left) : from + SrtExport.FallbackMs(entry.Translation);
            if (i + 1 < list.Count) to = Math.Min(to, Math.Max(0, Offset(list[i + 1].Shown)));
            to = Math.Max(to, from + SrtExport.MinCueMs);
            var source = entry.Origin switch
            {
                LineOrigin.Pack => "paket",
                LineOrigin.Correction => "senin",
                _ => engineLabel,
            };
            cues.Add(new HistoryCue(i + 1, entry, $"{SrtExport.Time(from)} → {SrtExport.Time(to)}", from, to, source));
        }
        cues.Reverse();
        return cues;
    }

    /// <summary>
    /// Arama: Türkçe ve İngilizce metinde (Türkçe büyük/küçük harf duyarsız) ya da zaman kodunda ("42:13", "00:42:1").
    /// Boş arama her satırı tutar.
    /// </summary>
    public static bool Matches(HistoryCue cue, string query, string? correction = null)
    {
        query = query.Trim();
        if (query.Length == 0) return true;
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var text = $"{cue.Entry.Translation}\n{cue.Entry.Source}\n{correction}";
        if (culture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase) >= 0) return true;
        return query.Any(char.IsDigit) && query.All(c => char.IsDigit(c) || c is ':' or ',' or '.')
            && cue.Range.Contains(query.Replace('.', ','), StringComparison.Ordinal);
    }

    /// <summary>Oturumun başlığı: "Bugün, 20:00", "Dün, 21:10", "12 Kasım, 20:00" (yerel saat).</summary>
    public static string Title(DateTime start, DateTime now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(start.ToUniversalTime(), DateTimeKind.Utc), zone);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now.ToUniversalTime(), DateTimeKind.Utc), zone).Date;
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var day = local.Date == today ? "Bugün" : local.Date == today.AddDays(-1) ? "Dün"
            : local.ToString(local.Year == today.Year ? "d MMMM" : "d MMMM yyyy", culture);
        return $"{day}, {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"GTA VI · 42 dk · 412 satır · 38 düzeltme" (oyun bilinmiyorsa yazılmaz).</summary>
    public static string Meta(HistorySession session, string? game)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(game)) parts.Add(game.Trim());
        var minutes = (int)Math.Round(session.Duration.TotalMinutes, MidpointRounding.AwayFromZero);
        parts.Add(minutes < 1 ? "1 dk'dan kısa" : string.Create(CultureInfo.InvariantCulture, $"{minutes} dk"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{session.Entries.Count} satır"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{session.Corrections} düzeltme"));
        return string.Join(" · ", parts);
    }

    /// <summary>"21 kr/sn · okumak için hızlı, kısalt" (yalnız hızlı satırda; hız yoksa null).</summary>
    public static string? SpeedWarning(HistoryCue cue) =>
        cue.Fast && cue.Cps is { } cps ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(cps):0} kr/sn · okumak için hızlı, kısalt") : null;

    /// <summary>
    /// Düzeltme kutusunun altı: "25 karakter · 11 kr/sn · sınırın altında" (satırın ekranda kaldığı süreye göre; süre
    /// bilinmiyorsa yalnız karakter). Hız sınırın üstündeyse "okumak için hızlı".
    /// </summary>
    public static string EditMetrics(string text, long durationMs)
    {
        var characters = ReadingSpeed.Characters(text);
        var line = string.Create(CultureInfo.InvariantCulture, $"{characters} karakter");
        if (ReadingSpeed.Cps(text, (int)Math.Min(int.MaxValue, durationMs)) is not { } cps) return line;
        var verdict = cps > ReadingSpeed.MaxCps ? "okumak için hızlı" : "sınırın altında";
        return string.Create(CultureInfo.InvariantCulture, $"{line} · {Math.Round(cps):0} kr/sn · {verdict}");
    }

    /// <summary>Düzeltmedeki hız sınırı aşıyor mu (kutunun altı turuncu yazılır).</summary>
    public static bool EditTooFast(string text, long durationMs) =>
        ReadingSpeed.Cps(text, (int)Math.Min(int.MaxValue, durationMs)) is { } cps && cps > ReadingSpeed.MaxCps;
}
