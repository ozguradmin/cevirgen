// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cevirgen.Core.Tests;

/// <summary>Sunucunun yerine geçen sahte HTTP: gelen istekleri saklar, sıradaki yanıtı döner (null: ulaşılamadı).</summary>
internal sealed class FakePoolServer : HttpMessageHandler
{
    private readonly object gate = new();
    public List<(HttpMethod Method, Uri Url, string Body)> Received { get; } = [];
    public Queue<Func<HttpResponseMessage>?> Replies { get; } = new();
    public Func<HttpResponseMessage>? Fallback { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

    public List<List<JsonElement>> Batches
    {
        get
        {
            lock (gate)
                return [.. Received.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("lines").EnumerateArray().ToList())];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpResponseMessage>? reply;
        lock (gate)
        {
            Received.Add((request.Method, request.RequestUri!, body));
            reply = Replies.Count > 0 ? Replies.Dequeue() : Fallback;
        }
        return reply is null ? throw new HttpRequestException("ulaşılamadı") : reply();
    }
}

internal sealed class PoolClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_793_000_000);
}

public class LinePoolTests
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "pool-" + Guid.NewGuid(), "lines-queue.json");
    private readonly FakePoolServer server = new();
    private readonly PoolClock clock = new();
    private static readonly Uri Primary = new("https://primary.test/");
    private static readonly Uri Fallback = new("https://fallback.test/");

    private LinePool MakePool(bool enabled = true) => new(file, "1.2.3", [Primary], enabled, server, () => clock.Now);

    private static void Offer(LinePool pool, string en, string tr = "Türkçesi burada.", string engine = "free",
                              LinePool.Area area = LinePool.Area.Subtitle) => pool.Offer(en, tr, engine, area);

    [Fact]
    public async Task DisabledPoolSendsNothing()
    {
        Assert.True(LinePool.EnabledByDefault); // Çevirgen: "Çevirileri iyileştirmeye katkı" varsayılan açık
        var pool = MakePool(enabled: false);
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        Assert.Empty(pool.Queued);
        Assert.Empty(server.Received);
    }

    /// <summary>Gönderilen gövde sunucunun sözleşmesiyle aynı: id, v, game, lines[en, tr, engine, area, at, n]; adres /v1/lines.</summary>
    [Fact]
    public async Task PayloadMatchesTheServerContract()
    {
        var pool = MakePool();
        pool.SetGame(LinePool.GameRef.Create("gta-6", "  Grand Theft Auto VI\n"));
        Offer(pool, "Get in the car, Lucia.", "Arabaya bin, Lucia.", "gemini:gemini-2.5-flash", LinePool.Area.Extra);
        await pool.FlushAsync();
        Assert.Equal(new Uri("https://primary.test/v1/lines"), server.Received[0].Url);
        Assert.Equal(HttpMethod.Post, server.Received[0].Method);
        var body = JsonDocument.Parse(server.Received[0].Body).RootElement;
        Assert.Equal(["game", "id", "lines", "v"], body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("1.2.3", body.GetProperty("v").GetString());
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", body.GetProperty("id").GetString()!);
        Assert.Equal("gta-6", body.GetProperty("game").GetProperty("id").GetString());
        Assert.Equal("Grand Theft Auto VI", body.GetProperty("game").GetProperty("title").GetString());
        var line = server.Batches[0][0];
        Assert.Equal(["area", "at", "en", "engine", "n", "tr"], line.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("Get in the car, Lucia.", line.GetProperty("en").GetString());
        Assert.Equal("Arabaya bin, Lucia.", line.GetProperty("tr").GetString());
        Assert.Equal("gemini:gemini-2.5-flash", line.GetProperty("engine").GetString());
        Assert.Equal("extra", line.GetProperty("area").GetString());
        Assert.Equal("2026-10-26T07:33:20Z", line.GetProperty("at").GetString());
        Assert.Equal(1, line.GetProperty("n").GetInt32());
    }

    /// <summary>Bir istek bir oyunun satırlarını taşır; oyun bilinmiyorsa game null gider.</summary>
    [Fact]
    public async Task LinesAreGroupedByGame()
    {
        var pool = MakePool();
        Offer(pool, "Who is that over there?");
        pool.SetGame(LinePool.GameRef.Create(null, "Red Dead Redemption 2"));
        Offer(pool, "Get in the car, Lucia.");
        pool.SetGame(null);
        Offer(pool, "Keep your head down, Jason.");
        await pool.FlushAsync();
        Assert.Equal(2, server.Received.Count);
        var first = JsonDocument.Parse(server.Received[0].Body).RootElement;
        Assert.Equal(JsonValueKind.Null, first.GetProperty("game").ValueKind);
        Assert.Equal(["Who is that over there?", "Keep your head down, Jason."], server.Batches[0].Select(l => l.GetProperty("en").GetString()));
        var second = JsonDocument.Parse(server.Received[1].Body).RootElement;
        Assert.False(second.GetProperty("game").TryGetProperty("id", out _));
        Assert.Equal("Red Dead Redemption 2", second.GetProperty("game").GetProperty("title").GetString());
        Assert.Empty(pool.Queued);
    }

    [Fact]
    public void GameRefDropsInvalidIdsAndEmptyNames()
    {
        Assert.Null(LinePool.GameRef.Create(null, " \0 "));
        Assert.Equal(new LinePool.GameRef(null, "GTA 6"), LinePool.GameRef.Create("GTA 6!", "GTA 6"));
        Assert.Equal(new LinePool.GameRef("rdr-2", null), LinePool.GameRef.Create("rdr-2", null));
    }

    [Fact]
    public void QueuedLinesSurviveRestart()
    {
        server.Fallback = null;
        var pool = MakePool();
        pool.SetGame(LinePool.GameRef.Create("gta-6", "Grand Theft Auto VI"));
        Offer(pool, "Get in the car, Lucia.", "Arabaya bin, Lucia.");
        pool.Save();
        var reopened = MakePool();
        var line = Assert.Single(reopened.Queued);
        Assert.Equal("Arabaya bin, Lucia.", line.Tr);
        Assert.Equal("gta-6", line.Game?.Id);
        Assert.Equal(pool.InstallId, reopened.InstallId);
    }

    /// <summary>Aynı satır kuyrukta beklerken yeniden görülünce sayılır ve son çeviri kalır; başka motorun çevirisi ayrı satırdır.</summary>
    [Fact]
    public void RepeatsAreCountedAndEnginesKeptApart()
    {
        var pool = MakePool();
        Offer(pool, "  Don Torrisi wants you.  ", "Don Torrisi seni istiyor.");
        Offer(pool, "DON TORRISI WANTS YOU!", "Don Torrisi seni arıyor.");
        Offer(pool, "Don Torrisi wants you.", "Don Torrisi seni istiyor.", "deepl");
        Offer(pool, "Don Torrisi wants you.", "Don Torrisi seni istiyor!", LinePool.UserEngine);
        Assert.Equal(3, pool.Queued.Count);
        Assert.Equal(2, pool.Queued[0].N);
        Assert.Equal("Don Torrisi seni arıyor.", pool.Queued[0].Tr);
        Assert.Equal(["free", "deepl", "user"], pool.Queued.Select(l => l.Engine));
        Assert.Equal(PackKey.Key("Don Torrisi wants you."), pool.Queued[0].Key);
        for (var i = 0; i < 100; i++) Offer(pool, "Don Torrisi wants you.");
        Assert.Equal(LinePool.MaxSightings, pool.Queued[0].N);
    }

    [Fact]
    public void TranslationMustBeUsable()
    {
        var pool = MakePool();
        Offer(pool, "Who is that over there?", "");
        Offer(pool, "Who is that over there?", "Who is that over there?");
        Offer(pool, "Who is that over there?", "Bana yaz: deniz@ornek.com");
        Offer(pool, "Who is that over there?", new string('a', LinePool.MaxTranslation + 1));
        Offer(pool, "Who is that over there?", "Oradaki kim?", "");
        Assert.Empty(pool.Queued);
        Offer(pool, "Who is that over there?", "Oradaki   kim?\n", "Gemini: Flash 2.5!");
        var line = Assert.Single(pool.Queued);
        Assert.Equal("Oradaki kim?", line.Tr);
        Assert.Equal("gemini:flash2.5", line.Engine);
    }

    /// <summary>Kişisel bilgiye benzeyen satır kuyruğa hiç girmez (sunucudaki lines.js ile aynı örnekler).</summary>
    [Theory]
    [InlineData("Contact me at foo@bar.com today.")]
    [InlineData("Call 555-123-4567 now!")]
    [InlineData("Call me on +90 (532) 123 45 67, okay?")]
    [InlineData("Add xX_Gamer_99 as a friend.")]
    [InlineData("Player1234 joined the session.")]
    [InlineData("Follow @lucia_vice for more.")]
    [InlineData("Visit www.example.com for details.")]
    [InlineData("Check rockstargames.com tonight.")]
    [InlineData("Your order #12345 has shipped.")]
    public void RejectsPersonalData(string line)
    {
        Assert.True(PersonalText.Looks(line), line);
        Assert.False(LinePool.Accepts(line), line);
    }

    [Theory]
    [InlineData("Where is the money, Jason?")]
    [InlineData("Back in the 1990s we ran this town.")]
    [InlineData("That's 500k in cash, man!")]
    [InlineData("Give me 50 dollars, Enzo.")]
    public void KeepsOrdinaryLinesWithNumbers(string line)
    {
        Assert.False(PersonalText.Looks(line), line);
        Assert.True(LinePool.Accepts(line), line);
    }

    /// <summary>Saat, kalibre ve ölçü kişisel bilgi sayılmaz (yazı tanıma çöpü süzgeci bunları ayrıca eleyebilir).</summary>
    [Theory]
    [InlineData("Meet me at 10pm by the docks.")]
    [InlineData("Bring the 9mm and the 4x4.")]
    [InlineData("It's the 3rd time this week.")]
    public void OrdinaryMixedWordsAreNotPersonal(string line) => Assert.False(PersonalText.Looks(line), line);

    /// <summary>PS5'in Uzaktan Oynatma bildirimi ortak havuza gitmez.</summary>
    [Theory]
    [InlineData("You have connected to Remote Play.")]
    [InlineData("Remote Play: Connected to the PS5.")]
    public void RejectsTheRemotePlayNotice(string line) => Assert.False(LinePool.Accepts(line), line);

    [Fact]
    public async Task BatchesAreAtMostOneHundredLines()
    {
        var pool = MakePool();
        for (var i = 0; i < 250; i++) Offer(pool, $"This is line number {i} of the test");
        await pool.FlushAsync();
        Assert.Equal([100, 100, 50], server.Batches.Select(b => b.Count));
        Assert.Empty(pool.Queued);
        // Gönderilen satır aynı gün yeniden kuyruğa girmez; ertesi gün bir kez daha gider (görülme sayısı için).
        Offer(pool, "This is line number 3 of the test");
        Assert.Empty(pool.Queued);
        clock.Now += TimeSpan.FromDays(1);
        Offer(pool, "This is line number 3 of the test");
        Assert.Single(pool.Queued);
    }

    [Fact]
    public async Task QueueSurvivesUnreachableServer()
    {
        server.Fallback = null;
        var pool = MakePool();
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        Assert.Single(server.Received);
        var reopened = MakePool();
        Assert.Equal(["Don Torrisi wants you."], reopened.Queued.Select(l => l.En));
        server.Fallback = () => new HttpResponseMessage(HttpStatusCode.OK);
        await reopened.FlushAsync();
        Assert.Empty(reopened.Queued);
    }

    /// <summary>Asıl adres ağ hatası ya da 5xx verirse yedek adres denenir (CevirgenEndpoints).</summary>
    [Fact]
    public async Task FallsBackToTheSecondAddress()
    {
        server.Replies.Enqueue(null);
        var pool = new LinePool(file, "1.2.3", [Primary, Fallback], true, server, () => clock.Now);
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        Assert.Equal(["primary.test", "fallback.test"], server.Received.Select(r => r.Url.Host));
        Assert.Empty(pool.Queued);
    }

    [Fact]
    public async Task ServerErrorKeepsTheQueue()
    {
        server.Replies.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var pool = MakePool();
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        Assert.Single(pool.Queued);
        await pool.FlushAsync();
        Assert.Empty(pool.Queued);
    }

    [Fact]
    public async Task TooManyRequestsWaitsForRetryAfter()
    {
        server.Replies.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });
        var pool = MakePool();
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        await pool.FlushAsync();
        Assert.Single(server.Received);
        clock.Now += TimeSpan.FromSeconds(121);
        await pool.FlushAsync();
        Assert.Equal(2, server.Received.Count);
        Assert.Empty(pool.Queued);
    }

    [Fact]
    public async Task RejectedBatchIsNotSentAgainToday()
    {
        server.Replies.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var pool = MakePool();
        Offer(pool, "Don Torrisi wants you.");
        await pool.FlushAsync();
        Offer(pool, "Don Torrisi wants you.");
        Assert.Empty(pool.Queued);
        Assert.Single(server.Received);
    }

    [Fact]
    public void QueueKeepsTheNewest()
    {
        var pool = MakePool();
        for (var i = 0; i < LinePool.MaxQueue + 10; i++) Offer(pool, $"Line number {i} here");
        Assert.Equal(LinePool.MaxQueue, pool.Queued.Count);
        Assert.Equal("Line number 10 here", pool.Queued[0].En);
        // Sıradakiler hâlâ sayılır (dizin kaymadı).
        Offer(pool, "Line number 11 here");
        Assert.Equal(2, pool.Queued[1].N);
    }

    /// <summary>Kapatınca kuyruk ve dosyası silinir, hiçbir şey gitmez.</summary>
    [Fact]
    public async Task TurningOffDeletesTheQueue()
    {
        var pool = MakePool();
        Offer(pool, "Don Torrisi wants you.");
        pool.Save();
        Assert.True(File.Exists(file));
        pool.SetEnabled(false);
        Assert.False(File.Exists(file));
        Offer(pool, "Who is that over there?");
        await pool.FlushAsync();
        Assert.Empty(pool.Queued);
        Assert.Empty(server.Received);
        Assert.Empty(MakePool().Queued);
    }

    [Fact]
    public void FiltersSkipShortLongAndNoisyLines()
    {
        Assert.True(LinePool.Accepts("Get down!"));
        Assert.False(LinePool.Accepts("Okay."));
        Assert.False(LinePool.Accepts("..."));
        Assert.False(LinePool.Accepts(string.Concat(Enumerable.Repeat("word ", 61))));
        Assert.False(LinePool.Accepts("We need ou$3e now"));
        Assert.False(LinePool.Accepts("HP 100/100 MP 25/50"));
        Assert.True(LinePool.Accepts("Give me 50 dollars, Enzo."));
    }

    /// <summary>Havuzda en çok görülen "satırlar" PS5'in Türkçe arayüzü ve onun yazı tanıma bozmalarıydı; İngilizce oyun satırı değiller.</summary>
    [Theory]
    [InlineData("Oturum Açtı")]
    [InlineData("Oturum Aqtl")]
    [InlineData("Oturum Aqtl.")]
    [InlineData("Sifirlama Seqenekleri")]
    [InlineData("Sifirlama Seqenekleri.")]
    [InlineData("PS5 Sistemini Yeniden Baslat")]
    [InlineData("PS5 Sistemini Yeniden Baslat.")]
    [InlineData("Oyunu Kapat")]
    [InlineData("Ana Ekranda Tut")]
    [InlineData("Lütfen bekleyin..")]
    [InlineData("Ekran ve Video")]
    [InlineData("ilerlemenizi takip edin Kupalar")]
    [InlineData("Keşfet PS5 ile ilgili Öne Çıkanlar")]
    [InlineData("Ekran görüntüsü kaydedildi.")]
    [InlineData("Kupa kazandınız!")]
    [InlineData("John signed in.")]
    [InlineData("You've earned a trophy!")]
    [InlineData("Please do not quit or turn off your console while the Rockstar cloud save icon is showing.")]
    [InlineData("GO to the")]
    [InlineData("Take out the trash and the")]
    [InlineData("it Auto V's Story Mode.")]
    [InlineData("FREE MONTH OF GTA+")]
    [InlineData("PHOTO MODE SELECT")]
    [InlineData("Main menu")]
    [InlineData("Hold on")]
    public void RejectsTurkishSystemTextAndFragments(string line) => Assert.False(LinePool.Accepts(line), line);

    /// <summary>Yabancı addaki tek ü/ö/ç, sözcük içi eşleşmeler ve noktalamayla biten kısa satırlar İngilizce satırı düşürmez.</summary>
    [Theory]
    [InlineData("Herr Müller will see you now.")]
    [InlineData("This place was designed in a hurry.")]
    [InlineData("Tell Sam I signed the deal.")]
    [InlineData("I lie to you, you lie to me.")]
    [InlineData("DON TORRISI WANTS YOU!")]
    [InlineData("Get down!")]
    [InlineData("Who's there?")]
    [InlineData("Line number 7 here")]
    [InlineData("We need to move before the cops show up")]
    public void KeepsEnglishGameLines(string line) => Assert.True(LinePool.Accepts(line), line);

    [Fact]
    public void RetryAfterAcceptsSecondsAndDates()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_793_000_000);
        Assert.Equal(TimeSpan.FromSeconds(30), LinePool.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), now));
        Assert.Equal(TimeSpan.FromSeconds(60), LinePool.RetryDelay(null, now));
        Assert.Equal(TimeSpan.FromSeconds(120), LinePool.RetryDelay(RetryConditionHeaderValue.Parse("Mon, 26 Oct 2026 07:35:20 GMT"), now));
    }
}
