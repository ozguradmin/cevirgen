// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text.Json;
using Cevirgen.Core;
using Cevirgen.Core.LgTv;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell.Tests;

/// <summary>
/// Çevirgen 1.5: ekranda gösterilen satırların ortak havuza gitmesi ("Çevirileri iyileştirmeye katkı"), PS5'in Uzaktan
/// Oynatma bildirimi ipucu ve tanılama kaydı (denetleyiciyle uçtan uca, sahte okuyucu ve akışla).
/// </summary>
public sealed class CevirgenLinesTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-lines-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static AppController Ready(FakeServices services)
    {
        var store = new MemoryStore();
        store.Save(StoreKeys.AccountId, Enumerable.Repeat((byte)1, 8).ToArray());
        store.Save(StoreKeys.Console, new RegisteredConsole("192.168.1.253", "PS5", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)2, 16).ToArray()));
        store.Save(StoreKeys.Tv, new LgTv("LG TV", "127.0.0.1"));
        store.Save(StoreKeys.TvKey, "tv-key");
        return AppControllerTests.Make(services, store);
    }

    private static async Task FeedUntil(FakeServices services, Func<bool> condition)
    {
        var frame = Frame.Blank(64, 64);
        for (var i = 0; i < 250 && !condition(); i++)
        {
            services.Streams[^1].Emit(frame);
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    private LinePool MakePool() => new(Path.Combine(folder, LinePool.FileName), "1.5.0", [new Uri("https://pool.test/")], true, new OkHandler());

    private const string Line = "Where is the money, Jason?";

    // ───── Ortak havuz ─────

    [Fact]
    public async Task ShownSubtitleGoesToThePoolWithEngineAndArea()
    {
        var pool = MakePool();
        var services = new FakeServices { ReaderLines = Reading.FromText(Line), Pool = pool };
        var controller = Ready(services);
        Assert.True(controller.Settings.ShareLines);
        await controller.StartAsync();
        await FeedUntil(services, () => pool.Queued.Count == 1);
        var queued = pool.Queued[0];
        Assert.Equal((Line, "TR: " + Line, "free", "subtitle"), (queued.En, queued.Tr, queued.Engine, queued.Area));
        // Kullanıcının kendi çevirisi "user" motoruyla ayrı satır olur.
        Assert.True(controller.SaveCorrection(Line, "Ben gelene dek kapıyı tut."));
        Assert.Contains(pool.Queued, line => line.Engine == LinePool.UserEngine && line.Tr == "Ben gelene dek kapıyı tut.");
        controller.Stop();
        controller.Dispose();
    }

    [Fact]
    public async Task TurningSharingOffStopsAndDeletesTheQueue()
    {
        var pool = MakePool();
        var services = new FakeServices { ReaderLines = Reading.FromText(Line), Pool = pool };
        var controller = Ready(services);
        await controller.StartAsync();
        await FeedUntil(services, () => pool.Queued.Count == 1);
        controller.Settings = controller.Settings with { ShareLines = false };
        Assert.False(pool.IsEnabled);
        Assert.Empty(pool.Queued);
        Assert.True(controller.SaveCorrection(Line, "Kapıyı tut."));
        Assert.Empty(pool.Queued);
        controller.Stop();
        controller.Dispose();
    }

    [Fact]
    public void EngineLabelNamesTheFreeModelWhenNoOnlineEngineIsReady()
    {
        var controller = Ready(new FakeServices());
        Assert.Equal("free", controller.PoolEngineLabel());
        controller.Dispose();
    }

    [Fact]
    public void OnboardingTextMentionsLineSharing()
    {
        Assert.Contains(LinePool.Disclosure, AppController.AnonymousCountOnboardingText);
        Assert.Contains("kişisel bilgi süzülür", LinePool.Disclosure);
    }

    // ───── PS5 Uzaktan Oynatma bildirimi ─────

    [Fact]
    public async Task RemotePlayNoticeIsNeverTranslatedAndRaisesTheHint()
    {
        var pool = MakePool();
        var notice = new Reading([new TextLine("Uzaktan Oynama'ya baglandi"), new TextLine(Line)]);
        var services = new FakeServices { ReaderLines = notice, Pool = pool };
        var controller = Ready(services);
        Assert.False(controller.ShowsRemotePlayHint);
        await controller.StartAsync();
        Assert.False(controller.ShowsRemotePlayHint);
        await FeedUntil(services, () => controller.History.Count >= 1 && controller.ShowsRemotePlayHint);
        Assert.All(controller.History, entry => Assert.DoesNotContain("Uzaktan", entry.Source, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Line, controller.History[0].Source);
        Assert.DoesNotContain(pool.Queued, line => line.En.Contains("Uzaktan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(LiveNoticeKind.RemotePlayPopup, controller.Notices(crashPending: false));

        // Kapatınca bu oturumda görünmez; bildirim yine okunsa da.
        controller.DismissRemotePlayHint();
        Assert.False(controller.ShowsRemotePlayHint);
        for (var i = 0; i < 5; i++) services.Streams[^1].Emit(Frame.Blank(64, 64));
        await Task.Delay(200);
        Assert.False(controller.ShowsRemotePlayHint);
        Assert.DoesNotContain(LiveNoticeKind.RemotePlayPopup, controller.Notices(crashPending: false));
        controller.Stop();

        // Sonraki oturumda bildirim yine okununca yeniden görünür (tek seferlik değil).
        await controller.StartAsync();
        Assert.False(controller.ShowsRemotePlayHint);
        await FeedUntil(services, () => controller.ShowsRemotePlayHint);
        controller.Stop();
        controller.Dispose();
    }

    [Fact]
    public async Task RemotePlayHintNeverShowsWhenTheNoticeIsNotRead()
    {
        var services = new FakeServices { ReaderLines = Reading.FromText(Line) };
        var controller = Ready(services);
        await controller.StartAsync();
        await FeedUntil(services, () => controller.History.Count == 1);
        Assert.False(controller.ShowsRemotePlayHint);
        Assert.DoesNotContain(LiveNoticeKind.RemotePlayPopup, controller.Notices(crashPending: false));
        controller.Stop();
        controller.Dispose();
    }

    [Fact]
    public void RemotePlayHintRules()
    {
        var hint = new RemotePlayHint();
        Assert.False(hint.Visible);
        Assert.False(hint.Dismiss());
        Assert.False(hint.NewSession());
        Assert.True(hint.Seen());
        Assert.True(hint.Visible);
        Assert.False(hint.Seen());
        Assert.True(hint.Dismiss());
        Assert.False(hint.Visible);
        // Kapatılmışken yeniden okunsa da görünmez.
        Assert.False(hint.Seen());
        Assert.False(hint.Visible);
        // Yeni oturum: okunana dek görünmez, okununca yine görünür.
        Assert.False(hint.NewSession());
        Assert.False(hint.Visible);
        Assert.True(hint.Seen());
        Assert.True(hint.NewSession());
        Assert.False(hint.Visible);
    }

    [Fact]
    public void RemotePlayNoticeIsAnInfoNoticeAfterGpuFallback()
    {
        Assert.Equal(LiveNoticeLevel.Info, LiveNotices.Level(LiveNoticeKind.RemotePlayPopup));
        var running = new LiveNoticeState { Running = true, RemotePlayPopup = true, MainAccountTip = true, GpuFallback = true };
        Assert.Equal([LiveNoticeKind.GpuFallback, LiveNoticeKind.RemotePlayPopup, LiveNoticeKind.MainAccountTip], LiveNotices.For(running));
        Assert.Empty(LiveNotices.For(new LiveNoticeState { Running = true, DemoMode = true, RemotePlayPopup = true, CanStart = true }));
        Assert.Contains("Uzaktan Oynatma Bağlantı Durumu Açılır Penceresini Gizle", RemotePlayHint.Instruction);
        Assert.Contains("Hide Remote Play Connection Status Pop-Up", RemotePlayHint.Instruction);
        Assert.Contains("ikinci hesap", RemotePlayHint.Accounts);
        Assert.Contains("ana hesab", RemotePlayHint.Accounts);
    }

    // ───── Tanılama kaydı ─────

    [Fact]
    public async Task DiagnosticsWriteFrameAndSidecarWhileTranslating()
    {
        var services = new FakeServices { ReaderLines = Reading.FromText(Line), DataFolder = folder, Jpeg = (_, width) => [0xFF, 0xD8, (byte)(width / 10)] };
        var controller = Ready(services);
        Assert.False(controller.DiagnosticsOn);
        Assert.Equal(Path.Combine(folder, DiagnosticRecorder.FolderName), controller.DiagnosticsFolder);
        controller.DiagnosticsOn = true;
        var session = controller.DiagnosticsSessionFolder!;
        Assert.StartsWith(controller.DiagnosticsFolder, session);
        await controller.StartAsync();
        await FeedUntil(services, () => File.Exists(Path.Combine(session, "0001.json")));
        Assert.Equal(new byte[] { 0xFF, 0xD8, 128 }, File.ReadAllBytes(Path.Combine(session, "0001.jpg")));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "0001.json")));
        var root = json.RootElement;
        Assert.Equal("0001.jpg", root.GetProperty("image").GetString());
        var subtitle = root.GetProperty("areas").GetProperty("subtitle");
        Assert.Equal(controller.Settings.Region.Top * 100, subtitle.GetProperty("top").GetDouble(), 2);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("areas").GetProperty("extra").ValueKind);
        // Beş saniye dolmadan ikinci kare yazılmaz.
        for (var i = 0; i < 5; i++) services.Streams[^1].Emit(Frame.Blank(64, 64));
        await Task.Delay(200);
        Assert.False(File.Exists(Path.Combine(session, "0002.jpg")));
        controller.DiagnosticsOn = false;
        Assert.Null(controller.DiagnosticsSessionFolder);
        controller.Stop();
        controller.Dispose();
    }

    [Fact]
    public void DiagnosticsAreOffAfterRestart()
    {
        var services = new FakeServices { DataFolder = folder, Jpeg = (_, _) => [1] };
        var controller = Ready(services);
        controller.DiagnosticsOn = true;
        controller.Dispose();
        var reopened = Ready(new FakeServices { DataFolder = folder, Jpeg = (_, _) => [1] });
        Assert.False(reopened.DiagnosticsOn);
        reopened.Dispose();
    }

    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 11, 24, 21, 5, 9);
    }

    [Fact]
    public void RecorderKeepsIntervalCapExpiryAndCleansOldSessions()
    {
        var clock = new Clock();
        var root = Path.Combine(folder, "tanilama");
        // Eski (4 gün) oturum silinir; yeni olan ve adı tarih olmayan klasör kalır.
        var old = Path.Combine(root, "2026-11-20_10-00-00");
        var recent = Path.Combine(root, "2026-11-23_10-00-00");
        var other = Path.Combine(root, "notlar");
        foreach (var directory in new[] { old, recent, other }) Directory.CreateDirectory(directory);
        var recorder = new DiagnosticRecorder(root, (_, _) => [1, 2, 3], () => clock.Now);
        Assert.False(recorder.Due());
        recorder.Start();
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(other));
        Assert.Equal(Path.Combine(root, "2026-11-24_21-05-09"), recorder.SessionFolder);

        var snapshot = new DiagnosticSnapshot(DateTimeOffset.Now, new CropRegion(0.6, 0.95, 0.1, 0.9),
            [new WatchedArea("hint", "İpucu", new CropRegion(0.02, 0.2, 0.02, 0.3))],
            (new Reading([new TextLine("He still here?", new TextBox(0.25, 0.5, 0.75, 0.9), 0.97)]), new CropRegion(0.6, 0.95, 0.1, 0.9)),
            "He still here?", "Hâlâ burada mı?", "Grand Theft Auto VI");
        var frame = Frame.Blank(1920, 1080);
        Assert.True(recorder.Record(frame, snapshot));
        Assert.False(recorder.Record(frame, snapshot));
        clock.Now += DiagnosticRecorder.Interval;
        Assert.True(recorder.Record(frame, snapshot));
        Assert.Equal(2, recorder.Frames);

        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(recorder.SessionFolder!, "0001.json"))))
        {
            var root2 = json.RootElement;
            Assert.Equal(1280, root2.GetProperty("frame").GetProperty("savedWidth").GetInt32());
            Assert.Equal(720, root2.GetProperty("frame").GetProperty("savedHeight").GetInt32());
            Assert.Equal(60, root2.GetProperty("areas").GetProperty("subtitle").GetProperty("top").GetDouble());
            Assert.Equal("hint", root2.GetProperty("areas").GetProperty("extra")[0].GetProperty("id").GetString());
            var line = root2.GetProperty("ocr").GetProperty("lines")[0];
            Assert.Equal("He still here?", line.GetProperty("text").GetString());
            // Kutu altyazı alanından karenin yüzdesine: sol 10 + 0.25 × 80 = 30; üst 60 + 0.5 × 35 = 77.5.
            Assert.Equal(30, line.GetProperty("box").GetProperty("left").GetDouble(), 2);
            Assert.Equal(77.5, line.GetProperty("box").GetProperty("top").GetDouble(), 2);
            Assert.Equal("Hâlâ burada mı?", root2.GetProperty("lastShown").GetProperty("translation").GetString());
            Assert.Equal("Grand Theft Auto VI", root2.GetProperty("game").GetString());
        }

        // Sınır: oturum başına en çok 400 kare.
        for (var i = 0; i < DiagnosticRecorder.MaxFrames + 5; i++)
        {
            clock.Now += DiagnosticRecorder.Interval;
            recorder.Record(frame, snapshot);
        }
        Assert.Equal(DiagnosticRecorder.MaxFrames, recorder.Frames);
        Assert.Equal(DiagnosticRecorder.MaxFrames, Directory.GetFiles(recorder.SessionFolder!, "*.jpg").Length);
        Assert.False(recorder.Due());

        // 30 dakika dolunca kapanmalı.
        Assert.True(recorder.Expired);
        recorder.Stop();
        Assert.False(recorder.Enabled);
        Assert.False(recorder.Expired);
        Assert.False(recorder.Record(frame, snapshot));
    }
}
