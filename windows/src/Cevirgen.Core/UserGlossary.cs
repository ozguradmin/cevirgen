// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cevirgen.Core;

/// <summary>Kullanıcı sözlüğündeki terimin kuralı.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GlossaryRule>))]
public enum GlossaryRule
{
    /// <summary>"çevirme": İngilizce terim olduğu gibi kalır (ad, marka, yer).</summary>
    [JsonStringEnumMemberName("keep")] Keep,
    /// <summary>"hep böyle": terim her zaman verilen Türkçeyle çevrilir.</summary>
    [JsonStringEnumMemberName("always")] Always,
}

/// <summary>
/// Çevirgen kullanıcı sözlüğü: terim ve kuralı ("çevirme" ya da "hep böyle X"). Dosya: uygulama veri klasöründe
/// glossary-user.json. Uygulanışı: yapay zekâ motorlarında (Gemini, OpenAI, Claude, yerel model) kurallar ek talimata
/// eklenir (<see cref="AiInstruction"/>); ücretsiz motor (OPUS-MT) ve DeepL'de terimler mevcut terim korumasıyla
/// (<see cref="TermProtector"/>, <see cref="DeepLMarkup"/>) uygulanır: sözlük <see cref="GlossaryStore.UserTerms"/>
/// üstünden genel sözlüğün üstüne eklenir (aynı terimde kullanıcınınki kazanır). Kullanıcının satır düzeltmeleri
/// (<see cref="UserCorrections"/>) yine her şeyden önce gelir. İş parçacığı güvenlidir.
/// </summary>
public sealed class UserGlossary
{
    /// <param name="English">Terim (sözlükte yazıldığı gibi).</param>
    /// <param name="Turkish">"Hep böyle"de karşılığı; "çevirme"de null.</param>
    public sealed record Term(string English, GlossaryRule Rule, string? Turkish, DateTime Updated)
    {
        /// <summary>Kuralın Türkçe adı ("çevirme", "hep böyle").</summary>
        [JsonIgnore]
        public string RuleLabel => Label(Rule);
    }

    public const string FileName = "glossary-user.json";
    /// <summary>Terimin en uzun hâli (genel sözlükle aynı sınır).</summary>
    public const int MaxTermLength = 80;

    private readonly object gate = new();
    private readonly Dictionary<string, Term> terms = new(StringComparer.Ordinal);
    private readonly string? file;
    private Glossary? glossary;

    /// <param name="file">Kayıt dosyası; null ise yalnız bellekte (testler).</param>
    public UserGlossary(string? file = null) => this.file = file;

    /// <summary>Sözlük değişti; çağıranın iş parçacığında.</summary>
    public event Action? Changed;

    public static string Label(GlossaryRule rule) => rule == GlossaryRule.Keep ? "çevirme" : "hep böyle";

    /// <summary>Dosyadaki terimler; dosya yoksa ya da bozuksa boş.</summary>
    public static UserGlossary Load(string? file)
    {
        var glossary = new UserGlossary(file);
        if (file is null) return glossary;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), Json);
            foreach (var term in stored?.Terms ?? [])
                if (Valid(term) is { } valid) glossary.terms[KeyOf(valid.English)] = valid;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        return glossary;
    }

    /// <summary>Terimler, alfabe sırasıyla.</summary>
    public IReadOnlyList<Term> Terms
    {
        get { lock (gate) return [.. terms.Values.OrderBy(term => term.English, StringComparer.OrdinalIgnoreCase)]; }
    }

    public int Count { get { lock (gate) return terms.Count; } }

    /// <summary>
    /// Terimi ekler ya da günceller (aynı terim büyük/küçük harf ve boşluk farkıyla da aynıdır). "Hep böyle"de Türkçe
    /// gerekir; "çevirme"de verilen Türkçe yok sayılır. Geçersizse false.
    /// </summary>
    public bool Set(string english, GlossaryRule rule, string? turkish = null, DateTime? now = null)
    {
        if (Valid(new Term(english, rule, turkish, (now ?? DateTime.UtcNow).ToUniversalTime())) is not { } term) return false;
        lock (gate)
        {
            var key = KeyOf(term.English);
            if (terms.TryGetValue(key, out var old) && old with { Updated = term.Updated } == term) return true;
            terms[key] = term;
            glossary = null;
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Terimi siler; yoksa false.</summary>
    public bool Remove(string english)
    {
        lock (gate)
        {
            if (!terms.Remove(KeyOf(Collapse(english)))) return false;
            glossary = null;
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Terim koruması için sözlük: "çevirme" terimi kendisiyle, "hep böyle" terimi Türkçesiyle. Terim yoksa null. Sözlük
    /// değişmedikçe aynı örnek döner (<see cref="GlossaryStore"/> değişikliği buradan anlar).
    /// </summary>
    public Glossary? AsGlossary()
    {
        lock (gate)
        {
            if (terms.Count == 0) return null;
            return glossary ??= Glossary.Of(terms.Values.Select(term => (term.English, term.Rule == GlossaryRule.Keep ? term.English : term.Turkish!)));
        }
    }

    /// <summary>
    /// Yapay zekâ motorları için talimat (İngilizce, sistem talimatıyla aynı dilde); terim yoksa null. Model Türkçe ekleri
    /// kendisi ekler; "çevirme" terimi aynen kalır.
    /// </summary>
    public string? AiInstruction()
    {
        var list = Terms;
        if (list.Count == 0) return null;
        var builder = new StringBuilder("Glossary (always follow these rules exactly):");
        foreach (var term in list)
        {
            builder.Append('\n');
            builder.Append(term.Rule == GlossaryRule.Keep
                ? $"- Keep \"{term.English}\" in English exactly as written; never translate it."
                : $"- Always translate \"{term.English}\" as \"{term.Turkish}\" (add Turkish suffixes when needed).");
        }
        return builder.ToString();
    }

    /// <summary>
    /// Ayarlardaki ek talimat ile sözlüğün talimatı birlikte. Sözlük boşsa ek talimat olduğu gibi döner (önbellek
    /// anahtarı ve önceki çeviriler değişmesin); ek talimat boşsa yalnız sözlüğün talimatı.
    /// </summary>
    public string? CombinedInstruction(string? userInstruction)
    {
        if (AiInstruction() is not { } rules) return userInstruction;
        return string.IsNullOrWhiteSpace(userInstruction) ? rules : userInstruction.Trim() + "\n\n" + rules;
    }

    private static Term? Valid(Term term)
    {
        if (term.English is null) return null;
        var english = Collapse(term.English);
        if (!english.Any(char.IsLetter) || english.Length > MaxTermLength) return null;
        var turkish = term.Turkish is null ? null : Collapse(term.Turkish);
        if (term.Rule == GlossaryRule.Always && string.IsNullOrEmpty(turkish)) return null;
        return term with { English = english, Turkish = term.Rule == GlossaryRule.Keep ? null : turkish };
    }

    private static string KeyOf(string english) => english.ToLowerInvariant();

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // --- Dosya ---

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Stored(int Version, List<Term> Terms);

    private void Save()
    {
        if (file is null) return;
        string json;
        lock (gate) json = JsonSerializer.Serialize(new Stored(1, [.. terms.Values.OrderBy(term => term.English, StringComparer.Ordinal)]), Json);
        AtomicFile.Write(file, json);
    }
}
