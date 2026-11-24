// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// Çevirgen: okuma hızı (CPS, saniyedeki karakter). Türkçe satırın karakterleri (satır sonları sayılmaz; boşluk ve
/// noktalama sayılır, Netflix'in saydığı gibi), İngilizcesi ekrandayken Türkçesinin göründüğü süreye bölünür.
/// Profesyonel sınır saniyede 17 karakter (Netflix, yetişkin içerik); üstündeki satır "hızlı" sayılır.
/// </summary>
public static class ReadingSpeed
{
    /// <summary>Netflix'in yetişkin içerik için okuma hızı sınırı.</summary>
    public const double MaxCps = 17;
    /// <summary>Bundan kısa görünen satırın hızı hesaplanmaz (ölçüm gürültüsü; ör. hemen yerine başkası geldi).</summary>
    public const int MinVisibleMs = 100;

    /// <summary>Okuma hızı, bir ondalığa yuvarlanmış; süre çok kısaysa ya da metin boşsa null.</summary>
    public static double? Cps(string text, int visibleMs)
    {
        var characters = Characters(text);
        if (characters == 0 || visibleMs < MinVisibleMs) return null;
        return Math.Round(characters / (visibleMs / 1000.0), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Sayılan karakterler: satır sonları dışında hepsi (baştaki ve sondaki boşluk hariç).</summary>
    public static int Characters(string text) => text.Trim().Count(c => c is not ('\n' or '\r'));
}

/// <summary>
/// Çevirgen: oturumun özeti (arayüz için). <paramref name="Lines"/>: gösterilen satır; <paramref name="Corrections"/>:
/// kullanıcının düzeltmesinden ("senin") gelen satır; <paramref name="FromPacks"/>: paketten gelen satır;
/// <paramref name="AverageCps"/>: hızı ölçülen satırların ortalaması; <paramref name="FastLines"/>:
/// <see cref="ReadingSpeed.MaxCps"/> üstündekiler; <paramref name="AverageLatencyMs"/>: satırın okunduğu kareden TV'ye
/// gidene kadar ortalama gecikme (<see cref="LineMetrics.TotalMs"/>); ölçüm yoksa null.
/// </summary>
public sealed record SessionStats(int Lines, int Corrections, int FromPacks, double? AverageCps, int FastLines, int? AverageLatencyMs)
{
    public static readonly SessionStats Empty = new(0, 0, 0, null, 0, null);

    /// <param name="entries">Oturumun geçmiş kayıtları.</param>
    /// <param name="latencies">Oturumda TV'ye giden satırların gecikmeleri (ms).</param>
    public static SessionStats From(IEnumerable<SubtitleHistory.Entry> entries, IEnumerable<int> latencies)
    {
        var list = entries.ToList();
        var speeds = list.Where(entry => entry.Cps is not null).Select(entry => entry.Cps!.Value).ToList();
        var delays = latencies.ToList();
        return new SessionStats(
            list.Count,
            list.Count(entry => entry.IsCorrection),
            list.Count(entry => entry.Origin == LineOrigin.Pack),
            speeds.Count == 0 ? null : Math.Round(speeds.Average(), 1, MidpointRounding.AwayFromZero),
            list.Count(entry => entry.IsFast),
            delays.Count == 0 ? null : (int)Math.Round(delays.Average(), MidpointRounding.AwayFromZero));
    }
}
