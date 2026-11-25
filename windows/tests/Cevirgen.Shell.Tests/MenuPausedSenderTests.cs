// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.Shell;

namespace Cevirgen.Shell.Tests;

/// <summary>Menü sırasında gelen altyazı (Mac/iPhone MenuHeldSenderTests ve Android MenuHeldSenderTest ile aynı).</summary>
public class MenuPausedSenderTests
{
    private sealed class Sink : ISubtitleSender
    {
        public List<string> Sent { get; } = [];
        public int Clears;
        public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false) => Sent.Add(text);
        public void Clear() => Clears++;
    }

    [Fact]
    public void MenuLabelsAreNotSentAfterARealMenu()
    {
        var sink = new Sink();
        var menu = false;
        long time = 0;
        var log = new List<string>();
        var sender = new MenuPausedSender(sink, () => menu, log.Add, () => time);
        sender.SendSubtitle("Bir");
        menu = true;
        time = 100;
        sender.SendSubtitle("RESTORE DEFAULT");
        sender.Clear();
        menu = false;
        time = 3000;
        sender.SendSubtitle("İki");
        Assert.Equal(["Bir", "İki"], sink.Sent);
        Assert.Equal(1, sink.Clears);
        Assert.Contains(log, line => line.Contains("menü sırasında bekletildi") && line.Contains("RESTORE DEFAULT"));
    }

    /// <summary>
    /// #1240: karanlık balık tutma sahnesinde tuş ipucu ve görev yazısı birlikte belirince menü sanılıyordu; o sırada gelen
    /// konuşma satırı TV'ye hiç gitmiyordu. Satır bekletilir, menü kapanınca (yeni satır gelmese de) gönderilir.
    /// </summary>
    [Fact]
    public void DialogueDuringAFalseMenuReachesTheTv()
    {
        var sink = new Sink();
        var menu = true;
        long time = 0;
        var log = new List<string>();
        var sender = new MenuPausedSender(sink, () => menu, log.Add, () => time);
        Assert.True(sender.Poll());
        time = 400;
        sender.SendSubtitle("Lucia: Balık yine kaçtı.");
        time = 900;
        sender.SendSubtitle("Jason: Sabret biraz, gelecek.");
        Assert.Empty(sink.Sent);
        menu = false;
        time = 1500;
        Assert.False(sender.Poll());
        Assert.Equal(["Jason: Sabret biraz, gelecek."], sink.Sent);
        Assert.Contains(log, line => line.Contains("menü kapanınca gönderildi") && line.Contains("Jason"));
    }

    /// <summary>
    /// #1240 uçtan uca: ipucu ve görev yazısı birlikte belirip dedektör menü sandı; altyazı alanında konuşan adıyla satır
    /// okundu (SubtitlePipeline.DialogueRead) → menü biter, satır TV'ye hemen gider.
    /// </summary>
    [Fact]
    public void PromptAndMissionWithDialogueStillSendsTheLine()
    {
        static Frame Fishing()
        {
            var frame = Frame.Blank(640, 360);
            Array.Fill(frame.Pixels, (byte)70);
            foreach (var (column, row, width) in new[] { (26, 15, 3), (12, 16, 8), (1, 1, 3) })
                for (var y = row * 20 + 3; y < row * 20 + 17; y++)
                    for (var x = column * 20; x < (column + width) * 20; x++)
                    {
                        var value = (byte)((y - row * 20) % 6 is 2 or 3 && x % 5 < 2 ? 240 : 24);
                        var p = y * frame.Stride + x * 4;
                        frame.Pixels[p] = frame.Pixels[p + 1] = frame.Pixels[p + 2] = value;
                    }
            return frame;
        }
        var detector = new ScreenTextDetector();
        for (long t = 0; t <= 400; t += ScreenTextDetector.SampleIntervalMs) detector.Sample(Fishing(), t, []);
        Assert.True(detector.InMenu);
        var sink = new Sink();
        long time = 450;
        var sender = new MenuPausedSender(sink, () => detector.InMenu, clock: () => time);
        Assert.True(sender.Poll());
        detector.NoteDialogue(time);
        sender.SendSubtitle("Lucia: Balık yine kaçtı.");
        Assert.Equal(["Lucia: Balık yine kaçtı."], sink.Sent);
    }

    [Fact]
    public void OldHeldLineIsNotSentAfterALongMenu()
    {
        var sink = new Sink();
        var menu = true;
        long time = 0;
        var sender = new MenuPausedSender(sink, () => menu, clock: () => time);
        sender.SendSubtitle("Lucia: Balık yine kaçtı.");
        menu = false;
        time = 10_000;
        sender.Poll();
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void ShortMenuSendsEveryHeldLineAtOnce()
    {
        var sink = new Sink();
        var menu = true;
        long time = 0;
        var sender = new MenuPausedSender(sink, () => menu, clock: () => time);
        sender.SendSubtitle("Bir");
        time = 100;
        sender.SendSubtitle("İki");
        menu = false;
        time = 200;
        sender.Poll();
        Assert.Equal(["Bir", "İki"], sink.Sent);
    }
}
