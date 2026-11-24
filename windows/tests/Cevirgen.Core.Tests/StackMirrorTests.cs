// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>
/// Çevirgen: GTA 6'da eski replik ekranda kalırken yenisi altına gelir. Türkçe de aynı düzende durur: eski satırın
/// Türkçesi İngilizcesi karede okundukça yeni satırın üstünde kalır, İngilizcesi kalkınca kalkar (SubtitlePipeline.MirrorsStack).
/// </summary>
public class StackMirrorTests
{
    private const string A = "Now, who the hell built this outhouse back here?";
    private const string B = "Can never have too much storage space, am I right?";
    private const string C = "You are not wrong about that one, my friend.";
    private const double Line = 0.04;

    /// <summary>Gerçek Türkçe döndüren çevirmen (sahte "TR: …" çeviri İngilizceyle aynı sayılıp gösterilmiyor).</summary>
    private sealed class Turkish : ITextTranslator
    {
        public static readonly Dictionary<string, string> Map = new()
        {
            [A] = "Bu tuvaleti buraya kim yaptı?",
            [B] = "Depo alanı asla fazla olmaz, değil mi?",
            [C] = "Bu konuda haksız sayılmazsın dostum.",
        };
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Map.TryGetValue(text, out var turkish) ? turkish : "Çeviri yok.");
    }

    private static string Tr(string english) => Turkish.Map[english];

    private static TextLine Row(string text, double top) => new(text, new TextBox(0.15, top, 0.85, top + Line), 0.95);

    private static Reading Frame(params (string Text, double Top)[] rows) => new(rows.Select(row => Row(row.Text, row.Top)));

    private static async Task<(FakeSender Sender, List<(string Source, int Ms)> Left)> Run(IEnumerable<Reading> frames, bool mirrors = true)
    {
        var list = frames.ToList();
        long clock = 0;
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new SequenceReader(list), new Turkish(), sender, clock: () => clock)
        { PackWait = 0, CleanWait = 0, MirrorsStack = mirrors };
        var left = new List<(string, int)>();
        pipeline.PairLeft += (source, ms) => left.Add((source, ms));
        // Boş karede görüntü de değişir (altyazı gerçekten kalktı; aynı görüntüde boş okuma satırı silmez).
        var empty = new byte[64 * 64 * 4];
        Array.Fill(empty, (byte)255);
        foreach (var reading in list)
        {
            var image = reading.Lines.Count == 0 ? new Core.Frame(empty, 64, 64, 256) : Core.Frame.Blank(64, 64);
            await pipeline.ProcessAsync(image, new CropRegion(0, 1, 0, 1));
            await pipeline.WaitForTranslationsAsync();
            clock += 500;
        }
        return (sender, left);
    }

    private static IEnumerable<Reading> Repeat(Reading frame, int count) => Enumerable.Repeat(frame, count);

    [Fact]
    public async Task OlderTurkishStaysAboveWhileItsEnglishStays()
    {
        var (sender, _) = await Run([
            .. Repeat(Frame((A, 0.70)), 4),
            .. Repeat(Frame((A, 0.70), (B, 0.80)), 6),
            .. Repeat(Frame((B, 0.80)), 4),
            .. Repeat(Reading.Empty, 8),
        ]);
        var first = sender.Sent.IndexOf(Tr(A));
        var second = sender.Sent.IndexOf(Tr(B));
        Assert.True(first >= 0 && second > first, string.Join(" | ", sender.Sent));
        // A tek başınayken üstünde satır yok; B gelince A'nın Türkçesi üstünde.
        Assert.Empty(sender.Above[first]!);
        Assert.Equal([Tr(A)], sender.Above[second]!);
        // A'nın İngilizcesi kalkınca Türkçesi de kalkar (bir kez), B yerinde kalır.
        Assert.Single(sender.AboveUpdates);
        Assert.Empty(sender.AboveUpdates[0]);
        Assert.Equal(1, sender.Clears);
    }

    [Fact]
    public async Task ThreeStackedLinesKeepEnglishOrder()
    {
        var (sender, _) = await Run([
            .. Repeat(Frame((A, 0.60)), 4),
            .. Repeat(Frame((A, 0.60), (B, 0.70)), 4),
            .. Repeat(Frame((A, 0.60), (B, 0.70), (C, 0.80)), 4),
        ]);
        var third = sender.Sent.IndexOf(Tr(C));
        Assert.True(third >= 0, string.Join(" | ", sender.Sent));
        Assert.Equal([Tr(A), Tr(B)], sender.Above[third]!);
        Assert.Empty(sender.AboveUpdates);
    }

    [Fact]
    public async Task SingleMissedReadDoesNotRemoveTheOlderLine()
    {
        // Eski satır bir karede okunamadı (OCR kaçağı): Türkçesi yerinde kalır.
        var (sender, _) = await Run([
            .. Repeat(Frame((A, 0.70)), 4),
            .. Repeat(Frame((A, 0.70), (B, 0.80)), 4),
            Frame((B, 0.80)),
            .. Repeat(Frame((A, 0.70), (B, 0.80)), 3),
        ]);
        Assert.Equal([Tr(A)], sender.Above[sender.Sent.IndexOf(Tr(B))]!);
        Assert.Empty(sender.AboveUpdates);
    }

    [Fact]
    public async Task OlderLineAlreadyGoneIsNotStacked()
    {
        // Yeni satır geldiğinde eskisi çoktan kalkmışsa üstte bir şey gösterilmez.
        var (sender, _) = await Run([
            .. Repeat(Frame((A, 0.70)), 4),
            .. Repeat(Frame((B, 0.80)), 4),
        ]);
        Assert.Empty(sender.Above[sender.Sent.IndexOf(Tr(B))]!);
    }

    [Fact]
    public async Task OffKeepsTheOldProtocol()
    {
        var (sender, _) = await Run([
            .. Repeat(Frame((A, 0.70)), 4),
            .. Repeat(Frame((A, 0.70), (B, 0.80)), 4),
            .. Repeat(Frame((B, 0.80)), 4),
        ], mirrors: false);
        Assert.Contains(Tr(B), sender.Sent);
        Assert.All(sender.Above, Assert.Null);
        Assert.Empty(sender.AboveUpdates);
    }

    [Fact]
    public async Task ShownLinesReportHowLongTheyStayed()
    {
        var (sender, left) = await Run([
            .. Repeat(Frame((A, 0.70)), 4),
            .. Repeat(Frame((B, 0.80)), 6),
            .. Repeat(Reading.Empty, 8),
        ]);
        Assert.Equal(2, left.Count);
        Assert.Equal(A, left[0].Source);
        Assert.Equal(B, left[1].Source);
        // Kareler 500 ms arayla: A, B'ye kadar ~2 sn; B, bölge boşalıp temizlenene kadar 3-4 sn ekranda.
        Assert.InRange(left[0].Ms, 1000, 2500);
        Assert.InRange(left[1].Ms, 2000, 4500);
    }

    [Fact]
    public void ServerSendsAboveFieldAndEvent()
    {
        var server = new SubtitleServer(0);
        var events = new List<SubtitleEvent>();
        server.EventSent += events.Add;
        server.SendWithAbove("Yeni satır.", 15000, null, false, ["Eski satır."]);
        server.SendAbove([]);
        Assert.Equal(SubtitleEventKind.Subtitle, events[0].Kind);
        Assert.Equal(["Eski satır."], events[0].Above!);
        Assert.Equal(SubtitleEventKind.Above, events[1].Kind);
        Assert.Empty(events[1].Above!);
        Assert.Contains("\"hidden\":true", SubtitleServer.VisibilityFrame(true));
    }
}
