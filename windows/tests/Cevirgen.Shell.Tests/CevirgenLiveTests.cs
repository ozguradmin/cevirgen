// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using Cevirgen.Core;

namespace Cevirgen.Shell.Tests;

/// <summary>
/// Çevirgen 1.4: Canlı'nın bildirim sırası (hata &gt; yapılacak &gt; bilgi), oyun seçicisi (son oynananlar, kapak önceliği,
/// takma adlar), destek alanı (20 satırdan sonra, hata yokken, sunucudan değişebilir) ve platform sorusundaki sayım metni.
/// </summary>
public sealed class CevirgenLiveTests
{
    // ───── Bildirimler ─────

    [Fact]
    public void BlockingErrorsComeBeforeActionsAndActionsBeforeInfo()
    {
        var kinds = LiveNotices.Order([LiveNoticeKind.GpuFallback, LiveNoticeKind.Tv, LiveNoticeKind.Crash, LiveNoticeKind.OcrMissing,
            LiveNoticeKind.Firewall, LiveNoticeKind.EngineFailed, LiveNoticeKind.Tv]);
        Assert.Equal([LiveNoticeKind.EngineFailed, LiveNoticeKind.OcrMissing, LiveNoticeKind.Tv, LiveNoticeKind.Firewall,
            LiveNoticeKind.Crash, LiveNoticeKind.GpuFallback], kinds);
        // Her tür bir önem alır; enum sırası önem sınırlarını korur.
        foreach (var kind in Enum.GetValues<LiveNoticeKind>())
        {
            var level = LiveNotices.Level(kind);
            if (kind <= LiveNoticeKind.PictureStopped) Assert.Equal(LiveNoticeLevel.Blocking, level);
            else if (kind <= LiveNoticeKind.AreaProfileOffer) Assert.Equal(LiveNoticeLevel.Action, level);
            else Assert.Equal(LiveNoticeLevel.Info, level);
        }
    }

    [Fact]
    public void MissingModelOnlyBlocksWhenTheFreeEngineIsSelected()
    {
        Assert.Equal(LiveNoticeLevel.Blocking, LiveNotices.Level(LiveNoticeKind.ModelMissing, freeEngine: true));
        Assert.Equal(LiveNoticeLevel.Action, LiveNotices.Level(LiveNoticeKind.ModelMissing, freeEngine: false));
        // Gemini seçiliyken model eksikliği "Yapılacak" olur: yazı tanıma hatasından sonra, aynı önemdeki TV'den önce (enum sırası).
        var state = new LiveNoticeState { Running = true, ModelMissing = true, FreeEngine = false, TvNeedsWork = true, OcrMissing = true };
        Assert.Equal([LiveNoticeKind.OcrMissing, LiveNoticeKind.ModelMissing, LiveNoticeKind.Tv], LiveNotices.For(state));
        Assert.Equal([LiveNoticeKind.ModelMissing, LiveNoticeKind.OcrMissing, LiveNoticeKind.Tv], LiveNotices.For(state with { FreeEngine = true }));
    }

    [Fact]
    public void SilenceReasonsAndEngineIssuesOnlyWhileTranslating()
    {
        var idle = new LiveNoticeState { Problem = HomeProblemKind.PictureStopped, OnlineIssue = true, AreaProfileOffered = true };
        Assert.Empty(LiveNotices.For(idle));
        var running = idle with { Running = true };
        Assert.Equal([LiveNoticeKind.PictureStopped, LiveNoticeKind.OnlineEngine, LiveNoticeKind.AreaProfileOffer], LiveNotices.For(running));
        // Deneme modunda sessizlik nedeni ve motor sorunu gösterilmez.
        Assert.Equal([LiveNoticeKind.AreaProfileOffer], LiveNotices.For(running with { DemoMode = true }));
        // Oyun sorulurken alan önerisi bekler; öneri yoksa etkin profil bilgi olarak.
        Assert.DoesNotContain(LiveNoticeKind.AreaProfileOffer, LiveNotices.For(running with { AskingGame = true }));
        Assert.Contains(LiveNoticeKind.AreaProfileActive, LiveNotices.For(running with { AreaProfileOffered = false, AreaProfileActive = true }));
        Assert.Equal([LiveNoticeKind.SubtitlesOff], LiveNotices.For(new LiveNoticeState { Running = true, Problem = HomeProblemKind.SubtitlesOff }));
        Assert.Equal([LiveNoticeKind.MenuOpen], LiveNotices.For(new LiveNoticeState { Running = true, Problem = HomeProblemKind.MenuOpen }));
    }

    [Fact]
    public void EveryNoticeHasItsSourceAndDemoOnlyWhenSetupIsIncomplete()
    {
        var all = new LiveNoticeState
        {
            Running = true, EngineFailed = true, ModelMissing = true, OcrMissing = true, ConsoleOffersReconnect = true,
            Problem = HomeProblemKind.PictureStopped, TvNeedsWork = true, Firewall = FirewallPrompt.Offer, OnlineIssue = true,
            AreaProfileOffered = true, CrashPending = true, GpuFallback = true, MainAccountTip = true, AutoScreenTextNotice = true,
        };
        var kinds = LiveNotices.For(all);
        Assert.Equal(13, kinds.Count);
        Assert.Equal(LiveNoticeKind.EngineFailed, kinds[0]);
        Assert.Equal(kinds.OrderBy(kind => LiveNotices.Level(kind)).ThenBy(kind => (int)kind), kinds);
        Assert.Equal([LiveNoticeKind.Demo], LiveNotices.For(new LiveNoticeState { CanStart = false }));
        Assert.Empty(LiveNotices.For(new LiveNoticeState { CanStart = false, Running = true }));
        Assert.Empty(LiveNotices.For(new LiveNoticeState()));
    }

    [Fact]
    public void LabelsColoursAndMoreText()
    {
        Assert.Equal(("Hata", "Yapılacak", "Bilgi"),
            (LiveNotices.Label(LiveNoticeLevel.Blocking), LiveNotices.Label(LiveNoticeLevel.Action), LiveNotices.Label(LiveNoticeLevel.Info)));
        Assert.Equal((DeviceState.Error, DeviceState.Waiting, DeviceState.Off),
            (LiveNotices.State(LiveNoticeLevel.Blocking), LiveNotices.State(LiveNoticeLevel.Action), LiveNotices.State(LiveNoticeLevel.Info)));
        Assert.Null(LiveNotices.MoreText(0));
        Assert.Null(LiveNotices.MoreText(1));
        Assert.Equal("+1 uyarı daha", LiveNotices.MoreText(2));
        Assert.Equal("+2 uyarı daha", LiveNotices.MoreText(3));
    }

    [Fact]
    public void ControllerStateFeedsTheNotices()
    {
        var controller = AppControllerTests.Make();
        var state = controller.NoticeState(crashPending: true);
        Assert.False(state.Running);
        Assert.True(state.CrashPending);
        Assert.Contains(LiveNoticeKind.Crash, controller.Notices(crashPending: true));
        Assert.DoesNotContain(LiveNoticeKind.Crash, controller.Notices(crashPending: false));
        // Çeviri durmuşken TV bildirimi yalnız TV eşleşmemişse (bağlı olmayan TV uygulaması beklenen hâldir).
        Assert.Equal(controller.NeedsTv, controller.TvNeedsAttention);
        Assert.Equal(controller.NeedsTv, controller.Notices(false).Contains(LiveNoticeKind.Tv));
    }

    // ───── Oyun seçicisi ─────

    private static readonly IReadOnlyList<PackShelfItem> Packs =
    [
        new("gta-6", "Grand Theft Auto VI", 1200, ["gta-6"], ["Grand Theft Auto VI"]),
        new("rdr-2", "Red Dead Redemption 2", 900, ["rdr-2"], ["Red Dead Redemption 2"]),
        new(GameSense.GeneralPack, "Genel", 10, [GameSense.GeneralPack], ["Genel"]),
    ];

    private static ResolvedGame Remote(string id, string title, string? cover, params string[] aliases) =>
        new(new RemoteGame(id, title, cover is null ? null : new Uri("https://cdn.test/" + id + ".jpg"), aliases), cover);

    [Fact]
    public void RecentGamesComeFirstThenPacksThenServerGamesThenPopular()
    {
        var remote = new[] { Remote("hades-2", "Hades II", "c:/kapak/hades.jpg", "Hades 2") };
        var tiles = GamePicker.Tiles("", ["Ghost of Yōtei", "GTA 6"], Packs, remote, chosen: "Grand Theft Auto VI", _ => null);
        Assert.Equal(["Ghost of Yōtei", "GTA 6", "Red Dead Redemption 2", "Hades II"], tiles.Take(4).Select(tile => tile.Name));
        // "GTA 6" ve paketteki "Grand Theft Auto VI" aynı oyun: bir kez; genel havuz paketi oyun değildir.
        Assert.DoesNotContain(tiles, tile => tile.Name == "Grand Theft Auto VI" || tile.Name == "Genel");
        Assert.Equal([GameTileTag.Recent, GameTileTag.Recent, GameTileTag.Pack, GameTileTag.None], tiles.Take(4).Select(tile => tile.Tag));
        Assert.True(tiles[1].Selected);
        Assert.Equal("gta-6", tiles[1].PackId);
        Assert.Equal("c:/kapak/hades.jpg", tiles[3].CoverFile);
        Assert.Single(tiles, tile => tile.Selected);
        Assert.Equal(tiles.Select(tile => GameName.Key(tile.Name)).Distinct().Count(), tiles.Count);
        Assert.True(tiles.Count <= GamePicker.MaxTiles);
        Assert.Contains(tiles, tile => tile.Name == "Elden Ring");
    }

    [Fact]
    public void UserCoverWinsOverServerCover()
    {
        var remote = new[] { Remote("gta-6", "Grand Theft Auto VI", "c:/uzak/gta.jpg"), Remote("hades-2", "Hades II", "c:/uzak/hades.jpg") };
        string? User(string id) => id switch { "gta-6" => "c:/kullanici/gta.png", "hades-2" => "c:/kullanici/hades.png", _ => null };
        var tiles = GamePicker.Tiles("", [], Packs, remote, null, User);
        Assert.Equal("c:/kullanici/gta.png", tiles.Single(tile => tile.Name == "Grand Theft Auto VI").CoverFile);
        // Paketi olmayan oyunda da kullanıcının kapağı (oyun kimliğiyle) önce gelir.
        Assert.Equal("c:/kullanici/hades.png", tiles.Single(tile => tile.Name == "Hades II").CoverFile);
        var noUser = GamePicker.Tiles("", [], Packs, remote, null, _ => null);
        Assert.Equal("c:/uzak/gta.jpg", noUser.Single(tile => tile.Name == "Grand Theft Auto VI").CoverFile);
        Assert.Null(noUser.Single(tile => tile.Name == "Elden Ring").CoverFile);
    }

    [Fact]
    public void SearchFindsAliasesAndEnterPicksTheMatch()
    {
        var remote = new[] { Remote("hades-2", "Hades II", null, "Hades 2", "Hades Sequel") };
        var found = GamePicker.Tiles("sequel", [], Packs, remote, null, _ => null);
        Assert.Equal(["Hades II"], found.Select(tile => tile.Name));
        var gta = GamePicker.Tiles("gta", [], Packs, remote, null, _ => null);
        Assert.Equal("Grand Theft Auto VI", gta[0].Name);
        Assert.Contains(gta, tile => tile.Name == "Grand Theft Auto V");
        Assert.Empty(GamePicker.Tiles("zzz yok", [], Packs, remote, null, _ => null));

        Assert.Equal("Hades II", GamePicker.EnterChoice("Hades Sequel", found, remote));
        Assert.Equal("Grand Theft Auto VI", GamePicker.EnterChoice("GTA 6", gta, remote));
        Assert.Equal("Hades II", GamePicker.EnterChoice("sequel", found, remote));
        Assert.Equal("Benim Oyunum", GamePicker.EnterChoice(" Benim Oyunum ", [], remote));
        Assert.Null(GamePicker.EnterChoice("  ", gta, remote));
    }

    [Fact]
    public void RecentListIsDistinctNewestFirstAndBounded()
    {
        var recent = GamePicker.WithRecent(["Elden Ring", "GTA 6"], "Grand Theft Auto VI");
        Assert.Equal(["Grand Theft Auto VI", "Elden Ring"], recent);
        IReadOnlyList<string> many = [];
        for (var i = 0; i < 12; i++) many = GamePicker.WithRecent(many, $"Oyun {i}");
        Assert.Equal(GamePicker.MaxRecent, many.Count);
        Assert.Equal("Oyun 11", many[0]);
        Assert.Equal(["x"], GamePicker.WithRecent(["x"], "  "));
        Assert.Equal(("Son oynanan", "Paket var"), (GamePicker.TagText(GameTileTag.Recent), GamePicker.TagText(GameTileTag.Pack)));
        Assert.Null(GamePicker.TagText(GameTileTag.None));
    }

    /// <summary>Çevirgen: okunan pencere açık değilse uyarı çıkar; "görüntü gelmiyor" onun yerine geçmez. Çeviri durmuşken çıkmaz.</summary>
    [Fact]
    public void AMissingWindowIsTheNoticeNotAStoppedPicture()
    {
        var missing = new LiveNoticeState { Running = true, WindowMissing = true, Problem = HomeProblemKind.PictureStopped };
        Assert.Equal(LiveNoticeKind.WindowMissing, LiveNotices.For(missing)[0]);
        Assert.DoesNotContain(LiveNoticeKind.PictureStopped, LiveNotices.For(missing));
        Assert.Equal(LiveNoticeLevel.Blocking, LiveNotices.Level(LiveNoticeKind.WindowMissing));
        Assert.DoesNotContain(LiveNoticeKind.WindowMissing, LiveNotices.For(missing with { Running = false }));
    }

    [Fact]
    public void ChoosingAGameUpdatesTheRecentList()
    {
        var store = new MemoryStore();
        var controller = AppControllerTests.Make(store: store);
        Assert.Empty(controller.RecentGames);
        controller.ChooseGame("Elden Ring");
        controller.ChooseGame("Hades II");
        controller.ChooseGame("Elden Ring");
        Assert.Equal(["Elden Ring", "Hades II"], controller.RecentGames);
        var tiles = AppControllerTests.Make(store: store).GameTiles("");
        Assert.Equal(["Elden Ring", "Hades II"], tiles.Take(2).Select(tile => tile.Name));
        Assert.All(tiles.Take(2), tile => Assert.Equal(GameTileTag.Recent, tile.Tag));
    }

    // ───── Destek alanı ─────

    [Fact]
    public void SupportDefaultAsksForAStarWithoutOfferingAnything()
    {
        var slot = SupportSlot.Default;
        Assert.Equal("Çevirgen işine yaradıysa GitHub'da yıldız vererek destek olabilirsin.", slot.Text);
        Assert.Equal("Yıldız ver", slot.Button);
        Assert.Equal("https://github.com/ozguradmin/cevirgen", slot.Link.AbsoluteUri);
        Assert.Null(slot.IconFile);
        Assert.Equal(slot, SupportSlot.From(null));
        var remote = new ResolvedSupport(new RemoteSupport("k", "Metin", "Düğme", new Uri("https://example.org/"), null), "c:/simge.png");
        Assert.Equal(new SupportSlot("k", "Metin", "Düğme", new Uri("https://example.org/"), "c:/simge.png"), SupportSlot.From(remote));

        Assert.False(SupportSlot.Visible(19, blockingNotice: false));
        Assert.True(SupportSlot.Visible(20, blockingNotice: false));
        Assert.False(SupportSlot.Visible(400, blockingNotice: true));
    }

    [Fact]
    public void SupportUnlocksAfterTwentyRealLinesAndStaysUnlocked()
    {
        var store = new MemoryStore();
        var controller = AppControllerTests.Make(store: store);
        var changed = new List<string?>();
        controller.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var add = typeof(AppController).GetMethod("AddToHistory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < SupportSlot.LinesBeforeShowing - 1; i++) add.Invoke(controller, [$"Line {i}", $"Satır {i}", LineOrigin.Engine]);
        Assert.False(controller.SupportUnlocked);
        Assert.DoesNotContain(nameof(AppController.SupportUnlocked), changed);
        add.Invoke(controller, ["Last line", "Son satır", LineOrigin.Engine]);
        Assert.True(controller.SupportUnlocked);
        Assert.Contains(nameof(AppController.SupportUnlocked), changed);
        Assert.Equal(SupportSlot.LinesBeforeShowing, store.Load<int?>(StoreKeys.SupportLines));
        // Sonraki açılışta da açık.
        Assert.True(AppControllerTests.Make(store: store).SupportUnlocked);

        Uri? opened = null;
        controller.OpenInBrowser = url => opened = url;
        controller.OpenSupport();
        Assert.Equal(SupportSlot.Default.Link, opened);
    }

    // ───── Platform sorusu ─────

    [Fact]
    public void OnboardingShowsTheCountDisclosureAsPlainTextPointingToSettings()
    {
        Assert.EndsWith("Ayarlar › Uygulama'dan kapatabilirsin.", AppController.AnonymousCountOnboardingText);
        Assert.StartsWith(UsageCounter.DisclosureBody, AppController.AnonymousCountOnboardingText);
        Assert.Contains("IP adresin saklanmaz", AppController.AnonymousCountOnboardingText);
        Assert.Contains("Ayarlardan kapatabilirsin", AppController.AnonymousCountDisclosure);
    }
}
