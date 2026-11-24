// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.Core.LgTv;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell.Tests;

/// <summary>
/// Çevirgen 1.3 düzeltmeleri: tek durum renk modeli (yeşil hazır, sarı bekleniyor, kırmızı hata, gri kapalı), kör testte
/// yalnız hazır motorlar ve Türkçe hata cümleleri, "Özel (OpenAI uyumlu)" motor.
/// </summary>
public sealed class CevirgenPolishTests
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

    // ───── Durum renkleri ─────

    [Fact]
    public void SourceStates()
    {
        Assert.Equal(DeviceState.Off, DeviceStates.Source(demo: false, configured: false, running: false, SessionPhase.Disconnected, retryStopped: false));
        // Eşleşmiş PS5 çeviri durmuşken hazırdır (başlayınca bağlanır).
        Assert.Equal(DeviceState.Ready, DeviceStates.Source(false, true, running: false, SessionPhase.Disconnected, false));
        Assert.Equal(DeviceState.Waiting, DeviceStates.Source(false, true, running: true, SessionPhase.Connecting, false));
        Assert.Equal(DeviceState.Waiting, DeviceStates.Source(false, true, running: true, SessionPhase.Disconnected, retryStopped: false));
        Assert.Equal(DeviceState.Error, DeviceStates.Source(false, true, running: true, SessionPhase.Disconnected, retryStopped: true));
        Assert.Equal(DeviceState.Ready, DeviceStates.Source(false, true, running: true, SessionPhase.Streaming, false));
        Assert.Equal(DeviceState.Ready, DeviceStates.Source(demo: true, configured: false, running: false, SessionPhase.Disconnected, false));
    }

    [Fact]
    public void TvWaitingIsNeverGreen()
    {
        // Kullanıcının bildirdiği hata: "TV uygulaması bekleniyor" yeşil noktayla gösteriliyordu.
        Assert.Equal(DeviceState.Waiting, DeviceStates.Tv(showsOnTv: true, hasTv: true, running: true, clients: 0, appOutdated: false));
        Assert.Equal(DeviceState.Ready, DeviceStates.Tv(true, true, running: true, clients: 1, appOutdated: false));
        Assert.Equal(DeviceState.Ready, DeviceStates.Tv(true, true, running: false, clients: 0, appOutdated: false));
        Assert.Equal(DeviceState.Waiting, DeviceStates.Tv(true, true, running: false, clients: 1, appOutdated: true));
        Assert.Equal(DeviceState.Off, DeviceStates.Tv(true, hasTv: false, running: false, clients: 0, appOutdated: false));
        Assert.Equal(DeviceState.Ready, DeviceStates.Tv(showsOnTv: false, hasTv: false, running: true, clients: 0, appOutdated: false));
    }

    [Fact]
    public void EngineStates()
    {
        Assert.Equal(DeviceState.Ready, DeviceStates.Engine(failed: false, needsDownload: false, configured: true));
        Assert.Equal(DeviceState.Waiting, DeviceStates.Engine(failed: false, needsDownload: true, configured: true));
        Assert.Equal(DeviceState.Waiting, DeviceStates.Engine(failed: false, needsDownload: false, configured: false));
        Assert.Equal(DeviceState.Error, DeviceStates.Engine(failed: true, needsDownload: false, configured: true));
        Assert.Equal(DeviceState.Off, DeviceStates.EngineChoice(configured: false, needsDownload: false));
        Assert.Equal(DeviceState.Waiting, DeviceStates.EngineChoice(configured: true, needsDownload: true));
        Assert.Equal(["hazır", "bekleniyor", "hata", "kapalı"],
            new[] { DeviceState.Ready, DeviceState.Waiting, DeviceState.Error, DeviceState.Off }.Select(state => state.Name()));
    }

    [Fact]
    public async Task ControllerStatesFollowTheSession()
    {
        var services = new FakeServices();
        var controller = Ready(services);
        Assert.Equal(DeviceState.Ready, controller.SourceState);
        Assert.Equal(DeviceState.Ready, controller.TvState);
        Assert.Equal(DeviceState.Ready, controller.EngineState);
        // Çeviri durmuşken TV için "bekleniyor" denmez.
        Assert.DoesNotContain("Waiting", controller.TvStatusText);

        await controller.StartAsync();
        Assert.Equal(DeviceState.Waiting, controller.TvState);
        Assert.Equal("Waiting for the TV app", controller.TvStatusText);
        controller.Stop();

        services.Translation.Installed = false;
        Assert.Equal(DeviceState.Waiting, controller.EngineState);
        Assert.Equal(DeviceState.Waiting, controller.EngineChoiceState(TranslationEngine.Free));
        Assert.Equal(DeviceState.Off, controller.EngineChoiceState(TranslationEngine.Gemini));
        controller.Dispose();
    }

    // ───── Kör test ─────

    [Fact]
    public void LocalServerJoinsTheBlindTestOnlyWhenChosen()
    {
        // Varsayılan Ollama adresi her zaman "geçerli"dir; sunucu yokken bütün satırları "çevrilemedi" olan sütun çıkıyordu.
        var controller = Ready();
        Assert.True(controller.IsReady(TranslationEngine.Local));
        Assert.DoesNotContain(controller.BlindTestColumns(), column => column.Engine == TranslationEngine.Local);
        controller.UseEngine(TranslationEngine.Local);
        Assert.Contains(controller.BlindTestColumns(), column => column.Engine == TranslationEngine.Local);
        controller.Dispose();
    }

    [Fact]
    public async Task BlindTestNeverShowsRawErrors()
    {
        var services = new FakeServices();
        var store = new MemoryStore();
        var controller = Ready(services, store);
        services.Translation.Installed = false;
        Assert.DoesNotContain(controller.BlindTestColumns(), column => column.Engine == TranslationEngine.Free);
        var free = await controller.TranslateSamplesAsync(TranslationEngine.Free, BlindTest.Lines);
        Assert.True(free.Failed);
        Assert.Equal("Ücretsiz model henüz inmedi.", free.Error);

        // Yedeksiz çevrimiçi çevirmen çeviremeyince TranslatorException(NeedsDownload) fırlatır; ekrana İngilizce ham metin çıkmaz.
        Assert.Equal("Ücretsiz model henüz inmedi.", controller.SampleFailure(TranslationEngine.Free, new TranslatorException(TranslatorStatus.NeedsDownload), null));
        var online = controller.SampleFailure(TranslationEngine.Gemini, new TranslatorException(TranslatorStatus.NeedsDownload), null);
        Assert.DoesNotContain("Translator not available", online);
        Assert.DoesNotContain("NeedsDownload", online);
        controller.Dispose();
    }

    // ───── Özel (OpenAI uyumlu) motor ─────

    [Fact]
    public void CustomUrlGetsHttpsUnlessLocal()
    {
        Assert.Equal("https://openrouter.ai/api/v1", UserSettings.CustomUrl(" openrouter.ai/api/v1 "));
        Assert.Equal("http://localhost:1234/v1", UserSettings.CustomUrl("localhost:1234/v1"));
        Assert.Equal("http://192.168.1.5:11434", UserSettings.CustomUrl("192.168.1.5:11434"));
        Assert.Equal("https://api.groq.com/openai/v1", UserSettings.CustomUrl("https://api.groq.com/openai/v1"));
        Assert.Equal("", UserSettings.CustomUrl("  "));

        var settings = new UserSettings();
        Assert.Null(settings.CustomAiServer);
        settings = settings with { CustomAiBaseUrl = "openrouter.ai/api/v1/chat/completions", CustomAiModel = " google/gemma-3-27b-it " };
        var server = Assert.IsType<LocalAiServer>(settings.CustomAiServer);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", server.ChatUrl.AbsoluteUri);
        Assert.Equal("google/gemma-3-27b-it", server.Model);
        Assert.Equal(settings.CustomAiServer?.Model, settings.ServerFor(TranslationEngine.Custom)?.Model);
        Assert.Null(settings.ServerFor(TranslationEngine.Gemini));
        Assert.Equal(StoreKeys.CustomAiKey, TranslationEngine.Custom.StoreKey());
        Assert.Equal("Özel (OpenAI uyumlu)", TranslationEngine.Custom.DisplayName());
        Assert.True(TranslationEngine.Custom.UsesServer());
    }

    [Fact]
    public async Task CustomEndpointTranslatesWithKeyGlossaryAndInstruction()
    {
        var services = new FakeServices();
        var store = new MemoryStore();
        var controller = Ready(services, store);
        Assert.False(controller.IsReady(TranslationEngine.Custom));
        Assert.Equal(OnlineKeyState.Unreachable, (await controller.TestCustomAiAsync()).State);

        controller.Settings = controller.Settings with
        {
            CustomAiBaseUrl = "https://api.groq.com/openai/v1", CustomAiModel = "llama-3.3-70b-versatile", AiInstruction = "Küfürleri yumuşatma.",
        };
        controller.SaveGlossaryTerm("Vice City", GlossaryRule.Keep);
        Assert.True(controller.IsReady(TranslationEngine.Custom));
        Assert.Contains(controller.BlindTestColumns(), column => column.Engine == TranslationEngine.Custom);

        // Yanlış anahtar saklanmaz, motor seçilmez.
        services.AiTest = new AiTestResult(OnlineKeyState.Invalid);
        Assert.Equal(OnlineKeyState.Invalid, (await controller.TestCustomAiAsync("wrong")).State);
        Assert.False(controller.HasKey(TranslationEngine.Custom));
        Assert.Equal(TranslationEngine.Free, controller.Settings.Engine);
        Assert.Contains("anahtarı kabul etmedi", OnlineKeyState.Invalid.Describe(TranslationEngine.Custom, Localizer.Current, controller.Settings.CustomAiServer));

        // Çalışınca anahtar şifreli depoya yazılır ve motor seçilir.
        services.AiTest = new AiTestResult(OnlineKeyState.Valid, "Merhaba");
        Assert.Equal(OnlineKeyState.Valid, (await controller.TestCustomAiAsync(" gsk-key ")).State);
        Assert.Equal("gsk-key", store.Load<string>(StoreKeys.CustomAiKey));
        Assert.Equal(TranslationEngine.Custom, controller.Settings.Engine);
        Assert.True(controller.OnlineTranslationActive);
        var (tested, key) = services.LocalTests[^1];
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", tested.ChatUrl.AbsoluteUri);
        Assert.Equal("gsk-key", key);

        services.AiInstructions.Clear();
        await controller.StartAsync();
        var made = Assert.Single(services.LocalTranslators);
        Assert.Equal("llama-3.3-70b-versatile", made.Server.Model);
        Assert.Equal("gsk-key", made.Key);
        Assert.Empty(services.AiTranslators);
        // Sözlük kuralları ve ek talimat yapay zekâ motorlarındaki gibi gider.
        var instruction = Assert.Single(services.AiInstructions);
        Assert.Contains("Küfürleri yumuşatma.", instruction);
        Assert.Contains("Vice City", instruction);
        controller.Dispose();
    }
}
