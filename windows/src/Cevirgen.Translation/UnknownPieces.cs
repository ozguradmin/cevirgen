// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cevirgen.Translation;

/// <summary>
/// OPUS-MT sözlüğünde olmayan karakterler. Kaynakta (ñ, é, ß, €) model bunları bilinmeyen parça görür; çıktıda ise
/// bilinmeyen parça (SentencePiece "⁇") olarak döner: "coño" → "co⁇o", "Mañana" → "Ma⁇ana", "mahkûmun" → "mahk⁇mun".
/// Eskiden çözücü bu parçayı sessizce siliyordu ("coo", "Maana", "mahkmun"). Kaynak çeviriden önce zararsızca
/// sadeleştirilir (é → e, ñ → n, « → “); çıktıdaki ⁇ kaynaktaki sözcükten, yoksa küçük bir Türkçe tablodan doldurulur, o da
/// olmazsa atılır. Sadeleşmiş kaynak sözcüğü çıktıya aynen geçtiyse ("Manana") özgün yazımı geri konur ("Mañana").
/// Yalnız ücretsiz motor kullanır.
/// </summary>
internal static partial class UnknownPieces
{
    /// <summary>SentencePiece'in bilinmeyen parça işareti.</summary>
    public const string Marker = "⁇";

    /// <summary>Sözlükte olmayan, anlamı değiştirmeden bilinen karşılığa çevrilen işaretler.</summary>
    private static readonly Dictionary<char, string> Punctuation = new()
    {
        ['«'] = "“",
    };

    /// <summary>
    /// Çıktıda ⁇ kalan ve kaynakta karşılığı olmayan Türkçe sözcük parçaları (şapkalı harfler). Küçük harfle; ⁇ joker.
    /// </summary>
    private static readonly (string Pattern, string Replacement)[] Turkish =
    [
        ("h⁇l⁇", "hâlâ"),
        ("k⁇m", "kûm"),
        ("k⁇r", "kâr"),
    ];

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex(@"[^\s]*⁇[^\s]*")]
    private static partial Regex MarkedWord();

    [GeneratedRegex(@"\p{L}+(?:[\p{L}\p{N}]|[-.](?=\p{L}))*")]
    private static partial Regex Word();

    private static readonly char[] Edges = ['.', ',', '!', '?', ';', ':', '"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', '…'];

    [GeneratedRegex(@"[ ]{2,}")]
    private static partial Regex Spaces();

    /// <summary>
    /// Modelin hiç görmediği karakterleri zararsız karşılığına çevirir: aksanlı harf, sözlükte varsa aksansız
    /// biçimine (café → cafe, Mañana → Manana); « açılış tırnağına. <paramref name="known"/> karakterin
    /// sözlükte bilinip bilinmediğini söyler. Bilinen karakterlere ve Türkçe harflere (ç, ğ, ı, ö, ş, ü, â) dokunulmaz.
    /// </summary>
    public static string Simplify(string text, Func<char, bool> known)
    {
        text = text.Normalize(NormalizationForm.FormC);
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            string? replacement = null;
            if (c > 0x7F && !char.IsSurrogate(c))
            {
                if (Punctuation.TryGetValue(c, out var mapped)) replacement = mapped;
                else if (char.IsLetter(c) && !known(c) && Plain(c) is { } plain && plain.All(known)) replacement = plain;
            }
            if (replacement is null)
            {
                builder?.Append(c);
                continue;
            }
            builder ??= new StringBuilder(text, 0, i, text.Length + 8);
            builder.Append(replacement);
        }
        return builder is null ? text : Spaces().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>Harfin aksansız biçimi (ñ → n, É → E); ayrışmıyorsa null.</summary>
    private static string? Plain(char letter)
    {
        var decomposed = letter.ToString().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        return plain.Length > 0 && plain.ToString() != letter.ToString() ? plain.ToString() : null;
    }

    /// <summary>
    /// Model çıktısını onarır. <paramref name="source"/> modele verilen metnin sadeleşmemiş hâlidir (yer tutucular
    /// ve bağlam dahil). Sırayla: ⁇'lı sözcük kaynaktaki bir sözcüğe uyuyorsa (⁇ bir ya da daha çok harf, büyük/küçük
    /// harf ayrımsız) o harfler; olmazsa Türkçe tablo; o da olmazsa ⁇ atılır. Ardından sadeleşmiş kaynak sözcüğü
    /// çıktıda aynen geçiyorsa özgün yazımı konur.
    /// </summary>
    public static string Repair(string source, string output)
    {
        // Sözcükler ve boşlukla ayrılmış parçalar ("50€", "30°"): ⁇ harf dışı bir karakterden de doğabilir.
        var sourceWords = Word().Matches(source).Select(m => m.Value)
            .Concat(source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim(Edges)))
            .Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (output.Contains(Marker, StringComparison.Ordinal))
        {
            output = MarkedWord().Replace(output, match => FillMarked(match.Value, sourceWords));
            output = output.Replace(Marker, "", StringComparison.Ordinal);
            output = Spaces().Replace(output, " ").Trim();
        }
        return RestoreSpelling(output, sourceWords);
    }

    private static string FillMarked(string token, IReadOnlyList<string> sourceWords)
    {
        // Sözcüğün çevresindeki noktalama ve Türkçe ek ("Ma⁇ana'ya") korunur; yalnız ⁇'lı gövde doldurulur.
        var start = 0;
        while (start < token.Length && !char.IsLetterOrDigit(token[start]) && !token.AsSpan(start).StartsWith(Marker)) start++;
        var end = token.Length;
        while (end > start && !char.IsLetterOrDigit(token[end - 1]) && !token.AsSpan(0, end).EndsWith(Marker)) end--;
        var body = token[start..end];
        var apostrophe = body.IndexOfAny(['\'', '’']);
        var suffix = "";
        if (apostrophe > 0 && body[..apostrophe].Contains(Marker, StringComparison.Ordinal))
        {
            suffix = body[apostrophe..];
            body = body[..apostrophe];
        }
        if (!body.Contains(Marker, StringComparison.Ordinal)) return token;
        var filled = FromSource(body, sourceWords) ?? FromTable(body);
        return filled is null ? token : token[..start] + filled + suffix + token[end..];
    }

    /// <summary>"co⁇o" + kaynakta "coño" → "coño". ⁇ yerine kaynaktaki harfler konur, gerisi çıktının yazımıyla kalır.</summary>
    private static string? FromSource(string body, IReadOnlyList<string> sourceWords)
    {
        var parts = body.Split(Marker);
        if (parts.All(part => part.Length == 0)) return null;
        var pattern = new Regex("^" + string.Join("(.+?)", parts.Select(Regex.Escape)) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var word in sourceWords)
        {
            var match = pattern.Match(word);
            // ⁇ yalnız sözlükte olmayan karakterden doğar: dolgu ASCII dışı karakter içermeli ("⁇nce" kaynakta "since" olmaz).
            if (!match.Success || !match.Groups.Values.Skip(1).All(group => group.Value.Any(c => c > 0x7F))) continue;
            var upper = body.Any(char.IsLetter) && body.Where(char.IsLetter).All(char.IsUpper) && body.Count(char.IsLetter) > 1;
            var builder = new StringBuilder(parts[0]);
            for (var i = 1; i < parts.Length; i++)
            {
                var fill = match.Groups[i].Value;
                builder.Append(upper ? fill.ToUpperInvariant() : fill).Append(parts[i]);
            }
            return builder.ToString();
        }
        return null;
    }

    private static string? FromTable(string body)
    {
        var lower = body.ToLower(TurkishCulture);
        foreach (var (pattern, replacement) in Turkish)
        {
            var at = lower.IndexOf(pattern, StringComparison.Ordinal);
            if (at < 0) continue;
            var original = body.Substring(at, pattern.Length);
            var fitted = char.IsUpper(original.FirstOrDefault(char.IsLetter))
                ? char.ToUpper(replacement[0], TurkishCulture) + replacement[1..]
                : replacement;
            return FromTable(body[..at] + fitted + body[(at + pattern.Length)..]) ?? body[..at] + fitted + body[(at + pattern.Length)..];
        }
        return null;
    }

    /// <summary>"Manana" (model sadeleşmiş kaynağı aynen yazdı) → "Mañana"; yalnız kaynakta aksanlı yazılan sözcükler.</summary>
    private static string RestoreSpelling(string output, IReadOnlyList<string> sourceWords)
    {
        Dictionary<string, string>? accented = null;
        foreach (var word in sourceWords)
        {
            var plain = Strip(word);
            if (plain == word) continue;
            accented ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            accented.TryAdd(plain, word);
        }
        if (accented is null) return output;
        return Word().Replace(output, match =>
        {
            if (!accented.TryGetValue(match.Value, out var original)) return match.Value;
            // Kaynağın yazımı; tümü büyük harfli çıktı büyük kalır, cümle başında ilk harf büyür ("cono" → "coño").
            if (match.Value.Length > 1 && match.Value.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                return original.ToUpperInvariant();
            return SentenceStart(output, match.Index) ? char.ToUpperInvariant(original[0]) + original[1..] : original;
        });
    }

    /// <summary>Konumdan önce yalnız boşluk/açılış tırnağı ve cümle sonu (ya da bağlam ayracı) var mı.</summary>
    private static bool SentenceStart(string text, int index)
    {
        var i = index - 1;
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || text[i] is '"' or '“' or '\'' or '‘' or '(' or '[' or '-' or '–')) i--;
        return i < 0 || text[i] is '.' or '!' or '?' or '…' or '*' or ':';
    }

    /// <summary>Sözcüğün sözlükte olmayan aksanlı harfleri aksansız (Türkçe harflere dokunulmaz).</summary>
    private static string Strip(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var c in word)
            builder.Append(c > 0x7F && !TurkishLetters.Contains(c) && Plain(c) is { Length: 1 } plain ? plain : c.ToString());
        return builder.ToString();
    }

    private const string TurkishLetters = "çğıöşüÇĞİÖŞÜâîÂÎ";
}
