// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;

namespace CouchSubs.Core;

/// <summary>
/// PS5'in Uzaktan Oynatma bağlantı bildirimi: Uzaktan Oynatma bağlıyken PS5 ekranın üstünde kalıcı bir "Uzaktan Oynatma'ya
/// bağlandı" / "Connected to Remote Play" penceresi gösterir (PS5'te Ayarlar › Sistem › Uzaktan Oynatma › "Uzaktan Oynatma
/// Bağlantı Durumu Açılır Penceresini Gizle" kapalıysa). Yazı tanıma onu sık sık okur, çoğu zaman bozarak ("Uzaktan
/// Oynama", "tan Oynama", "Remote PIay"). Bu yazı altyazı değildir: çevrilmez, gösterilmez, ortak havuza gitmez; görülünce
/// Canlı ekranda ayarın nasıl kapatılacağı söylenir.
/// </summary>
public static class RemotePlayNotice
{
    /// <summary>Okumada bildirimin satırları var mı.</summary>
    public static bool Contains(Reading reading) => Matching(reading).Count > 0;

    /// <summary>
    /// Bildirime ait satırlar: tek başına eşleşen satırlar ve bildirim bulunduysa onun yalnız "bağlandı" / "connected"
    /// sözcüğünden oluşan parçası (bildirim iki satıra bölünmüş olabilir). İngilizce biçim iki satıra bölünmüşse ("Connected
    /// to" / "Remote Play") satırlar birlikte de denenir.
    /// </summary>
    public static IReadOnlyList<TextLine> Matching(Reading reading)
    {
        var lines = reading.Lines;
        var found = new List<TextLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (Matches(lines[i].Text)) { found.Add(lines[i]); continue; }
            // "Connected to" + "Remote Play" (ya da tersi) alt alta.
            if (i + 1 < lines.Count && !Matches(lines[i + 1].Text) && Matches(lines[i].Text + " " + lines[i + 1].Text)
                && (IsRemoteOnly(lines[i].Text) || IsRemoteOnly(lines[i + 1].Text)))
            {
                found.Add(lines[i]);
                found.Add(lines[i + 1]);
                i++;
            }
        }
        if (found.Count == 0) return found;
        foreach (var line in lines)
            if (!found.Contains(line) && IsConnectionOnly(line.Text)) found.Add(line);
        return found;
    }

    /// <summary>Okuma, bildirimin satırları atılmış olarak (yoksa aynısı).</summary>
    public static Reading Without(Reading reading, out bool found)
    {
        var matching = Matching(reading);
        found = matching.Count > 0;
        return found ? new Reading([.. reading.Lines.Where(line => !matching.Contains(line))]) : reading;
    }

    /// <summary>
    /// Satır bildirimin kendisi mi: Türkçede "Uzaktan" (ya da kırpılmış "aktan", "tan") hemen ardından "Oynatma"/"Oynama";
    /// İngilizcede "Remote Play" ve "connected"/"bağlandı" (ikisi aynı satırda).
    /// </summary>
    public static bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = Fold(text);
        for (var i = 0; i + 1 < words.Count; i++)
            if (IsRemoteTr(words[i]) && IsPlayTr(words[i + 1])) return true;
        var remote = false;
        for (var i = 0; i < words.Count; i++)
        {
            if (IsRemoteEn(words[i]) && i + 1 < words.Count && IsPlayEn(words[i + 1])) remote = true;
            // Boşluksuz okunmuş "RemotePlay".
            if (words[i].Length >= 9 && IsRemoteEn(words[i][..6]) && IsPlayEn(words[i][6..])) remote = true;
        }
        return remote && words.Any(IsConnected);
    }

    private static bool IsRemoteOnly(string text)
    {
        var words = Fold(text);
        return words.Count is >= 1 and <= 3 && words.Any(IsRemoteEn) && words.Any(IsPlayEn) || words.Count == 1 && words[0].Length >= 9
            && IsRemoteEn(words[0][..6]) && IsPlayEn(words[0][6..]);
    }

    private static bool IsConnectionOnly(string text)
    {
        var words = Fold(text);
        return words.Count is >= 1 and <= 3 && words.Any(IsConnected) && words.All(word => IsConnected(word) || word is "to" or "ya" or "e" or "the");
    }

    // "uzaktan" (en çok iki harf farkı) ya da kırpılmış sonu ("zaktan", "aktan", "ktan", "tan"; ekranın kenarında kesilir).
    private static bool IsRemoteTr(string word) =>
        word.Length >= 3 && ("uzaktan".EndsWith(word, StringComparison.Ordinal) || Distance(word, "uzaktan") <= 2 && word.Length >= 5);

    // "oynatma", "oynatmaya", "oynama", "oynatma'ya" (kesme işaretinden bölünür) ve bir-iki harf farkı.
    private static bool IsPlayTr(string word) =>
        word.StartsWith("oyna", StringComparison.Ordinal) || word.Length >= 5 && (Distance(word, "oynatma") <= 2 || Distance(word, "oynatmaya") <= 2);

    private static bool IsRemoteEn(string word) => word.Length is >= 5 and <= 7 && Distance(word, "remote") <= 1;

    // "play", "pIay", "p1ay", "plav" (I, 1 ve | katlamada l olur).
    private static bool IsPlayEn(string word) => word.Length is >= 3 and <= 5 && Distance(word, "play") <= 1;

    private static bool IsConnected(string word) =>
        word.Length >= 6 && (Distance(word, "connected") <= 2 || Distance(word, "connection") <= 2 || Distance(word, "baglandi") <= 2
                             || word.StartsWith("baglan", StringComparison.Ordinal) || word.StartsWith("connect", StringComparison.Ordinal));

    /// <summary>
    /// Karşılaştırma için sözcükler: küçük harf, aksansız (ç→c, ş→s, ğ→g, ö→o, ü→u, ı/İ→i), yazı tanımanın karıştırdığı
    /// "1", "|", "!" l olur, "0" o olur; kesme işaretleri ve noktalama sözcük ayırır.
    /// </summary>
    internal static List<string> Fold(string text)
    {
        var lower = text.Replace('İ', 'i').Replace('I', 'l').ToLower(CultureInfo.InvariantCulture).Replace('ı', 'i');
        var plain = new StringBuilder(lower.Length);
        foreach (var c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(c switch { '1' or '|' or '!' => 'l', '0' => 'o', _ => c });
        }
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in plain.ToString())
        {
            if (char.IsLetterOrDigit(c)) { current.Append(c); continue; }
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    /// <summary>Levenshtein uzaklığı (kısa sözcükler için).</summary>
    internal static int Distance(string a, string b)
    {
        if (a == b) return 0;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
