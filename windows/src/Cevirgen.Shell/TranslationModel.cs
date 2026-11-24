// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>Kör testin bir sütunu: motor ve kullanılabilir mi (kör teste yalnız hazır motorlar girer; Ready hep true).</summary>
public sealed record BlindTestColumn(TranslationEngine Engine, bool Ready);

/// <summary>Bir motorun kör test çevirileri: satır başına çeviri (çevrilemeyen satır null), ortalama süre ve hata.</summary>
public sealed record BlindTestResult(IReadOnlyList<string?> Lines, int? AverageMs, string? Error = null)
{
    public bool Failed => Error is not null || Lines.All(line => line is null);
}

/// <summary>
/// Çevirgen: Çeviri ekranının "Kör test"i (taslak: Translation.dc.html). Beş zor replik en çok üç motora çevrilir; motorların
/// adı "Motor A/B/C" olarak gizlenir ve sütunların sırası karışıktır (ücretsiz model hep ilk sütunda olsa test kör olmazdı).
/// Kullanıcı her satırda bir çeviri seçer; seçimler motor başına sayılır, en çok seçilen motor "Bu motoru kullan" ile
/// uygulanır. Arayüzden bağımsızdır; test edilir.
/// </summary>
public static class BlindTest
{
    /// <summary>
    /// Beş zor replik: argo, ağız, yarım cümle, deyim ve hitap. Çevirgen için yazılmış özgün cümlelerdir; hiçbir oyundan
    /// alınmadı (ekran görüntülerinde ve tanıtımda da bunlar görünür).
    /// </summary>
    public static readonly IReadOnlyList<string> Lines =
    [
        "Dude, that was way too close.",
        "Ain't nobody gonna fix this but us.",
        "Almost there, just hang on.",
        "Let's not jump the gun here.",
        "Good to have you back in town, buddy.",
    ];

    /// <summary>En çok bu kadar motor karşılaştırılır.</summary>
    public const int MaxColumns = 3;

    /// <summary>Hazır motorların sırası (seçili motor her zaman önce gelir).</summary>
    private static readonly TranslationEngine[] Preferred =
        [TranslationEngine.Gemini, TranslationEngine.DeepL, TranslationEngine.OpenAi, TranslationEngine.Claude, TranslationEngine.Custom, TranslationEngine.Local];

    /// <summary>
    /// Sütunlar (henüz karıştırılmamış): yalnız hazır motorlar. Ücretsiz yerel model indiyse ilk; sonra seçili motor, sonra
    /// Gemini, DeepL, OpenAI, Claude, özel uç nokta, yerel sunucu; en çok üç. Hazır olmayan motor sütun olmaz (eskiden bütün
    /// satırları "çevrilemedi" olan sütunlar çıkıyordu); onların kurulumu motor listesindedir.
    /// </summary>
    public static IReadOnlyList<BlindTestColumn> Columns(TranslationEngine selected, Func<TranslationEngine, bool> ready, bool freeReady = true)
    {
        var columns = new List<BlindTestColumn>();
        if (freeReady) columns.Add(new BlindTestColumn(TranslationEngine.Free, true));
        IEnumerable<TranslationEngine> order = [selected, .. Preferred];
        foreach (var engine in order.Distinct())
        {
            if (columns.Count == MaxColumns) break;
            if (engine != TranslationEngine.Free && ready(engine)) columns.Add(new BlindTestColumn(engine, true));
        }
        return columns;
    }

    /// <summary>Karşılaştırma için en az iki hazır motor gerekir.</summary>
    public static bool CanCompare(IReadOnlyList<BlindTestColumn> columns) => columns.Count >= 2;

    /// <summary>Sütunların sırası karıştırılır (sınama için tohumlu rastgele).</summary>
    public static IReadOnlyList<BlindTestColumn> Shuffle(IReadOnlyList<BlindTestColumn> columns, Random random)
    {
        var list = columns.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>"Motor A", "Motor B", "Motor C".</summary>
    public static string HiddenName(int index) => "Motor " + (char)('A' + index);

    /// <summary>Motorun ekrandaki adı (açıldığında).</summary>
    public static string EngineName(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Free => "Ücretsiz yerel model",
        TranslationEngine.Gemini => "Google Gemini",
        TranslationEngine.DeepL => "DeepL",
        TranslationEngine.OpenAi => "OpenAI",
        TranslationEngine.Claude => "Anthropic Claude",
        TranslationEngine.Custom => "Özel (OpenAI uyumlu)",
        _ => "Yerel yapay zekâ sunucusu",
    };

    /// <summary>Motorun ücreti ve nerede çalıştığı (açıldığında "Ücret · hız" satırında, ölçülen süreyle).</summary>
    public static string Facts(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Free => "ücretsiz · çevrimdışı",
        TranslationEngine.Gemini => "ücretsiz kota",
        TranslationEngine.DeepL => "ayda 500.000 karakter ücretsiz",
        TranslationEngine.OpenAi => "ücretli · kendi anahtarın",
        TranslationEngine.Claude => "ücretli · kendi anahtarın",
        TranslationEngine.Custom => "OpenRouter, Groq, Together, LM Studio …",
        _ => "ücretsiz · kendi bilgisayarında",
    };

    /// <summary>"ücretsiz kota · ~250 ms" (süre ölçülmediyse yalnız ücret).</summary>
    public static string FactsWithSpeed(TranslationEngine engine, int? averageMs) =>
        averageMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{Facts(engine)} · ~{ms} ms") : Facts(engine);
}

/// <summary>Kör testte satır başına seçimler: satır → sütun. Sayımlar ve kazanan buradan.</summary>
public sealed class BlindTestVotes
{
    private readonly Dictionary<int, int> picks = [];

    /// <summary>Satırın seçimi (aynı hücre yeniden seçilirse seçim kalkar).</summary>
    public void Pick(int row, int column)
    {
        if (picks.TryGetValue(row, out var current) && current == column) picks.Remove(row);
        else picks[row] = column;
    }

    public int? Picked(int row) => picks.TryGetValue(row, out var column) ? column : null;

    public int Votes(int column) => picks.Values.Count(value => value == column);

    public int Count => picks.Count;

    /// <summary>En çok seçilen sütun; seçim yoksa ya da en çok seçilenler berabereyse null.</summary>
    public int? Winner
    {
        get
        {
            if (picks.Count == 0) return null;
            var groups = picks.Values.GroupBy(value => value).Select(group => (Column: group.Key, Votes: group.Count()))
                .OrderByDescending(item => item.Votes).ToList();
            return groups.Count > 1 && groups[0].Votes == groups[1].Votes ? null : groups[0].Column;
        }
    }

    /// <summary>"2 seçim".</summary>
    public string VotesText(int column) => string.Create(CultureInfo.InvariantCulture, $"{Votes(column)} seçim");

    public void Clear() => picks.Clear();
}

/// <summary>
/// Çeviri hızı hazır ayarları (Çeviri › İleri): Hızlı (kötü okunan satır beklenmez), Dengeli (önerilen bekleme süreleri),
/// Doğru (cümle tamamlanana dek daha uzun beklenir). Gelişmiş'teki iki bekleme süresinin hazır değerleridir; onlar elle
/// değişince seçim kalkar.
/// </summary>
public static class TranslationPaces
{
    public sealed record Pace(string Id, string Name, int MisreadWaitMs, int PartialWaitMs, string Hint);

    public static readonly IReadOnlyList<Pace> All =
    [
        new("Fast", "Hızlı", 400, 0, "Altyazı olabildiğince hızlı gelir; nadiren yarım cümle görebilirsin."),
        new("Balanced", "Dengeli", 800, 300, "Çoğu oyun için önerilir: hız ve doğruluk dengeli."),
        new("Accurate", "Doğru", 1000, 800, "Cümle tamamlanınca çevrilir; yarım saniye kadar geç gelir."),
    ];

    /// <summary>Ayarlardaki bekleme sürelerine uyan hazır ayar; yoksa null.</summary>
    public static Pace? Match(UserSettings settings) =>
        All.FirstOrDefault(pace => pace.MisreadWaitMs == settings.MisreadWaitMs && pace.PartialWaitMs == settings.PartialWaitMs);

    public static UserSettings Apply(UserSettings settings, Pace pace) =>
        settings with { MisreadWaitMs = pace.MisreadWaitMs, PartialWaitMs = pace.PartialWaitMs };
}
