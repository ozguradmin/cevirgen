// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace CouchSubs.Core;

/// <summary>
/// Kişisel bilgiye benzeyen yazı (tutucu): e-posta, bağlantı ya da alan adı, telefon gibi uzun rakam dizisi, @ad, #numara
/// ve kullanıcı adı / PSN kimliğine benzeyen sözcük ("xX_Gamer_99", "Player1234"). Böyle satır ortak havuza hiç girmez
/// (<see cref="LinePool"/>). Sunucudaki server/cevirgen-api/src/lines.js <c>hasPersonalData</c> ile aynı kurallar; sunucu
/// da ayrıca süzer.
/// </summary>
public static partial class PersonalText
{
    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(\.[\p{L}\p{N}-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"(^|[^\p{L}\p{N}])@[\p{L}\p{N}_.]{2,}")]
    private static partial Regex Handle();

    [GeneratedRegex(@"(https?://|\bwww\.|\b[\p{L}\p{N}-]+\.(com|net|org|io|gg|tv|me|co|tr|de|uk|ru|info|biz|xyz|app|dev|ly|be|to|fm|link|site|online|store|shop)\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlLike();

    [GeneratedRegex(@"#\s?\d")]
    private static partial Regex HashNumber();

    /// <summary>Rakamlar ve aralarındaki boşluk, tire, nokta, parantez: en az 7 rakam telefon numarası sayılır.</summary>
    [GeneratedRegex(@"\+?\(?\d[\d\s().\-]{5,}\d")]
    private static partial Regex DigitRun();

    /// <summary>Oyun dilinde olağan harf-rakam karışımları: "3rd", "1990s", "10pm", "9mm", "500k", "$20m", "50%".</summary>
    [GeneratedRegex(@"^\$?\d+([.,]\d+)?(s|st|nd|rd|th|am|pm|k|m|bn|km|mph|kmh|kg|lb|lbs|ft|mm|cm|hp|x|%|°|gb|mb)$", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinaryMixed();

    [GeneratedRegex(@"^\d+x\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex Dimension();

    /// <summary>Satır kişisel bilgi taşıyor gibi mi.</summary>
    public static bool Looks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (Email().IsMatch(text) || Handle().IsMatch(text) || UrlLike().IsMatch(text) || HashNumber().IsMatch(text)) return true;
        foreach (Match run in DigitRun().Matches(text))
            if (run.Value.Count(char.IsDigit) >= 7) return true;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(LooksLikeHandle);
    }

    /// <summary>Kullanıcı adına benzeyen sözcük: alt çizgi taşır ya da harfle birlikte en az iki rakam ("Player1234").</summary>
    public static bool LooksLikeHandle(string token)
    {
        // Baştaki ve sondaki harf, rakam ya da alt çizgi olmayan her şey atılır (tırnak, noktalama, "$", "%").
        int start = 0, end = token.Length;
        while (start < end && !IsWordChar(token[start])) start++;
        while (end > start && !IsWordChar(token[end - 1])) end--;
        var t = token[start..end];
        if (t.Length == 0) return false;
        if (t.Contains('_') && t.Replace("_", "").Any(char.IsLetterOrDigit)) return true;
        var letters = t.Count(char.IsLetter);
        var digits = t.Count(char.IsDigit);
        if (letters == 0 || digits < 2) return false;
        return !OrdinaryMixed().IsMatch(t) && !Dimension().IsMatch(t);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
