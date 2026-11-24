// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core.Tests;

/// <summary>
/// PS5'in "Uzaktan Oynatma'ya bağlandı" / "Connected to Remote Play" bildirimi (yazı tanıma bozmalarıyla): altyazı sayılmaz,
/// çevrilmez, ortak havuza gitmez; altyazı hattı görünce bildirir.
/// </summary>
public class RemotePlayNoticeTests
{
    [Theory]
    [InlineData("Uzaktan Oynatma'ya bağlandı")]
    [InlineData("Uzaktan Oynatma’ya bağlandı.")]
    [InlineData("UZAKTAN OYNATMA'YA BAĞLANDI")]
    [InlineData("Uzaktan Oynatma")]
    [InlineData("Uzaktan Oynama")]
    [InlineData("tan Oynama")]
    [InlineData("aktan Oynatma'ya bag")]
    [InlineData("Uzaktn Oynatmaya baglandi")]
    [InlineData("Uzaktan 0ynatma bağlantısı")]
    [InlineData("deniz_ps Uzaktan Oynatma'ya bağlandı")]
    [InlineData("Connected to Remote Play")]
    [InlineData("Remote Play: Connected")]
    [InlineData("Remote PIay connected")]
    [InlineData("Rem0te P1ay - Connected")]
    [InlineData("RemotePlay connected")]
    [InlineData("You have connected to Remote Play.")]
    public void MatchesTheNoticeAndItsOcrVariants(string text) => Assert.True(RemotePlayNotice.Matches(text), text);

    [Theory]
    [InlineData("")]
    [InlineData("Hold the gate until I come back.")]
    [InlineData("Play it again, Sam.")]
    [InlineData("Turn on the remote, we're watching the game.")]
    [InlineData("Remote Play")]
    [InlineData("We connected the dots.")]
    [InlineData("Remove the play button.")]
    [InlineData("Get a tan on the beach, Lucia.")]
    [InlineData("Oyna bakalım")]
    public void LeavesOtherTextAlone(string text) => Assert.False(RemotePlayNotice.Matches(text), text);

    [Fact]
    public void DropsOnlyTheNoticeLinesFromAReading()
    {
        var reading = new Reading([
            new TextLine("Uzaktan Oynatma'ya", new TextBox(0.4, 0.02, 0.6, 0.05)),
            new TextLine("bağlandı", new TextBox(0.45, 0.05, 0.55, 0.08)),
            new TextLine("Where is the money, Jason?", new TextBox(0.3, 0.8, 0.7, 0.86)),
        ]);
        var kept = RemotePlayNotice.Without(reading, out var found);
        Assert.True(found);
        Assert.Equal(["Where is the money, Jason?"], kept.Lines.Select(line => line.Text));

        // İngilizce biçim iki satıra bölünmüş.
        var split = new Reading([new TextLine("Connected to"), new TextLine("Remote Play"), new TextLine("Lucia, wait up!")]);
        Assert.Equal(["Lucia, wait up!"], RemotePlayNotice.Without(split, out found).Lines.Select(line => line.Text));
        Assert.True(found);

        // Bildirim yoksa okuma aynen döner; tek başına "Connected" satırı atılmaz.
        var plain = new Reading([new TextLine("Connected"), new TextLine("Lucia, wait up!")]);
        Assert.Same(plain, RemotePlayNotice.Without(plain, out found));
        Assert.False(found);
    }

    private sealed class Reader(Reading reading) : ITextReader
    {
        public string Read(Frame image) => reading.Text;
        public Reading ReadLines(Frame image, bool deep) => reading;
    }

    private sealed class Sender : ISubtitleSender
    {
        public List<string> Sent { get; } = [];
        public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false) => Sent.Add(text);
        public void Clear() { }
    }

    private sealed class Echo : ITextTranslator
    {
        public List<string> Asked { get; } = [];
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
        {
            lock (Asked) Asked.Add(text);
            return Task.FromResult("TR: " + text);
        }
    }

    /// <summary>Altyazı hattı bildirimi çevirmene göndermez ve görüldüğünü bildirir.</summary>
    [Fact]
    public async Task PipelineDropsTheNoticeAndReportsIt()
    {
        var reading = new Reading([
            new TextLine("Uzaktan Oynama", new TextBox(0.42, 0.03, 0.58, 0.07)),
            new TextLine("Where is the money, Jason?", new TextBox(0.3, 0.8, 0.7, 0.86)),
        ]);
        var translator = new Echo();
        var pipeline = new SubtitlePipeline(new Reader(reading), translator, new Sender());
        var seen = 0;
        pipeline.RemotePlayNoticeSeen += () => Interlocked.Increment(ref seen);
        var region = new CropRegion(0, 1, 0, 1);
        for (var i = 0; i < 20 && translator.Asked.Count == 0; i++)
        {
            await pipeline.ProcessAsync(Frame.Blank(64, 64), region);
            await Task.Delay(20);
        }
        Assert.True(seen > 0);
        lock (translator.Asked) Assert.All(translator.Asked, text => Assert.DoesNotContain("Oynama", text));
        Assert.NotNull(pipeline.LastRead);
    }
}
