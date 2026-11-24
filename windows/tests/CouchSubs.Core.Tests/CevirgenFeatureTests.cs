// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

/// <summary>Çevirgen: kullanıcı düzeltmeleri, kullanıcı sözlüğü, okuma hızı ve SRT.</summary>
public sealed class CevirgenFeatureTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    // --- Düzeltmeler ---

    [Fact]
    public void CorrectionMatchesLikePackKeys()
    {
        var corrections = new UserCorrections();
        Assert.True(corrections.Set("Can never have too much storage space, am I right?", "Depo hiç fazla gelmez, değil mi?"));
        // Büyük/küçük harf, noktalama ve boşluk farkı aynı satırdır (paket anahtarıyla aynı sadeleştirme).
        Assert.Equal("Depo hiç fazla gelmez, değil mi?", corrections.Find("can never have too much storage space am I right"));
        Assert.Null(corrections.Find("Can never have too much storage space."));
        // Konuşan adı: adsız cümleyle bulunur, ad eklenmez (hat gösterirken ekler).
        Assert.Equal("Depo hiç fazla gelmez, değil mi?", corrections.Find("JASON: Can never have too much storage space, am I right?"));
    }

    [Fact]
    public void SpeakerNameIsStrippedWhenSaving()
    {
        var corrections = new UserCorrections();
        corrections.Set("LUCIA: Get in the car.", "LUCIA: Arabaya bin.");
        var saved = Assert.Single(corrections.All);
        Assert.Equal(("Get in the car.", "Arabaya bin."), (saved.English, saved.Turkish));
        Assert.Equal("Arabaya bin.", corrections.Find("Get in the car."));
        Assert.Same(saved, corrections.Get("LUCIA: Get in the car."));
        Assert.True(corrections.Remove("LUCIA: Get in the car."));
        Assert.Empty(corrections.All);
        Assert.False(corrections.Remove("Get in the car."));
    }

    [Fact]
    public void MultiLineReadingUsesEachLinesCorrection()
    {
        var corrections = new UserCorrections();
        corrections.Set("Get in the car.", "Arabaya bin.");
        corrections.Set("We are late.", "Geç kaldık.");
        Assert.Equal("Arabaya bin.\nJASON: Geç kaldık.", corrections.Find("Get in the car.\nJASON: We are late."));
        // Bir satırın düzeltmesi yoksa okuma düzeltilmiş sayılmaz.
        Assert.Null(corrections.Find("Get in the car.\nWho are you?"));
    }

    [Fact]
    public void InvalidCorrectionsAreRejected()
    {
        var corrections = new UserCorrections();
        Assert.False(corrections.Set("...", "Bir şey"));
        Assert.False(corrections.Set("Hello", "   "));
        Assert.Equal(0, corrections.Count);
    }

    [Fact]
    public void CorrectionsAreSavedAndLoaded()
    {
        var file = Path.Combine(folder, UserCorrections.FileName);
        var corrections = UserCorrections.Load(file);
        var changes = 0;
        corrections.Changed += () => changes++;
        corrections.Set("Hold the gate.", "Kapıyı tut.");
        corrections.Set("Hold the gate!", "Kapıyı tutun!");
        Assert.Equal(2, changes);
        Assert.False(File.Exists(file + ".tmp"));
        var reopened = UserCorrections.Load(file);
        var only = Assert.Single(reopened.All);
        Assert.Equal(("Hold the gate!", "Kapıyı tutun!"), (only.English, only.Turkish));
        Assert.Contains("Kapıyı tutun!", File.ReadAllText(file));
        // Bozuk dosya boş açılır.
        File.WriteAllText(file, "{bozuk");
        Assert.Empty(UserCorrections.Load(file).All);
    }

    [Fact]
    public async Task CorrectionWinsOverPackAndEngine()
    {
        long clock = 0;
        var translator = new FakeTranslator();
        var sender = new FakeSender();
        var reader = new FakeReader(Enumerable.Repeat("JASON: Hold the gate.", 6).ToArray());
        var corrections = new UserCorrections();
        corrections.Set("Hold the gate.", "Kapıyı sen tut.");
        var logs = new List<string>();
        var metrics = new List<LineMetrics>();
        var pipeline = new SubtitlePipeline(reader, translator, sender, clock: () => clock)
        {
            PackLookup = _ => new PackMatch("Paketin çevirisi.", []),
            CorrectionLookup = corrections.Find,
            Log = logs.Add,
        };
        pipeline.MetricsReady += metrics.Add;
        for (var i = 0; i < 6; i++)
        {
            await pipeline.ProcessAsync(Frame.Blank(64, 64), new CropRegion());
            await pipeline.WaitForTranslationsAsync();
            clock += 200;
        }
        Assert.Equal("JASON: Kapıyı sen tut.", Assert.Single(sender.Sent));
        Assert.Empty(translator.Requested);
        Assert.Equal("senin", pipeline.LastOrigin);
        Assert.Equal(0, pipeline.PackHits);
        Assert.Equal(LineOrigin.Correction, Assert.Single(metrics).Origin);
        Assert.Contains(logs, line => line.EndsWith("→ senin", StringComparison.Ordinal));
    }

    // --- Kullanıcı sözlüğü ---

    [Fact]
    public void GlossaryRulesBecomeTermsAndAiInstruction()
    {
        var glossary = new UserGlossary();
        Assert.Null(glossary.AsGlossary());
        Assert.Null(glossary.AiInstruction());
        Assert.Equal("", glossary.CombinedInstruction(""));
        Assert.True(glossary.Set("Vice City", GlossaryRule.Keep, "Yardımcı Şehir"));
        Assert.True(glossary.Set("heist", GlossaryRule.Always, "soygun"));
        Assert.False(glossary.Set("score", GlossaryRule.Always, " "));
        Assert.False(glossary.Set("123", GlossaryRule.Keep));

        var terms = glossary.Terms;
        Assert.Equal(["heist", "Vice City"], terms.Select(term => term.English));
        Assert.Null(terms[1].Turkish);
        Assert.Equal(["hep böyle", "çevirme"], terms.Select(term => term.RuleLabel));

        var built = glossary.AsGlossary()!;
        Assert.Same(built, glossary.AsGlossary());
        Assert.Equal("Vice City", built.Turkish("vice city"));
        Assert.Equal("soygun", built.Turkish("Heist"));

        var instruction = glossary.AiInstruction()!;
        Assert.Contains("Keep \"Vice City\" in English", instruction);
        Assert.Contains("Always translate \"heist\" as \"soygun\"", instruction);
        Assert.StartsWith("Be blunt.\n\nGlossary", glossary.CombinedInstruction("Be blunt."));

        // Aynı terim (harf ve boşluk farkı) güncellenir; silinince sözlük yeni örnek olur.
        glossary.Set("HEIST", GlossaryRule.Keep);
        Assert.Equal(2, glossary.Count);
        Assert.NotSame(built, glossary.AsGlossary());
        Assert.True(glossary.Remove("Vice  City"));
        Assert.Single(glossary.Terms);
    }

    [Fact]
    public void GlossaryIsSavedAndLoaded()
    {
        var file = Path.Combine(folder, UserGlossary.FileName);
        var glossary = UserGlossary.Load(file);
        glossary.Set("Leonida", GlossaryRule.Keep);
        glossary.Set("cops", GlossaryRule.Always, "aynasızlar");
        var text = File.ReadAllText(file);
        Assert.Contains("\"keep\"", text);
        Assert.Contains("\"always\"", text);
        var reopened = UserGlossary.Load(file);
        Assert.Equal(glossary.Terms.Select(term => (term.English, term.Rule, term.Turkish)), reopened.Terms.Select(term => (term.English, term.Rule, term.Turkish)));
    }

    [Fact]
    public void FreeEngineProtectsUserTermsThroughGlossaryStore()
    {
        var glossary = new UserGlossary();
        // Uygulama GlossaryStore.UserTerms'i verir; test başka testleri etkilemesin diye örneğe verir.
        var store = new GlossaryStore(null, userTerms: glossary.AsGlossary);
        var before = store.Current;
        Assert.Null(before.Turkish("Leonida Keys"));
        glossary.Set("Leonida Keys", GlossaryRule.Keep);
        glossary.Set("score", GlossaryRule.Always, "vurgun");
        // Kullanıcı sözlüğü değişince beklemeden yenilenir, genel sözlüğün üstüne eklenir.
        var current = store.Current;
        Assert.Equal("Leonida Keys", current.Turkish("leonida keys"));
        Assert.Equal("vurgun", current.Turkish("score"));
        Assert.NotEqual(before.Fingerprint, current.Fingerprint);
        Assert.True(current.Count > glossary.Count);
        // Terim koruması (ücretsiz motor) terimi yer tutucuyla korur, çeviriden sonra Türkçesini koyar.
        var protector = new TermProtector(current, "We hit the score in Leonida Keys.");
        var (text, slots) = protector.Protect("We hit the score in Leonida Keys.");
        Assert.DoesNotContain("score", text);
        Assert.DoesNotContain("Leonida", text);
        Assert.Equal(["vurgun", "Leonida Keys"], slots.Select(slot => slot.Turkish));
        Assert.Equal("Vurgun Leonida Keys yaptık.", TermProtector.Restore($"{slots[0].Placeholder} {slots[1].Placeholder} yaptık.", slots));
    }

    // --- Okuma hızı ve geçmiş ---

    [Fact]
    public void ReadingSpeedCountsCharactersWithoutLineBreaks()
    {
        Assert.Equal(11, ReadingSpeed.Characters(" Merhaba!\nSen "));
        Assert.Equal(17.0, ReadingSpeed.Cps(new string('a', 34), 2000));
        Assert.Null(ReadingSpeed.Cps("Kısa", 50));
        Assert.Null(ReadingSpeed.Cps("", 2000));
    }

    [Fact]
    public void HistoryKeepsShownAndLeftTimesWithMilliseconds()
    {
        var history = new SubtitleHistory();
        var shown = new DateTime(2026, 11, 24, 20, 0, 0, 125, DateTimeKind.Utc);
        history.Add("Hold the gate.", "Kapıyı tut, hemen şimdi!", LineOrigin.Correction, shown);
        // Aynı satırın yeniden okuması ekrana geliş anını değiştirmez.
        history.Add("Hold the gate", "Kapıyı tut, hemen şimdi!", LineOrigin.Correction, shown.AddMilliseconds(400));
        Assert.True(history.MarkLeft("Hold the gate.", 1000));
        Assert.False(history.MarkLeft("Hold the gate.", 3000));
        var entry = Assert.Single(history.Entries);
        Assert.Equal(shown, entry.ShownAt);
        Assert.Equal(shown.AddSeconds(1), entry.LeftAt);
        Assert.Equal(24.0, entry.Cps);
        Assert.True(entry.IsFast);
        Assert.True(entry.IsCorrection);

        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "history.json");
        history.Save(file);
        Assert.Contains("\"shownAt\":\"2026-11-24T20:00:00.125Z\"", File.ReadAllText(file));
        var loaded = Assert.Single(SubtitleHistory.Load(file).Entries);
        Assert.Equal((entry.ShownAt, entry.LeftAt, entry.Cps, LineOrigin.Correction), (loaded.ShownAt, loaded.LeftAt, loaded.Cps, loaded.Origin));
    }

    [Fact]
    public void OldHistoryFilesStillLoad()
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "history.json");
        File.WriteAllText(file, """{"version":1,"entries":[{"id":3,"date":"2026-11-12T10:00:00Z","source":"Go.","translation":"Git.","origin":"pack"}]}""");
        var entry = Assert.Single(SubtitleHistory.Load(file).Entries);
        Assert.Null(entry.ShownAt);
        Assert.Null(entry.Cps);
        Assert.False(entry.IsFast);
        Assert.Equal(entry.Date, entry.Shown);
    }

    [Fact]
    public void SessionStatsSummarizeLines()
    {
        var history = new SubtitleHistory();
        var at = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
        history.Add("One.", "Birinci satır burada.", LineOrigin.Pack, at);
        history.MarkLeft("One.", 2000);
        history.Add("Two two.", "İki.", LineOrigin.Correction, at.AddSeconds(3));
        history.MarkLeft("Two two.", 400);
        history.Add("Three three three.", "Üç.", LineOrigin.Engine, at.AddSeconds(5));
        var stats = SessionStats.From(history.Entries, [300, 500]);
        Assert.Equal(new SessionStats(3, 1, 1, Math.Round((10.5 + 10.0) / 2, 1, MidpointRounding.AwayFromZero), 0, 400), stats);
        Assert.Equal(SessionStats.Empty, SessionStats.From([], []));
    }

    // --- SRT ---

    [Theory]
    [InlineData(0, "00:00:00,000")]
    [InlineData(61_001, "00:01:01,001")]
    [InlineData(3_723_045, "01:02:03,045")]
    [InlineData(363_599_999, "100:59:59,999")]
    public void SrtTimeFormat(long ms, string expected) => Assert.Equal(expected, SrtExport.Time(ms));

    private static SubtitleHistory.Entry Line(int id, string source, string translation, DateTime shown, DateTime? left) =>
        new(id, shown, source, translation, LineOrigin.Engine) { ShownAt = shown, LeftAt = left };

    [Fact]
    public void SrtUsesRealTimesRelativeToSessionStart()
    {
        var start = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
        SubtitleHistory.Entry[] entries =
        [
            // Sıra karışık verilse de ekrana geliş sırasıyla yazılır.
            Line(2, "LUCIA: Get in.", "LUCIA: Bin.", start.AddHours(1).AddSeconds(2.5), start.AddHours(1).AddSeconds(4)),
            Line(1, "Hold the gate.\nNow!", "Kapıyı tut.\nHemen!", start.AddSeconds(1.25), start.AddSeconds(3.5)),
        ];
        var turkish = SrtExport.Build(entries, SrtExport.Kind.Turkish, start);
        Assert.Equal(
            "1\r\n00:00:01,250 --> 00:00:03,500\r\nKapıyı tut.\r\nHemen!\r\n\r\n" +
            "2\r\n01:00:02,500 --> 01:00:04,000\r\nLUCIA: Bin.\r\n\r\n", turkish);
        var bilingual = SrtExport.Build(entries, SrtExport.Kind.Bilingual, start);
        Assert.StartsWith("1\r\n00:00:01,250 --> 00:00:03,500\r\nHold the gate.\r\nNow!\r\nKapıyı tut.\r\nHemen!\r\n\r\n2\r\n", bilingual);
        Assert.EndsWith("LUCIA: Get in.\r\nLUCIA: Bin.\r\n\r\n", bilingual);
    }

    [Fact]
    public void SrtFillsUnknownEndsWithoutOverlapping()
    {
        var start = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
        SubtitleHistory.Entry[] entries =
        [
            Line(1, "A.", "Kısa.", start, null),                                  // kalkışı bilinmiyor: en az 1 sn
            Line(2, "B.", "Uzun bir Türkçe satır.", start.AddSeconds(5), start.AddSeconds(9)), // sonraki 6 sn'de başlıyor
            Line(3, "C.", "Son.", start.AddSeconds(6), start.AddSeconds(6)),      // sıfır süre: en az 100 ms
        ];
        var srt = SrtExport.Build(entries, SrtExport.Kind.Turkish);
        Assert.Contains("00:00:00,000 --> 00:00:01,000", srt);
        Assert.Contains("00:00:05,000 --> 00:00:06,000", srt);
        Assert.Contains("00:00:06,000 --> 00:00:06,100", srt);
        Assert.Equal("", SrtExport.Build([], SrtExport.Kind.Turkish));
    }

    [Fact]
    public void SrtFileIsUtf8()
    {
        var path = Path.Combine(folder, "alt", "oturum.srt");
        var start = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
        var count = SrtExport.Write(path, [Line(1, "Go.", "Gidelim, çabuk!", start, start.AddSeconds(2))], SrtExport.Kind.Turkish, start);
        Assert.Equal(1, count);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("Gidelim, çabuk!", System.Text.Encoding.UTF8.GetString(bytes));
    }
}
