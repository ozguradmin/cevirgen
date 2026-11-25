// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>Arkası oynayan sabit yazının çevirisi yanıp sönmüyor; kalkan yazının çevirisi kalkıyor (upstream 45c39d98).</summary>
public partial class RegionWatcherTests
{
    /// <summary>
    /// Bütün kare arkaplanı <paramref name="background"/>; ortadaki şeritte sabit, parlak "yazı" çizgileri. Yazının izi her
    /// karede aynıdır (<see cref="RegionWatcher.TraceGone"/> yanlış), ama karenin çoğu değişir (<see cref="RegionWatcher.BigChangeShare"/> üstü).
    /// </summary>
    private static Frame Lettered(byte background)
    {
        var frame = Shade(background);
        for (var y = 60; y < 120; y++)
            for (var x = 0; x < frame.Width; x++)
                if (x % 8 < 4) Array.Fill(frame.Pixels, (byte)255, y * frame.Stride + x * 4, 4);
        return frame;
    }

    /// <summary>Otomatik ekran yazısı gibi 500 ms'de bir tur atan saat ve izleyici; "Talk to Arthur at the camp" gösterilmiş olarak.</summary>
    private sealed class Camp
    {
        public readonly Reader Reader = new();
        public readonly Translator Translator = new();
        public readonly Server Server = new();
        public readonly List<string> Log = [];
        public long Clock;
        public readonly RegionWatcher Watcher;

        public Camp() => Watcher = new RegionWatcher(Reader, Translator, Server, clock: () => Clock) { Log = line => { lock (Log) Log.Add(line); } };

        public static async Task<Camp> Shown()
        {
            var camp = new Camp();
            camp.Reader.Lines = [Line("Talk to Arthur at the camp")];
            await camp.Watcher.PassAsync(Lettered(10), [Hud]);
            camp.Clock += 500;
            await camp.Watcher.PassAsync(Lettered(10), [Hud]);
            Assert.Equal(["TR: Talk to Arthur at the camp"], camp.Server.Sent.Select(s => s.Text));
            return camp;
        }

        public async Task Pass(string text, Frame frame)
        {
            Clock += 500;
            Reader.Lines = text.Length == 0 ? [] : [Line(text)];
            await Watcher.PassAsync(frame, [Hud]);
        }
    }

    /// <summary>
    /// Yazı sabit, arkasında sahne oynuyor: okumalar yarım, bozuk, arada boş ya da ilgisiz. Çeviri kalır; kutu 10 sn boyunca hiç
    /// kalkıp geri gelmez (önceden 3 tur = 1,5 sn'de kalkıp önbellekten geri geliyordu; Samsung'da parlaklık sıçraması).
    /// </summary>
    [Fact]
    public async Task SteadyTextOverAMovingSceneKeepsItsTranslationWithoutBlinking()
    {
        var camp = await Camp.Shown();
        string[] reads = ["Talk to Arthur at the cam", "", "Wind noise", "Arthur at tho camp", "Tlk t0 Arthr", "Grss camp",
                          "", "Talk to Arthur", "Birds flying", "Talk Arthur at camp."];
        for (var step = 0; step < 20; step++) await camp.Pass(reads[step % reads.Length], Lettered((byte)(40 + step % 4 * 40)));
        Assert.Equal(["TR: Talk to Arthur at the camp"], camp.Server.Sent.Select(s => s.Text));
        Assert.Equal(["Talk to Arthur at the camp"], camp.Translator.Calls);
    }

    /// <summary>
    /// #1198: yazı gidip yerinde sahne oynayınca okuma hiç kararlı olmuyordu; eski çeviri TV'de kalıyordu. Gösterilene benzemeyen
    /// okumalar <see cref="RegionWatcher.UnsteadyTurns"/> tur ve <see cref="RegionWatcher.UnsteadyMs"/> sürünce çeviri kalkar:
    /// ~3 sn içinde, daha önce değil.
    /// </summary>
    [Fact]
    public async Task TextThatReallyLeavesLosesItsTranslationWithinThreeSeconds()
    {
        var camp = await Camp.Shown();
        var start = camp.Clock + 500;
        long? clearedAt = null;
        string[] reads = ["Grass and trees", "Sky above hills", "Dust road ahead", "Rocky cliffs", "Blue river", "Green pines",
                          "Old fence", "Wet stones", "Dark clouds"];
        for (var step = 0; step < reads.Length; step++)
        {
            await camp.Pass(reads[step], Shade((byte)(40 + step * 20)));
            if (clearedAt is null && camp.Server.Sent.Count > 1) clearedAt = camp.Clock;
        }
        Assert.Equal(["TR: Talk to Arthur at the camp", ""], camp.Server.Sent.Select(s => s.Text));
        Assert.NotNull(clearedAt);
        Assert.InRange(clearedAt!.Value - start, RegionWatcher.UnsteadyMs, 3000);
        Assert.Contains(camp.Log, line => line.Contains("kararlı okunmadı, çevirisi kaldırıldı"));
    }

    /// <summary>
    /// Yazı ve izi birlikte gitti (boş okuma, yazısız kare): çeviri beklemeden kalkar. Boş okumada izi yerinde duran yazının
    /// çevirisi ise hemen kalkmaz.
    /// </summary>
    [Fact]
    public async Task EmptyReadRemovesTheTranslationAtOnceOnlyWhenTheTraceIsGone()
    {
        var camp = await Camp.Shown();
        await camp.Pass("", Lettered(200));
        Assert.Single(camp.Server.Sent);
        await camp.Pass("", Shade(100));
        Assert.Equal(["TR: Talk to Arthur at the camp", ""], camp.Server.Sent.Select(s => s.Text));
    }

    /// <summary>
    /// Kaldırılan yazı kısa sürede yeniden okunursa yerindeydi: çeviri önbellekten geri gelir, günlüğe yazılır ve o alanın
    /// kaldırma eşiği uzar (aynı kararsızlık bu kez 3 sn'de çeviriyi kaldırmaz, uzamış eşikte kaldırır).
    /// </summary>
    [Fact]
    public async Task SameTextReturningAfterRemovalExtendsTheThreshold()
    {
        var camp = await Camp.Shown();
        string[] names = ["alpha", "bravo", "delta", "omega", "kappa", "sigma"];
        async Task Unrelated(int count)
        {
            for (var step = 0; step < count; step++)
                await camp.Pass($"Noise {names[step % names.Length]} {step}", Shade((byte)(40 + step % 8 * 20)));
        }
        await Unrelated(7);
        Assert.Equal(["TR: Talk to Arthur at the camp", ""], camp.Server.Sent.Select(s => s.Text));
        await camp.Pass("Talk to Arthur at the camp", Lettered(10));
        await camp.Pass("Talk to Arthur at the camp", Lettered(10));
        Assert.Equal(["TR: Talk to Arthur at the camp", "", "TR: Talk to Arthur at the camp"], camp.Server.Sent.Select(s => s.Text));
        Assert.Equal(["Talk to Arthur at the camp"], camp.Translator.Calls);
        Assert.Contains(camp.Log, line => line.Contains("kaldırılan yazı geri geldi"));
        await Unrelated(8);
        Assert.Equal(3, camp.Server.Sent.Count);
        await Unrelated(13);
        Assert.Equal(["TR: Talk to Arthur at the camp", "", "TR: Talk to Arthur at the camp", ""], camp.Server.Sent.Select(s => s.Text));
    }

    [Fact]
    public void PartlyVisibleReadingsAreNotUnsteady()
    {
        Assert.True(RegionWatcher.PartlyVisible("tlk t0 arthr", "talk to arthur at the camp"));
        Assert.True(RegionWatcher.PartlyVisible("grss camp", "talk to arthur at the camp"));
        Assert.False(RegionWatcher.PartlyVisible("sky above the hills", "talk to arthur at the camp"));
        Assert.False(RegionWatcher.PartlyVisible("", "talk to arthur at the camp"));
        Assert.False(RegionWatcher.UnsteadyLong(3, 5000, 0));
        Assert.False(RegionWatcher.UnsteadyLong(7, 2500, 0));
        Assert.True(RegionWatcher.UnsteadyLong(7, 3000, 0));
        Assert.False(RegionWatcher.UnsteadyLong(7, 3000, 1));
    }
}
