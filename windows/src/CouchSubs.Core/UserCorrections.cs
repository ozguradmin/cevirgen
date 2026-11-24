// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CouchSubs.Core;

/// <summary>
/// Çevirgen "Düzelt": kullanıcının kendi çevirileri. İngilizce satır (oyun paketinin anahtarıyla aynı sadeleştirme,
/// <see cref="PackKey.Canon"/>: büyük/küçük harf, noktalama ve aksan farkı yok) → Türkçe. Satır okununca düzeltme
/// paketlerden, önbellekten ve çeviri motorlarından önce gelir (günlükte "senin"). Konuşan adı ("JASON: …") anahtara
/// girmez: düzeltme adsız cümleye yazılır, ad gösterilirken eklenir. Çok satırlı okumada her satırın düzeltmesi varsa
/// satırlar ayrı ayrı bulunur. Dosya: uygulama veri klasöründe corrections.json (önce geçici dosyaya, sonra yerine
/// konarak yazılır). İş parçacığı güvenlidir: hat okurken arayüz ekleyip silebilir.
/// </summary>
public sealed class UserCorrections
{
    /// <param name="English">Kullanıcının düzelttiği İngilizce satır (konuşan adı olmadan, okunduğu gibi).</param>
    /// <param name="Turkish">Kullanıcının Türkçesi.</param>
    /// <param name="Updated">Son değiştiği an (UTC).</param>
    public sealed record Correction(string English, string Turkish, DateTime Updated);

    public const string FileName = "corrections.json";
    /// <summary>Bir satırın en uzun hâli (dosya şişmesin).</summary>
    public const int MaxLength = 500;

    private readonly object gate = new();
    private readonly Dictionary<string, Correction> byKey = new(StringComparer.Ordinal);
    private readonly string? file;

    /// <param name="file">Kayıt dosyası; null ise yalnız bellekte (testler).</param>
    public UserCorrections(string? file = null) => this.file = file;

    /// <summary>Düzeltmeler değişti (eklendi, güncellendi, silindi); çağıranın iş parçacığında.</summary>
    public event Action? Changed;

    /// <summary>Dosyadaki düzeltmeler; dosya yoksa ya da bozuksa boş.</summary>
    public static UserCorrections Load(string? file)
    {
        var corrections = new UserCorrections(file);
        if (file is null) return corrections;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), Json);
            foreach (var entry in stored?.Corrections ?? [])
                if (entry is { English: not null, Turkish: not null } && Key(entry.English) is { } key) corrections.byKey[key] = entry;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        return corrections;
    }

    /// <summary>Bütün düzeltmeler, en son değişen başta.</summary>
    public IReadOnlyList<Correction> All
    {
        get { lock (gate) return [.. byKey.Values.OrderByDescending(entry => entry.Updated).ThenBy(entry => entry.English, StringComparer.Ordinal)]; }
    }

    public int Count { get { lock (gate) return byKey.Count; } }

    /// <summary>
    /// Düzeltmeyi ekler ya da günceller. Baştaki konuşan adı İngilizceden (Türkçede de aynı ad varsa ondan) atılır.
    /// İngilizcede harf/rakam yoksa ya da Türkçe boşsa eklenmez (false).
    /// </summary>
    public bool Set(string english, string turkish, DateTime? now = null)
    {
        var (source, target) = Clean(english, turkish);
        if (Key(source) is not { } key || target.Length == 0) return false;
        lock (gate)
        {
            if (byKey.TryGetValue(key, out var old) && old.Turkish == target && old.English == source) return true;
            byKey[key] = new Correction(source, target, (now ?? DateTime.UtcNow).ToUniversalTime());
        }
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Satırın düzeltmesini siler (aynı sadeleştirmeyle, adlı ya da adsız yazılmış olsa da); yoksa false.</summary>
    public bool Remove(string english)
    {
        var body = SpeakerPrefix.Parse(english)?.Body ?? english;
        bool removed;
        lock (gate) removed = (Key(english) is { } whole && byKey.Remove(whole)) || (Key(body) is { } bare && byKey.Remove(bare));
        if (!removed) return false;
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Bu satırın kaydı (adlı satırda adsız cümleninki); yoksa null.</summary>
    public Correction? Get(string english)
    {
        lock (gate)
        {
            if (Key(english) is { } key && byKey.TryGetValue(key, out var found)) return found;
            return SpeakerPrefix.Parse(english) is { } prefix && Key(prefix.Body) is { } bare && byKey.TryGetValue(bare, out var named) ? named : null;
        }
    }

    /// <summary>
    /// Okunan satırın düzeltmesi (konuşan adı olmadan; hat adı gösterirken ekler); yoksa null. Sırayla: satırın tamamı,
    /// adsız cümlesi, çok satırlıysa her satır (hepsinin düzeltmesi olmalı; adlı satırın adı korunur).
    /// </summary>
    public string? Find(string text)
    {
        lock (gate)
        {
            if (byKey.Count == 0) return null;
            if (Single(text) is { } whole) return whole;
            var lines = text.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count < 2) return null;
            var parts = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                if (Single(line) is not { } found) return null;
                parts.Add(SpeakerPrefix.Parse(line) is { } prefix ? $"{prefix.Name}: {found}" : found);
            }
            return string.Join("\n", parts);
        }
    }

    private string? Single(string text)
    {
        if (Key(text) is { } key && byKey.TryGetValue(key, out var found)) return found.Turkish;
        return SpeakerPrefix.Parse(text) is { } prefix && Key(prefix.Body) is { } bare && byKey.TryGetValue(bare, out var named) ? named.Turkish : null;
    }

    /// <summary>Anahtar: paket araması ile aynı sadeleştirme; harf/rakam yoksa null.</summary>
    public static string? Key(string english) => PackKey.Canon(english) is { Length: > 0 } canon ? canon : null;

    private static (string English, string Turkish) Clean(string english, string turkish)
    {
        var source = Collapse(english);
        var target = string.Join("\n", turkish.Replace("\r\n", "\n").Split('\n').Select(Collapse).Where(line => line.Length > 0));
        if (SpeakerPrefix.Parse(source) is { } prefix && !source.Contains('\n'))
        {
            source = prefix.Body;
            if (SpeakerPrefix.Parse(target) is { } named && named.Name == prefix.Name) target = named.Body;
        }
        return (Clip(source), Clip(target));
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Clip(string text) => text.Length > MaxLength ? text[..MaxLength] : text;

    // --- Dosya ---

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Stored(int Version, List<Correction> Corrections);

    /// <summary>Dosyaya yazar (önce geçici dosyaya, sonra yerine koyarak; yarım dosya kalmaz). Yazılamazsa sessizce geçer.</summary>
    private void Save()
    {
        if (file is null) return;
        string json;
        lock (gate) json = JsonSerializer.Serialize(new Stored(1, [.. byKey.Values.OrderBy(entry => entry.English, StringComparer.Ordinal)]), Json);
        AtomicFile.Write(file, json);
    }
}

/// <summary>Küçük JSON dosyalarının yarım kalmadan yazılması: geçici dosya, sonra yerine taşıma.</summary>
public static class AtomicFile
{
    private static readonly object Gate = new();

    /// <returns>Yazıldıysa true; klasör ya da dosya yazılamazsa false.</returns>
    public static bool Write(string path, string text)
    {
        lock (Gate)
        {
            try
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }
    }
}
