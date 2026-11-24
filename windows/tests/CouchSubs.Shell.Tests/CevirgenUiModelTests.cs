// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using CouchSubs.Core.LgTv;
using CouchSubs.RemotePlay;

namespace CouchSubs.Shell.Tests;

/// <summary>
/// Çevirgen arayüzünün (1. bölüm) arayüzden bağımsız mantığı: platform seçimi ve kurulum akışı, Canlı ekranın dökümü ve
/// TV önizlemesinin yerleşimi.
/// </summary>
public sealed class CevirgenUiModelTests
{
    private static AppController Ready(FakeServices? services = null, MemoryStore? store = null)
    {
        store ??= new MemoryStore();
        store.Save(StoreKeys.AccountId, Enumerable.Repeat((byte)1, 8).ToArray());
        store.Save(StoreKeys.Console, new RegisteredConsole("192.168.1.253", "PS5", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)2, 16).ToArray()));
        store.Save(StoreKeys.Tv, new LgTv("LG TV", "127.0.0.1"));
        store.Save(StoreKeys.TvKey, "tv-key");
        return AppControllerTests.Make(services, store);
    }

    // ───── Platform ve kurulum akışı ─────

    [Fact]
    public void PlatformOptionsMatchTheSetupMockup()
    {
        Assert.Equal(["PS5", "Xbox", "Bilgisayar", "Diğer"], SetupPlatforms.All.Select(option => option.Name));
        Assert.Equal("4 adım · 3 dk", SetupPlatforms.Option(SetupPlatform.Ps5).StepsText);
        Assert.Equal("2 adım · 1 dk", SetupPlatforms.Option(SetupPlatform.Pc).StepsText);
        Assert.All(SetupPlatforms.All, option => Assert.False(string.IsNullOrWhiteSpace(option.Needs)));
    }

    [Fact]
    public void EachPlatformHasOnlyItsOwnSteps()
    {
        Assert.Equal([OnboardingStep.SecondAccount, OnboardingStep.PsnLogin, OnboardingStep.Console, OnboardingStep.Tv, OnboardingStep.Translation,
            OnboardingStep.BetterTranslation, OnboardingStep.Test], SetupPlatforms.Steps(SetupPlatform.Ps5, hdmi: false));
        Assert.Equal([OnboardingStep.PsnLogin, OnboardingStep.Console, OnboardingStep.Tv, OnboardingStep.Hdmi, OnboardingStep.Translation,
            OnboardingStep.BetterTranslation, OnboardingStep.Test], SetupPlatforms.Steps(SetupPlatform.Xbox, hdmi: true));
        foreach (var platform in new[] { SetupPlatform.Pc, SetupPlatform.Capture })
        {
            var steps = SetupPlatforms.Steps(platform, hdmi: false);
            Assert.Equal(OnboardingStep.Window, steps[0]);
            Assert.DoesNotContain(OnboardingStep.SecondAccount, steps);
            Assert.DoesNotContain(OnboardingStep.PsnLogin, steps);
            Assert.DoesNotContain(OnboardingStep.Console, steps);
        }
    }

    [Fact]
    public void NextSkipsStepsThatAreNotNeededAndEndsAtDone()
    {
        Assert.Equal(OnboardingStep.SecondAccount, SetupPlatforms.Next(SetupPlatform.Ps5, OnboardingStep.Welcome, false));
        Assert.Equal(OnboardingStep.Window, SetupPlatforms.Next(SetupPlatform.Pc, OnboardingStep.Welcome, false));
        Assert.Equal(OnboardingStep.Tv, SetupPlatforms.Next(SetupPlatform.Ps5, OnboardingStep.Welcome, false, step => step == OnboardingStep.Tv));
        Assert.Equal(OnboardingStep.Done, SetupPlatforms.Next(SetupPlatform.Xbox, OnboardingStep.Welcome, false, _ => false));
        Assert.Equal(OnboardingStep.Done, SetupPlatforms.Next(SetupPlatform.Pc, OnboardingStep.Test, false));
        Assert.Equal(OnboardingStep.Welcome, SetupPlatforms.Previous(SetupPlatform.Pc, OnboardingStep.Window, false));
        Assert.Equal(OnboardingStep.Window, SetupPlatforms.Previous(SetupPlatform.Capture, OnboardingStep.Tv, false));
    }

    [Fact]
    public void PlatformIsInferredFromTheSettings()
    {
        var settings = new UserSettings();
        Assert.Equal(SetupPlatform.Ps5, SetupPlatforms.Infer(GameConsoleKind.PlayStation, settings));
        Assert.Equal(SetupPlatform.Xbox, SetupPlatforms.Infer(GameConsoleKind.Xbox, settings));
        var obs = settings with { FrameSource = FrameSource.CaptureWindow, CaptureWindow = new WindowCaptureTarget("obs64", "Projektör") };
        Assert.Equal(SetupPlatform.Capture, SetupPlatforms.Infer(GameConsoleKind.PlayStation, obs));
        var game = obs with { CaptureWindow = new WindowCaptureTarget("GTA5", "Grand Theft Auto V") };
        Assert.Equal(SetupPlatform.Pc, SetupPlatforms.Infer(GameConsoleKind.PlayStation, game));
        // Kayıtlı seçim (Diğer) ayarlarla çelişmiyorsa korunur; çelişiyorsa ayarlar kazanır.
        Assert.Equal(SetupPlatform.Capture, SetupPlatforms.Infer(GameConsoleKind.PlayStation, game, SetupPlatform.Capture));
        Assert.Equal(SetupPlatform.Ps5, SetupPlatforms.Infer(GameConsoleKind.PlayStation, settings, SetupPlatform.Capture));
        Assert.Equal(SetupPlatform.Pc, SetupPlatforms.Infer(GameConsoleKind.PlayStation, settings with { FrameSource = FrameSource.RemotePlayWindow }));
    }

    [Fact]
    public void ChoosingPcAtWelcomeGoesToTheWindowStepAndNeedsAWindow()
    {
        var store = new MemoryStore();
        var controller = AppControllerTests.Make(store: store);
        Assert.Equal(OnboardingStep.Welcome, controller.Step);
        controller.ChoosePlatform(SetupPlatform.Pc);
        // Seçim Devam'a basılmadan uygulanmaz.
        Assert.Equal(FrameSource.Ps5, controller.Settings.FrameSource);
        Assert.Equal(SetupPlatform.Pc, controller.PendingPlatform);
        controller.Advance();
        Assert.Equal(OnboardingStep.Window, controller.Step);
        Assert.Equal(FrameSource.CaptureWindow, controller.Settings.FrameSource);
        Assert.Equal(SetupPlatform.Pc, controller.Platform);
        Assert.Equal(SetupPlatform.Pc, store.Load<SetupPlatform?>(StoreKeys.SetupPlatform));
        Assert.False(controller.CanAdvance);
        controller.Settings = controller.Settings with { CaptureWindow = new WindowCaptureTarget("GTA5", "Grand Theft Auto V") };
        Assert.True(controller.CanAdvance);
        controller.Advance();
        Assert.Equal(OnboardingStep.Tv, controller.Step);
        controller.Back();
        Assert.Equal(OnboardingStep.Window, controller.Step);
        controller.Back();
        Assert.Equal(OnboardingStep.Welcome, controller.Step);
        controller.Dispose();
    }

    [Fact]
    public void ChoosingXboxAtWelcomeStartsWithMicrosoftSignIn()
    {
        var controller = AppControllerTests.Make(new FakeServices { Xbox = new FakeXbox([]) });
        controller.ChoosePlatform(SetupPlatform.Xbox);
        controller.Advance();
        Assert.True(controller.IsXbox);
        Assert.Equal(OnboardingStep.PsnLogin, controller.Step);
        Assert.Equal(SetupPlatform.Xbox, controller.Platform);
        controller.Dispose();
    }

    [Fact]
    public void XboxCannotBeChosenWithoutTheXboxHelper()
    {
        var controller = AppControllerTests.Make();
        controller.ChoosePlatform(SetupPlatform.Xbox);
        Assert.Equal(SetupPlatform.Ps5, controller.PendingPlatform);
        controller.Dispose();
    }

    [Fact]
    public void ChangingThePlatformFromSettingsKeepsOtherSettingsAndShowsOnlyMissingSteps()
    {
        var controller = Ready();
        Assert.Equal(OnboardingStep.Done, controller.Step);
        controller.Settings = controller.Settings with { Style = controller.Settings.Style with { FontSize = 40 } };
        controller.BeginPlatformChange();
        Assert.True(controller.PartialSetup);
        Assert.Equal(OnboardingStep.Welcome, controller.Step);
        Assert.Equal(SetupPlatform.Ps5, controller.PendingPlatform);
        controller.ChoosePlatform(SetupPlatform.Capture);
        controller.Advance();
        // Pencere her zaman sorulur; TV zaten eşli, model kurulu: pencere seçilince kurulum biter.
        Assert.Equal(OnboardingStep.Window, controller.Step);
        controller.Settings = controller.Settings with { CaptureWindow = new WindowCaptureTarget("obs64", "Projektör") };
        controller.Advance();
        Assert.Equal(OnboardingStep.Done, controller.Step);
        Assert.False(controller.PartialSetup);
        Assert.Equal(SetupPlatform.Capture, controller.Platform);
        Assert.Equal(40, controller.Settings.Style.FontSize);
        Assert.True(controller.HasTv);
        Assert.True(controller.HasAccount);

        // Geri PS5'e: hesap ve eşleşme duruyor, hiçbir adım gerekmez.
        controller.BeginPlatformChange();
        controller.ChoosePlatform(SetupPlatform.Ps5);
        controller.Advance();
        Assert.Equal(OnboardingStep.Done, controller.Step);
        Assert.Equal(FrameSource.Ps5, controller.Settings.FrameSource);
        controller.Dispose();
    }

    [Fact]
    public void CancellingAPlatformChangeReturnsHomeWithoutChangingAnything()
    {
        var controller = Ready();
        controller.BeginPlatformChange();
        controller.ChoosePlatform(SetupPlatform.Pc);
        controller.CancelPartialSetup();
        Assert.Equal(OnboardingStep.Done, controller.Step);
        Assert.False(controller.PartialSetup);
        Assert.Equal(SetupPlatform.Ps5, controller.Platform);
        Assert.Equal(FrameSource.Ps5, controller.Settings.FrameSource);
        controller.Dispose();
    }

    [Fact]
    public void ForgettingThePs5AndPairingAnotherRunsOnlyThePairingStep()
    {
        var controller = Ready();
        controller.ForgetConsole();
        Assert.Null(controller.Console);
        Assert.True(controller.HasAccount);
        controller.RunSetupStep(OnboardingStep.Console);
        Assert.Equal(OnboardingStep.Console, controller.Step);
        Assert.True(controller.PartialSetup);
        controller.Back();
        // Hesap duruyor: geri, karşılamaya (ana ekran değil) iner.
        Assert.Equal(OnboardingStep.Welcome, controller.Step);
        controller.Dispose();
    }

    // ───── Canlı ekranın dökümü ─────

    private static SubtitleHistory.Entry Entry(int id, DateTime at, string english, string turkish, LineOrigin origin = LineOrigin.Engine, double? cps = null) =>
        new(id, at, english, turkish, origin) { ShownAt = at, Cps = cps };

    [Fact]
    public void TimecodesAreRelativeToTheSessionStart()
    {
        Assert.Equal("00:42:15,880", LiveTranscript.Timecode(new TimeSpan(0, 0, 42, 15, 880)));
        Assert.Equal("01:02:03,004", LiveTranscript.Timecode(new TimeSpan(0, 1, 2, 3, 4)));
        Assert.Equal("00:00:00,000", LiveTranscript.Timecode(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void TranscriptMarksOnScreenFastAndSourceLines()
    {
        var start = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            Entry(6, start.AddSeconds(10.5), "He still here?", "Hâlâ burada mı?"),
            Entry(5, start.AddSeconds(8), "Can never have too much storage space.", "Depolama alanı hiç fazla gelmez."),
            Entry(4, start.AddSeconds(6), "Whip him!", "Ağzını burnunu kır şunun!", LineOrigin.Correction),
            Entry(3, start.AddSeconds(4), "I'm thinking of a vacation.", "Ufak bir tatile çıkmayı düşünüyorum.", cps: 21.3),
            Entry(2, start.AddSeconds(2), "Selling that trailer?", "Karavanı satıyor musunuz?", LineOrigin.Pack),
            Entry(1, start.AddSeconds(1), "Welcome back to Vice City, bro.", "Vice City'ye tekrar hoş geldin, kanka."),
            Entry(0, start.AddSeconds(0.2), "Hi.", "Selam."),
        };
        var cues = LiveTranscript.Build(entries, start, ["Depolama alanı hiç fazla gelmez.", "Hâlâ burada mı?"], "Gemini", ["Vice City"]);
        Assert.Equal(7, cues.Count);
        Assert.Equal("00:00:10,500", cues[0].Timecode);
        Assert.True(cues[0].Current);
        Assert.Equal(("● ekranda", CueLabelKind.OnScreen), (cues[0].Label, cues[0].Kind));
        Assert.True(cues[1].OnScreen);
        Assert.False(cues[1].Current);
        Assert.Equal(("senin", CueLabelKind.Yours), (cues[2].Label, cues[2].Kind));
        Assert.Equal(("21 kr/sn · hızlı", CueLabelKind.Fast), (cues[3].Label, cues[3].Kind));
        Assert.Equal("paket", cues[4].Label);
        Assert.Equal("sözlük", cues[5].Label);
        Assert.Equal(("Gemini", CueLabelKind.Source), (cues[6].Label, cues[6].Kind));
    }

    [Fact]
    public void OnlyTheNewestCopyOfARepeatedLineIsOnScreen()
    {
        var start = DateTime.UtcNow.AddMinutes(-1);
        var entries = new[] { Entry(2, start.AddSeconds(30), "No!", "Hayır!"), Entry(1, start.AddSeconds(5), "No!", "Hayır!") };
        var cues = LiveTranscript.Build(entries, start, ["Hayır!"], "ücretsiz");
        Assert.True(cues[0].OnScreen);
        Assert.False(cues[1].OnScreen);
        Assert.Equal("ücretsiz", cues[1].Label);
        Assert.Single(LiveTranscript.Build(entries, start, [], "x", max: 1));
    }

    [Fact]
    public void GlossaryTermsMatchWholeWordsOnly()
    {
        Assert.True(LiveTranscript.ContainsTerm("Welcome back to Vice City, bro.", "vice city"));
        Assert.False(LiveTranscript.ContainsTerm("Advice City", "Vice City"));
        Assert.False(LiveTranscript.ContainsTerm("Anything", " "));
    }

    // ───── TV önizlemesi ─────

    [Fact]
    public void PreviewScalesTheRealStyleToThePreviewWidth()
    {
        var style = new SubtitleStyle(FontSize: 32, Outline: true);
        Assert.Equal(16, LivePreviewLayout.FontPx(style, 960), 3);
        Assert.Equal(12.8, LivePreviewLayout.FontPx(style, 960, current: false), 3);
        // TV'de max(2, round(32/16)) = 2 px, yarı boy önizlemede 1 px.
        Assert.Equal(1, LivePreviewLayout.OutlinePx(style, 960), 3);
        Assert.Equal(0, LivePreviewLayout.OutlinePx(style with { Outline = false }, 960));
        Assert.Equal(0, LivePreviewLayout.FontPx(style, 0));
        Assert.Equal(0.4, LivePreviewLayout.BackgroundAlpha(style with { Background = 40 }), 3);
    }

    [Fact]
    public void PreviewPlacesTheTranslationLikeTheTv()
    {
        var english = new TextBox(0.2, 0.8, 0.8, 0.9);
        var above = LivePreviewLayout.Anchor(new SubtitleStyle(Placement: SubtitlePlacement.Above), english);
        Assert.Equal(1 - 0.8 + LivePreviewLayout.Gap, above.Bottom!.Value, 6);
        Assert.Null(above.Top);
        var cover = LivePreviewLayout.Anchor(new SubtitleStyle(Placement: SubtitlePlacement.Cover), english);
        Assert.Equal(0.85, cover.Center!.Value, 6);
        var fixedAnchor = LivePreviewLayout.Anchor(new SubtitleStyle(Placement: SubtitlePlacement.Fixed, BottomPercent: 7), english);
        Assert.Equal(0.07, fixedAnchor.Bottom!.Value, 6);
        // Alttaki satırın altında yer yoksa üstüne geçilir; yer bilinmiyorsa sabit yükseklik.
        var below = LivePreviewLayout.Anchor(new SubtitleStyle(Placement: SubtitlePlacement.Below), new TextBox(0.2, 0.85, 0.8, 0.95));
        Assert.NotNull(below.Bottom);
        Assert.Equal(0.07, LivePreviewLayout.Anchor(new SubtitleStyle(Placement: SubtitlePlacement.Above, BottomPercent: 7), null).Bottom!.Value, 6);
        Assert.True(above.Left >= LivePreviewLayout.SideMin && above.Right >= LivePreviewLayout.SideMin);
    }

    [Fact]
    public void LookSummaryNamesPlacementSizeAndOutline()
    {
        Assert.Equal("İngilizcenin üstünde · 32 px · kenarlık 2 px", LivePreviewLayout.Summary(new SubtitleStyle()));
        Assert.Equal("İngilizcenin üstüne · 46 px · zemin %60",
            LivePreviewLayout.Summary(new SubtitleStyle(FontSize: 46, Placement: SubtitlePlacement.Cover, Background: 60, Outline: false)));
    }
}
