// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using CouchSubs.Core;

namespace CouchSubs.Shell;

/// <summary>Okunurluk testinin sonucu (Görünüm ekranı).</summary>
public enum Legibility { Readable, Hard, Unreadable }

/// <summary>Okunurluk testinin zemini: oyunun karanlık ve aydınlık anlarını temsil eden düz renk.</summary>
/// <param name="Name">Ekranda gösterilen ad ("Gece").</param>
/// <param name="Hex">Zeminin rengi ("#RRGGBB").</param>
/// <param name="LightPercent">Zeminin aydınlığı (yüzde; etikette "Gece · %4").</param>
public sealed record LegibilityGround(string Name, string Hex, int LightPercent)
{
    /// <summary>"Gece · %4".</summary>
    public string Label => string.Create(CultureInfo.InvariantCulture, $"{Name} · %{LightPercent}");
}

/// <summary>
/// Çevirgen: altyazının okunurluğu (Görünüm › Okunurluk testi). Aynı satır dört zeminde (gece, gölge, gündüz gökyüzü, kar)
/// kullanıcının gerçek görünümüyle değerlendirilir. Ölçü WCAG kontrast oranıdır (1–21):
/// <list type="bullet">
/// <item>Yazı ile arkasındaki renk: zemin kutusu varsa kutunun rengi zeminle koyuluğu oranında karışır
/// (<see cref="Plate"/>), yoksa doğrudan oyun zemini.</item>
/// <item>Siyah kenarlık yazıyı zeminden ayırır: kenarlık açıkken yazı ile siyah arasındaki kontrast, kalınlığa göre
/// ağırlıklı sayılır (1 px ince, 4 px tam yakın). Koyu yazıda kenarlık işe yaramaz (yazı ile siyah zaten yakındır).</item>
/// <item>İnce yazı ve 24 px'ten küçük yazı biraz daha zor okunur.</item>
/// </list>
/// Sonuç: 3 ve üstü "Okunur" (WCAG büyük yazı sınırı), 1,5 ve üstü "Zor", altı "Okunmaz". Arayüzden bağımsızdır; test edilir.
/// </summary>
public static class SubtitleLegibility
{
    /// <summary>Testin dört zemini (taslak: Appearance.dc.html).</summary>
    public static readonly IReadOnlyList<LegibilityGround> Grounds =
    [
        new("Gece", "#0C0E13", 4), new("Gölge", "#3E444E", 25), new("Gündüz gök", "#B7C6D6", 72), new("Kar", "#F1F3F5", 95),
    ];

    /// <summary>Bu orandan itibaren "Okunur".</summary>
    public const double ReadableRatio = 3;
    /// <summary>Bu orandan itibaren "Zor"; altı "Okunmaz".</summary>
    public const double HardRatio = 1.5;

    /// <summary>Kenarlığın kalınlığa göre ağırlığı (0–4 px).</summary>
    private static readonly double[] OutlineWeight = [0, 0.12, 0.3, 0.45, 0.6];

    /// <summary>"#RRGGBB" → (r, g, b) 0–255; geçersizse siyah.</summary>
    public static (int R, int G, int B) Rgb(string hex)
    {
        if (SubtitleStyle.Hex(hex) is not { } valid) return (0, 0, 0);
        var value = int.Parse(valid.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    /// <summary>Göreli parlaklık (WCAG 2.x, 0–1).</summary>
    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    /// <summary>İki rengin kontrast oranı (1–21).</summary>
    public static double Contrast(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary><paramref name="top"/> rengi <paramref name="alpha"/> (0–1) koyulukla <paramref name="bottom"/>'un üstünde.</summary>
    public static string Blend(string top, string bottom, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        var (tr, tg, tb) = Rgb(top);
        var (br, bg, bb) = Rgb(bottom);
        int Mix(int t, int b) => (int)Math.Round(t * alpha + b * (1 - alpha), MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"#{Mix(tr, br):X2}{Mix(tg, bg):X2}{Mix(tb, bb):X2}");
    }

    /// <summary>Yazının hemen arkasındaki renk: zemin kutusu koyuluğu oranında zeminle karışır; kutu yoksa zemin.</summary>
    public static string Plate(SubtitleStyle style, string ground) =>
        style.Background <= 0 ? SubtitleStyle.Hex(ground) ?? "#000000" : Blend(style.BackgroundRgb, ground, Math.Clamp(style.Background, 0, 100) / 100.0);

    /// <summary>Okunurluk puanı (kontrast oranı cinsinden; bkz. sınıf açıklaması).</summary>
    public static double Score(SubtitleStyle style, string ground)
    {
        var text = style.TextRgb;
        var direct = Contrast(text, Plate(style, ground));
        var outline = Contrast(text, "#000000") * OutlineWeight[Math.Clamp(style.OutlinePx, 0, OutlineWeight.Length - 1)];
        var score = Math.Max(direct, outline);
        if (!style.Bold) score *= 0.9;
        if (OverlayPlacement.FontSize(style.FontSize) < 24) score *= 0.85;
        return score;
    }

    public static Legibility Verdict(SubtitleStyle style, string ground)
    {
        var score = Score(style, ground);
        return score >= ReadableRatio ? Legibility.Readable : score >= HardRatio ? Legibility.Hard : Legibility.Unreadable;
    }

    /// <summary>"Okunur" / "Zor" / "Okunmaz".</summary>
    public static string Label(Legibility verdict) => verdict switch
    {
        Legibility.Readable => "Okunur",
        Legibility.Hard => "Zor",
        _ => "Okunmaz",
    };
}

/// <summary>Görünüm ekranının hazır görünümleri.</summary>
public enum AppearancePreset { Cinema, Clean, Readable, Classic }

/// <param name="Name">"Sinema".</param>
/// <param name="Description">Bir satırlık açıklama (taslaktaki gibi).</param>
public sealed record AppearancePresetInfo(AppearancePreset Id, string Name, string Description);

/// <summary>
/// Çevirgen: hazır görünümler (Görünüm › Hazır görünüm). Seçilince altyazı biçimine uygulanır; yazı rengi, zemin rengi,
/// sabit konum ve diğer ayarlar korunur. Ekrandaki seçim biçimden çıkarılır (<see cref="Match"/>): zemin koyuluğu, kalınlık,
/// kenarlık ve yer bir hazır görünümle aynıysa o seçili görünür; kullanıcı ince ayarla değiştirince hiçbiri seçili olmaz.
/// <list type="bullet">
/// <item>Sinema: zeminsiz, kalın, 2 px siyah kenarlık, İngilizcenin üstünde.</item>
/// <item>Sade: zeminsiz, ince yazı, kenarlıksız (TV hafif gölge çizer).</item>
/// <item>Okunaklı: en az 38 px, %62 zemin, 1 px kenarlık.</item>
/// <item>Klasik kutu: tam koyu zemin, İngilizcenin üstüne (örter).</item>
/// </list>
/// </summary>
public static class AppearancePresets
{
    /// <summary>Okunaklı görünümün en küçük yazı boyu.</summary>
    public const int ReadableFontSize = 38;
    /// <summary>Okunaklı görünümün zemin koyuluğu.</summary>
    public const int ReadableBackground = 62;

    public static readonly IReadOnlyList<AppearancePresetInfo> All =
    [
        new(AppearancePreset.Cinema, "Sinema", "Zeminsiz, kalın, siyah kenarlıklı."),
        new(AppearancePreset.Clean, "Sade", "İnce yazı, hafif gölge. Parlak sahnede zorlanır."),
        new(AppearancePreset.Readable, "Okunaklı", "Büyük yazı, yarı saydam zemin."),
        new(AppearancePreset.Classic, "Klasik kutu", "Koyu kutu; İngilizceyi de örtebilir."),
    ];

    /// <summary>Hazır görünüm biçime uygulanır.</summary>
    public static SubtitleStyle Apply(AppearancePreset preset, SubtitleStyle style) => preset switch
    {
        AppearancePreset.Cinema => style with
        {
            Background = 0, Bold = true, Outline = true, OutlineWidth = 2, Placement = SubtitlePlacement.Above,
        },
        AppearancePreset.Clean => style with
        {
            Background = 0, Bold = false, Outline = false, Placement = SubtitlePlacement.Above,
        },
        AppearancePreset.Readable => style with
        {
            Background = ReadableBackground, Bold = true, Outline = true, OutlineWidth = 1, Placement = SubtitlePlacement.Above,
            BackgroundShape = SubtitleBackgroundShape.Fit,
            FontSize = Math.Clamp(Math.Max(style.FontSize, ReadableFontSize), AppearanceSteps.MinFont, AppearanceSteps.MaxFont),
        },
        _ => style with
        {
            Background = 100, Bold = true, Outline = false, Placement = SubtitlePlacement.Cover,
        },
    };

    /// <summary>Biçime uyan hazır görünüm; hiçbiri uymuyorsa null.</summary>
    public static AppearancePreset? Match(SubtitleStyle style)
    {
        foreach (var info in All)
        {
            var applied = Apply(info.Id, style);
            if (applied.Background == style.Background && applied.Bold == style.Bold && applied.OutlinePx == style.OutlinePx
                && applied.Placement == style.Placement && (info.Id != AppearancePreset.Readable || style.FontSize >= ReadableFontSize))
                return info.Id;
        }
        return null;
    }
}

/// <summary>
/// Görünüm ekranının ince ayar düğmeleri (− / +): yazı boyu 16–50 px (1920 genişlikli TV pikseli; 2'şer), kenarlık
/// 0–4 px (1'er), zemin koyuluğu %0–100 (5'er; ara değer en yakın adıma oturur). Satır sınırı profesyonel altyazı kuralıdır.
/// </summary>
public static class AppearanceSteps
{
    public const int MinFont = 16;
    public const int MaxFont = 50;
    public const int FontStep = 2;
    public const int BackgroundStep = 5;
    /// <summary>Uzun çeviri en çok iki satıra bölünür.</summary>
    public const int MaxLines = 2;
    /// <summary>Bir satırda en çok bu kadar karakter.</summary>
    public const int MaxCharacters = 42;

    /// <summary>"2 satır · 42 karakter".</summary>
    public static string LineLimit => string.Create(CultureInfo.InvariantCulture, $"{MaxLines} satır · {MaxCharacters} karakter");

    /// <summary>
    /// Değer <paramref name="direction"/> (−1 / +1) yönde bir adım: adımın katı değilse önce o yöndeki en yakın kata oturur;
    /// sonuç sınırların içinde kalır.
    /// </summary>
    public static int Step(int value, int direction, int min, int max, int step)
    {
        value = Math.Clamp(value, min, max);
        if (step <= 1) return Math.Clamp(value + Math.Sign(direction), min, max);
        var offset = value - min;
        var next = direction > 0 ? (offset / step + 1) * step : (offset % step == 0 ? offset - step : offset / step * step);
        return Math.Clamp(min + next, min, max);
    }

    public static SubtitleStyle FontSize(SubtitleStyle style, int direction) =>
        style with { FontSize = Step(style.FontSize, direction, MinFont, MaxFont, FontStep) };

    /// <summary>Kenarlık: 0'da kenarlık kapanır, 0'dan büyüyünce açılır.</summary>
    public static SubtitleStyle Outline(SubtitleStyle style, int direction)
    {
        var width = Step(style.OutlinePx, direction, 0, SubtitleStyle.MaxOutlineWidth, 1);
        return style with { Outline = width > 0, OutlineWidth = width > 0 ? width : style.OutlineWidth };
    }

    public static SubtitleStyle Background(SubtitleStyle style, int direction) =>
        style with { Background = Step(style.Background, direction, 0, 100, BackgroundStep) };
}
