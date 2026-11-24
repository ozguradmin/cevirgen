// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.Core.LgTv;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell.Tests;

/// <summary>Çevirgen: denetleyicinin düzeltme, sözlük, oturum özeti, SRT ve gizle/göster yöntemleri (arayüzden bağımsız).</summary>
public sealed class CevirgenControllerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-ctl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static AppController Ready(FakeServices services, MemoryStore? store = null)
    {
        store ??= new MemoryStore();
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

    [Fact]
    public async Task CorrectionReplacesTheEngineAndIsMarkedInHistory()
    {
        var services = new FakeServices { ReaderLines = Reading.FromText("Hold the gate"), DataFolder = folder };
        var controller = Ready(services);
        var changed = new List<string?>();
        controller.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(controller.SaveCorrection("Hold the gate", "Kapıyı sen tut"));
        Assert.False(controller.SaveCorrection("Hold the gate", ""));
        Assert.Contains(nameof(AppController.Corrections), changed);
        Assert.Equal("Kapıyı sen tut", controller.CorrectionFor("hold the gate!")?.Turkish);
        Assert.True(File.Exists(Path.Combine(folder, UserCorrections.FileName)));

        await controller.StartAsync();
        await FeedUntil(services, () => controller.History.Count == 1);
        var entry = controller.History[0];
        Assert.Equal(("Kapıyı sen tut", LineOrigin.Correction, true), (entry.Translation, entry.Origin, entry.IsCorrection));
        Assert.Equal(1, controller.SessionStats.Corrections);
        Assert.Equal(1, controller.SessionStats.Lines);
        controller.Stop();

        // Yeniden açılınca düzeltme dosyadan gelir; silinince dosyadan da kalkar.
        var reopened = Ready(new FakeServices { DataFolder = folder });
        Assert.Equal("Hold the gate", Assert.Single(reopened.Corrections).English);
        Assert.True(reopened.RemoveCorrection("Hold the gate"));
        Assert.False(reopened.RemoveCorrection("Hold the gate"));
        Assert.Empty(Ready(new FakeServices { DataFolder = folder }).Corrections);
        controller.Dispose();
        reopened.Dispose();
    }

    [Fact]
    public async Task GlossaryRulesReachTheAiInstructionAndRebuildTheTranslator()
    {
        var services = new FakeServices { DataFolder = folder };
        var store = new MemoryStore();
        var controller = Ready(services, store);
        await controller.SetAiKeyAsync(AiProvider.Claude, "sk-good");
        controller.Settings = controller.Settings with { AiInstruction = "Be blunt." };
        await controller.StartAsync();
        Assert.Equal("Be blunt.", services.AiInstructions[^1]);

        Assert.True(controller.SaveGlossaryTerm("Vice City", GlossaryRule.Keep));
        Assert.True(controller.SaveGlossaryTerm("heist", GlossaryRule.Always, "soygun"));
        Assert.False(controller.SaveGlossaryTerm("score", GlossaryRule.Always));
        Assert.Equal(["heist", "Vice City"], controller.GlossaryTerms.Select(term => term.English));
        // Çeviri sürerken yapay zekâ çevirmeni yeni talimatla kurulur.
        var instruction = services.AiInstructions[^1]!;
        Assert.StartsWith("Be blunt.\n\nGlossary", instruction);
        Assert.Contains("\"Vice City\"", instruction);
        Assert.Contains("\"soygun\"", instruction);
        // Ücretsiz motor ve DeepL aynı terimleri genel sözlük üstünden korur (uygulamanın sözlüğü GlossaryStore.UserTerms;
        // testler paralel çalıştığından burada başka denetleyici değiştirmiş olabilir, ayrıca Core testlerinde denenir).

        Assert.True(controller.RemoveGlossaryTerm("heist"));
        Assert.DoesNotContain("soygun", services.AiInstructions[^1]!);
        Assert.True(File.Exists(Path.Combine(folder, UserGlossary.FileName)));
        controller.Dispose();
    }

    [Fact]
    public async Task SessionLinesGetTimesSpeedAndExportAsSrt()
    {
        var services = new FakeServices { ReaderLines = Reading.FromText("Hold the gate"), DataFolder = folder };
        var controller = Ready(services);
        await controller.StartAsync();
        Assert.NotNull(controller.SessionStartedAt);
        await FeedUntil(services, () => controller.History.Count == 1);
        // İngilizce satır kalkar (bölge boşalır): satırın ekranda kaldığı süre ve okuma hızı yazılır.
        services.ReaderLines = null;
        var empty = new byte[64 * 64 * 4];
        Array.Fill(empty, (byte)255);
        for (var i = 0; i < 250 && controller.History[0].LeftAt is null; i++)
        {
            services.Streams[^1].Emit(new Frame(empty, 64, 64, 256));
            await Task.Delay(20);
        }
        var entry = controller.History[0];
        Assert.NotNull(entry.ShownAt);
        Assert.NotNull(entry.LeftAt);
        Assert.True(entry.LeftAt >= entry.ShownAt);
        Assert.NotNull(controller.SessionStats.AverageLatencyMs);

        var turkish = Path.Combine(folder, "tr.srt");
        var both = Path.Combine(folder, "iki.srt");
        Assert.Equal(1, controller.ExportSrt(turkish));
        Assert.Equal(1, controller.ExportSrt(both, SrtExport.Kind.Bilingual));
        Assert.Matches(@"^﻿?1\r\n00:00:\d\d,\d{3} --> 00:00:\d\d,\d{3}\r\nTR: Hold the gate\r\n\r\n$", File.ReadAllText(turkish));
        Assert.Contains("Hold the gate\r\nTR: Hold the gate\r\n", File.ReadAllText(both));
        controller.Dispose();
    }

    [Fact]
    public async Task HideSubtitlesTogglesWithoutStoppingTranslation()
    {
        var services = new FakeServices { ReaderLines = Reading.FromText("Hold the gate") };
        var controller = Ready(services);
        await controller.StartAsync();
        var changed = new List<string?>();
        controller.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(controller.SubtitlesHidden);
        controller.ToggleSubtitlesHidden();
        Assert.True(controller.SubtitlesHidden);
        Assert.Contains(nameof(AppController.SubtitlesHidden), changed);
        // Çeviri sürer: gizliyken de satırlar çevrilir ve geçmişe girer.
        await FeedUntil(services, () => controller.History.Count == 1);
        Assert.True(controller.IsRunning);
        controller.ToggleSubtitlesHidden();
        Assert.False(controller.SubtitlesHidden);
        controller.Dispose();
    }
}
