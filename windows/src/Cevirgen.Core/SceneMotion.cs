// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// Kamerayla kayan yazı (plaka, tabela, dükkân adı, haritadaki yer adı) altyazı değildir. Oyunun altyazısı ekranda hep
/// aynı yerde ve aynı boyda durur; oyun dünyasındaki yazı ise kamera, araç ya da harita oynadıkça yana kayar ya da
/// yaklaşıp büyür. Kullanıcı bildirimleri (GitHub #1259, #1203, GTA 6): "MOTOR OIL MOTOR OIL" tabelası, "Game Base" dükkân
/// adı, haritada "Tola Bay", teknedeki "Charters" yazısı altyazı alanından okunup çevriliyor, "Charters / We're doing that
/// tomorrow." diye konuşmanın başına yapışıyordu; kısa tek satır oldukları için <see cref="SceneText"/>'in plaka ve eğik
/// satır kurallarına takılmıyorlardı.
/// Aynı yazı (birebir aynı okunan satır) <see cref="WindowMs"/> içinde yatay ortası karenin genişliğinin
/// <see cref="Shift"/> kadarı kaydıysa ya da boyu <see cref="Scale"/> katı değiştiyse kayan yazıdır; son kaydığı andan
/// <see cref="HoldMs"/> sonrasına dek okumadan çıkarılır. Gerçek altyazıyı düşürmemek için konuşan adlı satır ve küçük
/// harfli, <see cref="MaxWords"/>'ten uzun satır hiç çıkarılmaz; yalnız yatay kayma ve boy değişimi sayılır (yukarı itilen
/// altyazı satırı kalır); yalnız birebir aynı okuma, önceki okumalarla karşılaştırılır. Tek karede hiçbir şey çıkarılmaz.
/// Mac/iPhone (SceneMotion.swift) ve Android (SceneMotion.kt) aynı kuralları ve eşikleri uygular; ortak deneme dosyası
/// shared/fixtures/scene-motion.json.
/// </summary>
public sealed class SceneMotion
{
    /// <summary>Kayma bu süre içindeki ilk görüşe göre ölçülür (yavaş kayan yazı birikerek yakalanır).</summary>
    public const long WindowMs = 2000;
    /// <summary>Yatay ortanın en az bu kadar kayması (karenin genişliğine oranla).</summary>
    public const double Shift = 0.02;
    /// <summary>Satır boyu en az bu kat değişirse (yaklaşan/uzaklaşan tabela) kayan yazıdır.</summary>
    public const double Scale = 1.4;
    /// <summary>Son kaymadan bu kadar sonra yazı yeniden aday olur.</summary>
    public const long HoldMs = 1000;
    /// <summary>Küçük harfli satır en çok bu kadar sözcükse kayan yazı sayılabilir.</summary>
    public const int MaxWords = 3;
    /// <summary>Günlükte atılma nedeni.</summary>
    public const string Reason = "kayan yazı (kamerayla kayan tabela/plaka)";

    private sealed class Anchor
    {
        public required string Text;
        public double Center;
        public double Height;
        public long Since;
        public long LastSeen;
        public long? MovedAt;
    }

    private readonly List<Anchor> anchors = [];

    /// <summary>Son <see cref="Filter"/> çağrısında kayan yazı sayılıp okumadan çıkarılan satırlar (günlük için).</summary>
    public IReadOnlyList<TextLine> Removed { get; private set; } = [];

    /// <summary>
    /// Okumanın kayan yazıları çıkarılmış hali. Satır kutuları <paramref name="area"/>'ya oranlıdır; <paramref name="area"/>
    /// okunan alanın karedeki yeridir. <paramref name="time"/>: milisaniye saati.
    /// </summary>
    public Reading Filter(Reading reading, TextBox area, long time)
    {
        var removed = new List<TextLine>();
        anchors.RemoveAll(anchor => time - anchor.LastSeen > WindowMs);
        var previous = anchors.Where(anchor => anchor.Since < time).ToList();
        var used = new HashSet<Anchor>();
        var fresh = new List<Anchor>();
        var kept = new List<TextLine>();
        foreach (var line in reading.Lines)
        {
            if (line.Box is not { } relative || !Eligible(line.Text))
            {
                kept.Add(line);
                continue;
            }
            var box = relative.PlacedIn(area);
            var text = Key(line.Text);
            double center = (box.Left + box.Right) / 2, height = box.Bottom - box.Top;
            var match = previous.Where(anchor => !used.Contains(anchor) && anchor.Text == text)
                .OrderBy(anchor => Math.Abs(anchor.Center - center)).FirstOrDefault();
            if (match is null)
            {
                fresh.Add(new Anchor { Text = text, Center = center, Height = height, Since = time, LastSeen = time });
                kept.Add(line);
                continue;
            }
            used.Add(match);
            match.LastSeen = time;
            if (Moved(match, center, height))
            {
                match.MovedAt = time;
                (match.Center, match.Height, match.Since) = (center, height, time);
            }
            else if (time - match.Since > WindowMs)
            {
                // Yerinde duran yazının ölçüsü tazelenir: çok yavaş kayma (pencere başına Shift'ten az) birikmez.
                (match.Center, match.Height, match.Since) = (center, height, time);
            }
            if (match.MovedAt is { } moved && time - moved <= HoldMs) removed.Add(line);
            else kept.Add(line);
        }
        anchors.AddRange(fresh);
        Removed = removed;
        return removed.Count == 0 ? reading : new Reading(kept);
    }

    public void Reset()
    {
        anchors.Clear();
        Removed = [];
    }

    /// <summary>
    /// Satır kayan yazı sayılabilir mi: konuşan adı yok, en az üç harf/rakam; küçük harf taşıyorsa en çok
    /// <see cref="MaxWords"/> sözcük (konuşma cümlesi hiç çıkarılmaz).
    /// </summary>
    internal static bool Eligible(string text)
    {
        if (SpeakerPrefix.Parse(text) is not null || text.Count(char.IsLetterOrDigit) < 3) return false;
        return !text.Any(char.IsLower) || SceneText.WordCount(text) <= MaxWords;
    }

    private static string Key(string text) => text.Trim().ToLowerInvariant();

    private static bool Moved(Anchor anchor, double center, double height)
    {
        if (Math.Abs(center - anchor.Center) >= Shift) return true;
        if (anchor.Height <= 0 || height <= 0) return false;
        return Math.Max(height, anchor.Height) >= Scale * Math.Min(height, anchor.Height);
    }
}
