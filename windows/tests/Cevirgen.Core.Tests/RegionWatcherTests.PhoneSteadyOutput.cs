// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>
/// GTA 6 telefonunun çevirisi kararlı (X, 22 Kas: "sağ tarafta kutu çıkarıyorsun ama her saniye şekli de hareketi de
/// değişiyor"): aynı ekran gürültülü okunsa da, telefon kapanıp açılsa da çizilen Türkçe birebir aynı kalır, mesaj yeniden
/// çevrilmez. Mac (PhoneSteadyOutputTests.swift) ve Android (PhoneSteadyOutputTest.kt) aynı durumları sınar. Metinler uydurmadır.
/// </summary>
public partial class RegionWatcherTests
{
    [Fact]
    public async Task NoisyPhoneReadingsAndReopeningKeepTheSameTranslation()
    {
        const string message = "Are you coming to the barbecue tonight?";
        var reader = new Reader { Lines = [Line(message)] };
        var server = new Server();
        var calls = 0;
        // Her çağrıda başka Türkçe veren motor (yapay zekâ gibi).
        var translator = new Translator { Map = text => $"Çeviri {Interlocked.Increment(ref calls)}: {text}" };
        var watcher = new RegionWatcher(reader, translator, server);
        using var first = new CancellationTokenSource();
        var open = Phone with { PanelLifetime = first.Token };
        await watcher.PassAsync(Shade(10), [open]);
        await watcher.PassAsync(Shade(10), [open]);
        var shown = server.Sent[^1].Text;
        Assert.StartsWith("Çeviri 1:", shown);

        // Bir harfi oynayan, soru işareti kaçan okumalar.
        reader.Lines = [Line("Are you cominq to the barbecue tonight?")];
        await watcher.PassAsync(Shade(40), [open]);
        await watcher.PassAsync(Shade(40), [open]);
        reader.Lines = [Line("Are you coming to the barbecue tonight")];
        await watcher.PassAsync(Shade(70), [open]);
        await watcher.PassAsync(Shade(70), [open]);

        // Telefon kapanıp yeniden açıldı: hafızadaki çeviri kullanılır.
        first.Cancel();
        watcher.RemovePanel(open.Id);
        using var next = new CancellationTokenSource();
        var reopened = Phone with { PanelLifetime = next.Token };
        reader.Lines = [Line(message)];
        await watcher.PassAsync(Shade(10), [reopened]);
        await watcher.PassAsync(Shade(10), [reopened]);

        Assert.Equal(1, calls);
        Assert.All(server.Sent.Where(sent => sent.Text.Length > 0), sent => Assert.Equal(shown, sent.Text));
        Assert.Equal(shown, server.Sent[^1].Text);
    }

    [Fact]
    public async Task ResetForgetsThePhoneMemory()
    {
        var reader = new Reader { Lines = [Line("Bring the folding chairs from the garage.")] };
        var server = new Server();
        var translator = new Translator();
        var watcher = new RegionWatcher(reader, translator, server);
        using var lifetime = new CancellationTokenSource();
        var open = Phone with { PanelLifetime = lifetime.Token };
        await watcher.PassAsync(Shade(10), [open]);
        await watcher.PassAsync(Shade(10), [open]);
        watcher.Reset();
        await watcher.PassAsync(Shade(10), [open]);
        await watcher.PassAsync(Shade(10), [open]);
        Assert.Equal(2, translator.Calls.Count);
    }
}
