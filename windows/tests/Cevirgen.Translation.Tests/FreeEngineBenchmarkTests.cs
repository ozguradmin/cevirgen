// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using Cevirgen.Core;

namespace Cevirgen.Translation.Tests;

/// <summary>
/// Ücretsiz motorun gerçek satırlarla ölçümü. İngilizce satırlar depoda olmadığından yalnız
/// <c>COUCHSUBS_MT_BENCH=&lt;satırlar.txt&gt;</c> verilince çalışır (model: <c>COUCHSUBS_TEST_MODELS</c>, çalışma zamanı:
/// <c>COUCHSUBS_CT2_RUNTIME</c>). Her satır uygulamadaki hattan geçer (cümle bölme, bağlam, terimler, düzeltmeler);
/// sonuç <c>&lt;satırlar.txt&gt;.&lt;COUCHSUBS_MT_BENCH_TAG&gt;.tsv</c> dosyasına yazılır.
/// </summary>
public class FreeEngineBenchmarkTests
{
    [Fact]
    public async Task TranslatesBenchmarkLines()
    {
        var path = Environment.GetEnvironmentVariable("COUCHSUBS_MT_BENCH");
        if (string.IsNullOrEmpty(path) || !TestModels.ModelAvailable) return;
        var tag = Environment.GetEnvironmentVariable("COUCHSUBS_MT_BENCH_TAG") ?? "out";
        // Çevirgen: COUCHSUBS_MT_BENCH_THREADS (varsayılan 4) ve COUCHSUBS_MT_BENCH_FAST=1 (hızlı mod).
        var threads = int.TryParse(Environment.GetEnvironmentVariable("COUCHSUBS_MT_BENCH_THREADS"), out var count) ? count : 4;
        using var translator = new MarianTranslator(() => new Ct2MarianEngine(TestModels.Directory, 128, 4, false, null,
            cpuThreads: threads), () => true, glossary: GlossaryStore.BuiltInOnly())
        {
            Fast = Environment.GetEnvironmentVariable("COUCHSUBS_MT_BENCH_FAST") == "1",
        };
        await translator.WarmUpAsync();
        var lines = File.ReadAllLines(path).Where(line => line.Trim().Length > 0).ToList();
        var rows = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (var line in lines)
        {
            var watch = Stopwatch.StartNew();
            var translated = await translator.TranslateAsync(line);
            rows.Add($"{watch.Elapsed.TotalMilliseconds:F0}\t{line}\t{translated.Replace("\n", " / ")}");
        }
        rows.Add($"# {lines.Count} lines, {total.Elapsed.TotalMilliseconds / lines.Count:F0} ms/line");
        File.WriteAllLines($"{path}.{tag}.tsv", rows);
    }
}
