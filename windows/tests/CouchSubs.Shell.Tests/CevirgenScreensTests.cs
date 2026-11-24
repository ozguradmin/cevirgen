// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using CouchSubs.Core.LgTv;
using CouchSubs.RemotePlay;

namespace CouchSubs.Shell.Tests;

/// <summary>
/// Çevirgen arayüzünün (2. bölüm) arayüzden bağımsız mantığı: Görünüm (okunurluk testi, hazır görünümler, ince ayar),
/// Çeviri (kör test, hız ayarı), Geçmiş (oturumlar, ipuçları, arama, düzeltme ölçüleri), Alanları düzenle ve TV
/// önizlemesinin kare ölçeği.
/// </summary>
public sealed class CevirgenScreensTests
{
    private static AppController Ready(FakeServices? services = null, MemoryStore? store = null, MemorySettings? settings = null)
    {
        store ??= new MemoryStore();
        store.Save(StoreKeys.AccountId, Enumerable.Repeat((byte)1, 8).ToArray());
        store.Save(StoreKeys.Console, new RegisteredConsole("192.168.1.253", "PS5", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)2, 16).ToArray()));
        store.Save(StoreKeys.Tv, new LgTv("LG TV", "127.0.0.1"));
        store.Save(StoreKeys.TvKey, "tv-key");
        return AppControllerTests.Make(services, store, settings);
    }

    // ───── Görünüm: okunurluk ─────

    private static Legibility[] Verdicts(SubtitleStyle style) =>
        [.. SubtitleLegibility.Grounds.Select(ground => SubtitleLegibility.Verdict(style, ground.Hex))];

    [Fact]
    public void ContrastFollowsWcag()
    {
        Assert.Equal(21, SubtitleLegibility.Contrast("#FFFFFF", "#000000"), 1);
        Assert.Equal(1, SubtitleLegibility.Contrast("#777777", "#777777"), 3);
        Assert.Equal("#808080", SubtitleLegibility.Blend("#FFFFFF", "#000000", 0.5025));
        Assert.Equal("#F1F3F5", SubtitleLegibility.Plate(new SubtitleStyle(Background: 0), "#F1F3F5"));
        Assert.Equal("#101014", SubtitleLegibility.Plate(new SubtitleStyle(Background: 100), "#F1F3F5"));
    }

    [Fact]
    public void PresetsGiveTheMockupVerdicts()
    {
        var start = new SubtitleStyle();
        var all = Enumerable.Repeat(Legibility.Readable, 4).ToArray();
        Assert.Equal(all, Verdicts(AppearancePresets.Apply(AppearancePreset.Cinema, start)));
        Assert.Equal([Legibility.Readable, Legibility.Readable, Legibility.Hard, Legibility.Unreadable],
            Verdicts(AppearancePresets.Apply(AppearancePreset.Clean, start)));
        Assert.Equal(all, Verdicts(AppearancePresets.Apply(AppearancePreset.Readable, start)));
        Assert.Equal(all, Verdicts(AppearancePresets.Apply(AppearancePreset.Classic, start)));
    }

    [Fact]
    public void OutlineThicknessAndTextColorChangeTheVerdict()
    {
        var bare = new SubtitleStyle(Background: 0, Outline: false);
        Assert.Equal(Legibility.Unreadable, SubtitleLegibility.Verdict(bare, "#F1F3F5"));
        // İnce kenarlık karda yetmez, 2 px okunur.
        Assert.Equal(Legibility.Hard, SubtitleLegibility.Verdict(bare with { Outline = true, OutlineWidth = 1 }, "#F1F3F5"));
        Assert.Equal(Legibility.Readable, SubtitleLegibility.Verdict(bare with { Outline = true, OutlineWidth = 2 }, "#F1F3F5"));
        // Koyu yazıya siyah kenarlık yardım etmez: gecede okunmaz.
        var dark = bare with { TextHex = "#111114", Outline = true, OutlineWidth = 4 };
        Assert.Equal(Legibility.Unreadable, SubtitleLegibility.Verdict(dark, "#0C0E13"));
        Assert.Equal(Legibility.Readable, SubtitleLegibility.Verdict(dark, "#F1F3F5"));
        Assert.Equal(["Okunur", "Zor", "Okunmaz"], new[] { Legibility.Readable, Legibility.Hard, Legibility.Unreadable }.Select(SubtitleLegibility.Label));
        Assert.Equal("Gece · %4", SubtitleLegibility.Grounds[0].Label);
    }

    [Fact]
    public void PresetsApplyAndAreRecognised()
    {
        var style = new SubtitleStyle(FontSize: 30, TextHex: "#FFE135", BottomPercent: 12);
        foreach (var info in AppearancePresets.All)
        {
            var applied = AppearancePresets.Apply(info.Id, style);
            Assert.Equal(info.Id, AppearancePresets.Match(applied));
            // Yazı rengi ve sabit konum korunur.
            Assert.Equal("#FFE135", applied.TextHex);
            Assert.Equal(12, applied.BottomPercent);
        }
        Assert.Equal(38, AppearancePresets.Apply(AppearancePreset.Readable, style).FontSize);
        Assert.Equal(44, AppearancePresets.Apply(AppearancePreset.Readable, style with { FontSize = 44 }).FontSize);
        Assert.Equal(SubtitlePlacement.Cover, AppearancePresets.Apply(AppearancePreset.Classic, style).Placement);
        Assert.Equal(100, AppearancePresets.Apply(AppearancePreset.Classic, style).Background);
        // Varsayılan biçim Sinema'dır; ince ayarla değişince hiçbiri seçili değil.
        Assert.Equal(AppearancePreset.Cinema, AppearancePresets.Match(new SubtitleStyle()));
        Assert.Null(AppearancePresets.Match(new SubtitleStyle(Background: 40)));
    }

    [Fact]
    public void FineControlsStepAndStayInRange()
    {
        Assert.Equal(34, AppearanceSteps.Step(32, +1, 16, 50, 2));
        Assert.Equal(34, AppearanceSteps.Step(33, +1, 16, 50, 2));
        Assert.Equal(32, AppearanceSteps.Step(33, -1, 16, 50, 2));
        Assert.Equal(50, AppearanceSteps.Step(50, +1, 16, 50, 2));
        Assert.Equal(16, AppearanceSteps.Step(10, -1, 16, 50, 2));
        Assert.Equal(65, AppearanceSteps.Step(62, +1, 0, 100, 5));
        Assert.Equal(60, AppearanceSteps.Step(62, -1, 0, 100, 5));
        Assert.Equal(0, AppearanceSteps.Step(0, -1, 0, 100, 5));

        var style = new SubtitleStyle();
        Assert.Equal(3, AppearanceSteps.Outline(style, +1).OutlinePx);
        var off = AppearanceSteps.Outline(AppearanceSteps.Outline(style, -1), -1);
        Assert.Equal(0, off.OutlinePx);
        Assert.False(off.Outline);
        var on = AppearanceSteps.Outline(off, +1);
        Assert.True(on.Outline);
        Assert.Equal(1, on.OutlinePx);
        Assert.Equal(4, AppearanceSteps.Outline(style with { OutlineWidth = 4 }, +1).OutlinePx);
        Assert.Equal(5, AppearanceSteps.Background(style, +1).Background);
        Assert.Equal(34, AppearanceSteps.FontSize(style, +1).FontSize);
        Assert.Equal("2 satır · 42 karakter", AppearanceSteps.LineLimit);
    }

    // ───── TV önizlemesi: kenarlık ve kare ölçeği ─────

    [Fact]
    public void PreviewOutlineUsesTheChosenWidth()
    {
        var style = new SubtitleStyle(OutlineWidth: 3);
        Assert.Equal(1.5, LivePreviewLayout.OutlinePx(style, 960), 3);
        // Üstteki küçük satır: max(1, round(3 × 0,8)) = 2 TV pikseli (TV ile aynı).
        Assert.Equal(1, LivePreviewLayout.OutlinePx(style, 960, current: false), 3);
        Assert.Equal(0, LivePreviewLayout.OutlinePx(style with { OutlineWidth = 0 }, 960));
        Assert.Equal("İngilizcenin üstünde · 32 px · kenarlık 3 px", LivePreviewLayout.Summary(style));
        Assert.Equal("İngilizcenin üstünde · 32 px", LivePreviewLayout.Summary(style with { OutlineWidth = 0 }));
    }

    [Fact]
    public void FrameIsFittedSoEnglishAndTurkishKeepTheTvRatio()
    {
        // 1280 × 720 kare 960 × 540 önizlemeye: kare pikseli 0,75; Türkçe 1920'ye göre 0,5. 1080p'de 32 px İngilizce
        // (karede 21,3 px) önizlemede 16 px, 32 px Türkçe de 16 px: TV'deki gibi aynı boy.
        var rect = LivePreviewLayout.FrameRect(1280, 720, 960, 540);
        Assert.Equal((0d, 0d, 960d, 540d), rect);
        Assert.Equal(0.75, LivePreviewLayout.FrameScale(1280, 720, 960, 540), 3);
        Assert.Equal(32 / 1.5 * 0.75, 16, 3);
        Assert.Equal(16, LivePreviewLayout.FontPx(new SubtitleStyle(FontSize: 32), rect.Width), 3);
        // 4:3 kare 16:9 önizlemede ortalanır, yanlarda boşluk kalır.
        var square = LivePreviewLayout.FrameRect(1024, 768, 960, 540);
        Assert.Equal(720, square.Width, 3);
        Assert.Equal(120, square.X, 3);
        Assert.Equal(0, square.Y, 3);
        Assert.Equal((0d, 0d, 960d, 540d), LivePreviewLayout.FrameRect(0, 0, 960, 540));
    }

    // ───── Çeviri: kör test ─────

    [Fact]
    public void BlindTestAlwaysHasTheFreeModelAndThreeColumns()
    {
        var none = BlindTest.Columns(TranslationEngine.Free, _ => false);
        Assert.Equal([TranslationEngine.Free, TranslationEngine.Gemini, TranslationEngine.DeepL], none.Select(c => c.Engine));
        Assert.Equal([true, false, false], none.Select(c => c.Ready));

        var claude = BlindTest.Columns(TranslationEngine.Claude, engine => engine is TranslationEngine.Claude or TranslationEngine.DeepL);
        Assert.Equal([TranslationEngine.Free, TranslationEngine.Claude, TranslationEngine.DeepL], claude.Select(c => c.Engine));
        Assert.All(claude, column => Assert.True(column.Ready));

        var one = BlindTest.Columns(TranslationEngine.Free, engine => engine == TranslationEngine.OpenAi);
        Assert.Equal([TranslationEngine.Free, TranslationEngine.OpenAi, TranslationEngine.Gemini], one.Select(c => c.Engine));
        Assert.Equal([true, true, false], one.Select(c => c.Ready));

        Assert.Equal(5, BlindTest.Lines.Count);
        Assert.Equal(["Motor A", "Motor B", "Motor C"], Enumerable.Range(0, 3).Select(BlindTest.HiddenName));
        Assert.Equal("ücretsiz kota · ~250 ms", BlindTest.FactsWithSpeed(TranslationEngine.Gemini, 250));
    }

    [Fact]
    public void BlindTestShuffleKeepsEveryColumn()
    {
        var columns = BlindTest.Columns(TranslationEngine.Free, _ => true);
        var orders = Enumerable.Range(0, 20).Select(seed => BlindTest.Shuffle(columns, new Random(seed))).ToList();
        Assert.All(orders, order => Assert.Equal(columns.Select(c => c.Engine).Order(), order.Select(c => c.Engine).Order()));
        // Ücretsiz model her zaman ilk sütunda değil.
        Assert.Contains(orders, order => order[0].Engine != TranslationEngine.Free);
    }

    [Fact]
    public void VotesAreCountedPerColumnAndTiesHaveNoWinner()
    {
        var votes = new BlindTestVotes();
        Assert.Null(votes.Winner);
        votes.Pick(0, 1);
        votes.Pick(1, 1);
        votes.Pick(2, 2);
        Assert.Equal(2, votes.Votes(1));
        Assert.Equal("1 seçim", votes.VotesText(2));
        Assert.Equal(1, votes.Winner);
        votes.Pick(1, 2);
        Assert.Equal(1, votes.Picked(0));
        Assert.Equal(2, votes.Winner);
        votes.Pick(3, 1);
        Assert.Null(votes.Winner);
        // Aynı hücre yeniden seçilince seçim kalkar.
        votes.Pick(3, 1);
        Assert.Null(votes.Picked(3));
        Assert.Equal(2, votes.Winner);
        Assert.Equal(3, votes.Count);
    }

    [Fact]
    public async Task SamplesAreTranslatedByTheChosenEngineWithoutFallback()
    {
        var services = new FakeServices();
        var store = new MemoryStore();
        var controller = Ready(services, store);
        var free = await controller.TranslateSamplesAsync(TranslationEngine.Free, BlindTest.Lines);
        Assert.False(free.Failed);
        Assert.Equal("TR: Hold tight, we're almost there.", free.Lines[2]);
        Assert.NotNull(free.AverageMs);

        var noKey = await controller.TranslateSamplesAsync(TranslationEngine.Gemini, BlindTest.Lines);
        Assert.True(noKey.Failed);
        Assert.Empty(services.AiTranslators);

        store.Save(StoreKeys.GeminiKey, "gemini-key");
        var gemini = await controller.TranslateSamplesAsync(TranslationEngine.Gemini, BlindTest.Lines);
        Assert.False(gemini.Failed);
        Assert.Equal(AiProvider.Gemini, Assert.Single(services.AiTranslators).Provider);

        services.Translation.Installed = false;
        Assert.True((await controller.TranslateSamplesAsync(TranslationEngine.Free, BlindTest.Lines)).Failed);
        Assert.Contains(controller.BlindTestColumns(), column => column.Engine == TranslationEngine.Gemini && column.Ready);
    }

    [Fact]
    public void UseEngineSelectsTheWinner()
    {
        var store = new MemoryStore();
        var controller = Ready(store: store);
        store.Save(StoreKeys.DeepLKey, "deepl-key");
        controller.UseEngine(TranslationEngine.DeepL);
        Assert.Equal(TranslationEngine.DeepL, controller.Settings.Engine);
        controller.UseEngine(TranslationEngine.Free);
        Assert.Equal(TranslationEngine.Free, controller.Settings.Engine);
    }

    [Fact]
    public void PacesMatchTheWaitSettings()
    {
        var settings = new UserSettings();
        var balanced = TranslationPaces.All.Single(pace => pace.Id == "Balanced");
        var applied = TranslationPaces.Apply(settings, balanced);
        Assert.Equal(800, applied.MisreadWaitMs);
        Assert.Equal(300, applied.PartialWaitMs);
        Assert.Equal(balanced, TranslationPaces.Match(applied));
        Assert.Null(TranslationPaces.Match(applied with { MisreadWaitMs = 500 }));
    }

    // ───── Geçmiş ─────

    private static readonly DateTime Start = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);

    private static SubtitleHistory.Entry Entry(int id, double seconds, string source, string translation, double? leftAfter = null,
        LineOrigin origin = LineOrigin.Engine, double? cps = null) =>
        new(id, Start.AddSeconds(seconds), source, translation, origin)
        {
            ShownAt = Start.AddSeconds(seconds),
            LeftAt = leftAfter is { } left ? Start.AddSeconds(seconds + left) : null,
            Cps = cps,
        };

    [Fact]
    public void HistoryIsSplitIntoSessionsByGapsAndTheCurrentStart()
    {
        // Önceki gün iki satır, bugün iki oturum (arada 2 saat), sonra bu çalışmanın oturumu.
        var entries = new[]
        {
            Entry(0, -86400, "Old one", "Eski bir"), Entry(1, -86390, "Old two", "Eski iki", 2),
            Entry(2, 0, "A", "A tr", 2), Entry(3, 60, "B", "B tr", 2),
            Entry(4, 7200, "C", "C tr", 2),
            Entry(5, 9000, "D", "D tr", 2), Entry(6, 9010, "E", "E tr", 2),
        }.Reverse().ToList();
        var current = Start.AddSeconds(8990);
        var sessions = HistoryModel.Sessions(entries, current);
        Assert.Equal(4, sessions.Count);
        Assert.Equal(["E", "D"], sessions[0].Entries.Select(e => e.Source));
        Assert.Equal(current, sessions[0].Origin);
        Assert.Equal(["C"], sessions[1].Entries.Select(e => e.Source));
        Assert.Equal(["B", "A"], sessions[2].Entries.Select(e => e.Source));
        Assert.Equal(Start, sessions[2].Origin);
        Assert.Equal(2, sessions[3].Entries.Count);
        Assert.Empty(HistoryModel.Sessions([]));
    }

    [Fact]
    public void CuesAreNumberedAndTimedLikeTheSrtFile()
    {
        var session = new HistorySession(Start, [.. new[]
        {
            Entry(0, 2532.0, "Cheers!", "Şerefe!", 1.03, LineOrigin.Correction),
            Entry(1, 2534.870, "I'm thinkin' of doing a little cross-country vacay.", "Bir hanım arkadaşımla ülke boyunca ufak bir tatile çıkmayı düşünüyorum.", 2.882, cps: 21.2),
            Entry(2, 2539.402, "Whip it!", "Kır şunu!"),
            Entry(3, 2541.0, "Next", "Sonraki", 1),
        }.Reverse()]);
        var cues = HistoryModel.Cues(session, "Gemini");
        Assert.Equal([4, 3, 2, 1], cues.Select(cue => cue.Number));
        var cheers = cues[^1];
        Assert.Equal("00:42:12,000 → 00:42:13,030", cheers.Range);
        Assert.Equal("senin", cheers.Source);
        var fast = cues[2];
        Assert.True(fast.Fast);
        Assert.Equal("21 kr/sn · okumak için hızlı, kısalt", HistoryModel.SpeedWarning(fast));
        Assert.Equal("Gemini", fast.Source);
        Assert.Null(HistoryModel.SpeedWarning(cheers));
        // Kalkışı bilinmeyen satır okunacak kadar kalır (en az 1 sn), sonrakinin başını geçmez.
        Assert.Equal("00:42:19,402 → 00:42:20,402", cues[1].Range);
        Assert.Equal(1000, cues[1].DurationMs);

        Assert.True(HistoryModel.Matches(cheers, "şerefe"));
        Assert.True(HistoryModel.Matches(cheers, "CHEERS"));
        Assert.True(HistoryModel.Matches(cheers, "42:12"));
        Assert.False(HistoryModel.Matches(cheers, "43:00"));
        Assert.True(HistoryModel.Matches(cheers, ""));
        Assert.True(HistoryModel.Matches(cheers, "içelim", correction: "Hadi içelim!"));
    }

    [Fact]
    public void SessionHeaderAndEditMetrics()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");
        Assert.Equal("Bugün, 20:00", HistoryModel.Title(Start, Start.AddHours(1), zone));
        Assert.Equal("Dün, 20:00", HistoryModel.Title(Start, Start.AddDays(1), zone));
        Assert.Equal("20 Kasım, 20:00", HistoryModel.Title(Start.AddDays(-4), Start, zone));

        var session = new HistorySession(Start, [Entry(1, 2520, "B", "B tr", 2, LineOrigin.Correction), Entry(0, 0, "A", "A tr", 2)]);
        Assert.Equal("GTA VI · 42 dk · 2 satır · 1 düzeltme", HistoryModel.Meta(session, "GTA VI"));
        Assert.Equal("1 dk'dan kısa · 0 satır · 0 düzeltme", HistoryModel.Meta(new HistorySession(Start, []), null));

        Assert.Equal("25 karakter · 11 kr/sn · sınırın altında", HistoryModel.EditMetrics("Ağzını burnunu kır şunun!", 2198));
        Assert.Equal("40 karakter · 20 kr/sn · okumak için hızlı", HistoryModel.EditMetrics(new string('a', 40), 2000));
        Assert.True(HistoryModel.EditTooFast(new string('a', 40), 2000));
        Assert.Equal("3 karakter", HistoryModel.EditMetrics("abc", 0));
    }

    // ───── Alanları düzenle ─────

    private static AreaSet Areas() => new(new CropRegion(), AreaPreset.Bottom,
        [new ExtraArea("hint", "İpucu", new CropRegion(0.025, 0.165, 0.02, 0.30), Enabled: true)]);

    [Fact]
    public void BoxesMoveResizeAndNudge()
    {
        var model = new AreaEditModel(Areas());
        Assert.Equal(["Altyazı", "İpucu"], model.Boxes.Select(model.DisplayName));
        Assert.False(model.Changed);
        var hint = model.Boxes[1];
        model.Drag(hint.Id, hint.Region, RegionEdit.Handle.None, 0.10, 0.05);
        Assert.Equal(0.12, model.Boxes[1].Region.Left, 6);
        Assert.Equal(0.075, model.Boxes[1].Region.Top, 6);
        Assert.True(model.Changed);
        // Sağ alt köşeden büyütme (ek alan serbest).
        model.Drag(hint.Id, model.Boxes[1].Region, RegionEdit.Handle.Right | RegionEdit.Handle.Bottom, 0.1, 0.1);
        Assert.Equal(0.12, model.Boxes[1].Region.Left, 6);
        Assert.Equal(0.50, model.Boxes[1].Region.Right, 6);

        // Altyazı alanı ortada tutulur: sağ kenar çekilince sol da açılır.
        var main = model.Boxes[0];
        model.Drag(main.Id, main.Region, RegionEdit.Handle.Right, 0.04, 0);
        Assert.Equal(0.10, model.Boxes[0].Region.Left, 6);
        Assert.Equal(0.90, model.Boxes[0].Region.Right, 6);

        model.Select(AreaEditModel.MainId);
        var before = model.Boxes[0].Region;
        model.Nudge(0, -1, large: false);
        Assert.Equal(before.Top - 0.005, model.Boxes[0].Region.Top, 6);
        model.Nudge(1, 0, large: true);
        Assert.Equal(before.Left + 0.05, model.Boxes[0].Region.Left, 6);
        // Kare dışına taşmaz.
        for (var i = 0; i < 40; i++) model.Nudge(0, 1, large: true);
        Assert.Equal(1, model.Boxes[0].Region.Bottom, 6);
        model.Nudge(0, -1, large: true, resize: true);
        Assert.Equal(0.95, model.Boxes[0].Region.Bottom, 6);
    }

    [Fact]
    public void BoxesAreAddedRemovedAndSaved()
    {
        var model = new AreaEditModel(Areas());
        var added = model.Add()!;
        Assert.Equal(added.Id, model.Selected);
        Assert.Equal("Kutu 3", model.DisplayName(added));
        Assert.Equal(2, model.ShortcutSlot(added));
        model.Rename(added.Id, " Görev ");
        model.SetLive(added.Id, false);
        model.SetTarget(added.Id, ResultTarget.Both);
        Assert.False(model.Remove(AreaEditModel.MainId));

        var set = model.ToAreaSet();
        Assert.Equal(AreaPreset.Bottom, set.AreaPreset);
        Assert.Equal(2, set.ExtraAreas.Count);
        var saved = set.ExtraAreas[1];
        Assert.Equal("Görev", saved.Name);
        Assert.False(saved.Enabled);
        Assert.Equal(ResultTarget.Both, saved.Target);

        Assert.True(model.Remove(added.Id));
        Assert.Equal("hint", model.Selected);
        Assert.Null(model.ShortcutSlot(model.Boxes[0]));

        model.ResetToDefaults();
        Assert.Single(model.Boxes);
        Assert.Equal(new CropRegion(), model.Boxes[0].Region);
        Assert.Empty(model.ToAreaSet().ExtraAreas);
    }

    [Fact]
    public void GameAreasAreFoundAutomaticallyOnce()
    {
        Assert.True(AreaEditModel.CanAutoFind("Grand Theft Auto VI"));
        Assert.False(AreaEditModel.CanAutoFind("Far Cry 3"));
        Assert.False(AreaEditModel.CanAutoFind(null));
        var model = new AreaEditModel(new AreaSet(new CropRegion(), AreaPreset.Bottom, []));
        Assert.Equal(2, model.AutoFind("GTA 6"));
        Assert.Equal(["Altyazı", "İpucu", "Mesajlar"], model.Boxes.Select(model.DisplayName));
        Assert.Equal(0, model.AutoFind("GTA 6"));
    }

    [Fact]
    public void CoordinatesAreShownAsPercentWithTurkishDecimals()
    {
        Assert.Equal("x 14 % · y 64 % · 72 × 33 %", AreaEditModel.Coordinates(new CropRegion(0.64, 0.97, 0.14, 0.86)));
        Assert.Equal("x 2 % · y 2,5 % · 28 × 14 %", AreaEditModel.Coordinates(new CropRegion(0.025, 0.165, 0.02, 0.30)));
    }

    [Fact]
    public async Task EditedAreasAreSavedForTheGame()
    {
        // Oyun bilinmiyor: genel alan.
        var controller = Ready();
        Assert.Null(controller.AreaEditGame);
        var edited = new AreaSet(new CropRegion(0.6, 0.9, 0.2, 0.8), AreaPreset.Custom, []);
        controller.ApplyEditedAreas(edited);
        Assert.Equal(edited, controller.Settings.Areas);
        Assert.Empty(controller.Settings.AreaProfiles);

        // Oyun oynanıyor: alanlar o oyunun profili olur; sonraki düzenleme aynı profile yazılır.
        await controller.StartAsync();
        controller.ChooseGame("Far Cry 3");
        Assert.Equal("Far Cry 3", controller.AreaEditGame);
        controller.ApplyEditedAreas(edited with { Region = new CropRegion(0.5, 0.9, 0.2, 0.8) });
        var profile = Assert.Single(controller.Settings.AreaProfiles);
        Assert.Equal("Far Cry 3", controller.AreaProfileGame);
        Assert.Equal(0.5, profile.Areas.Region.Top, 6);
        controller.ApplyEditedAreas(edited with { Region = new CropRegion(0.4, 0.9, 0.2, 0.8) });
        Assert.Equal(0.4, Assert.Single(controller.Settings.AreaProfiles).Areas.Region.Top, 6);
        Assert.False(controller.AreaProfileUnsaved);
    }
}
