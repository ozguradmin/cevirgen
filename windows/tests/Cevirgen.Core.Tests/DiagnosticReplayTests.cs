// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Cevirgen.Core;
using Xunit.Abstractions;

namespace Cevirgen.Core.Tests;

/// <summary>
/// Çevirgen: tanılama kaydının ek alan örnekleri gerçek hattan geçer (yalnız yerel). CEVIRGEN_TV_SAMPLES bir dizin
/// gösterirse içindeki her "AD-alan.json" (tanılama kaydının yazdığı okuma: alanın yeri ve satırlar, kutular kırpıntının
/// yüzdesi) ile "AD-alan.png" (alanın tam çözünürlüklü kırpıntısı) <see cref="RegionWatcher"/>'dan geçer: gruplama, sözlük,
/// stil. TV'ye gidecek "region" olayı "AD-olay.json"a yazılır; scripts/tv-preview.js bu olayı gerçek TV sayfasında, oyun
/// karesinin üstünde çizip ekran görüntüsünü alır. Kareler telif altındadır, depoya girmez.
/// </summary>
public class DiagnosticReplayTests(ITestOutputHelper output)
{
    private static readonly string? Root = Environment.GetEnvironmentVariable("CEVIRGEN_TV_SAMPLES");
    private const string Suffix = "-alan.json";

    public static IEnumerable<object[]> Samples() => Root is null || !Directory.Exists(Root) ? [["-"]]
        : Directory.GetFiles(Root, "*" + Suffix).Select(path => new object[] { Path.GetFileName(path)[..^Suffix.Length] });

    /// <summary>Kayıttaki okumayı döner (kutular kırpıntıya göre).</summary>
    private sealed class SavedReader(Reading reading) : ITextReader
    {
        public string Read(Frame image) => string.Join('\n', reading.Lines.Select(line => line.Text));
        public Reading ReadLines(Frame image, bool deep) => reading;
        public Reading ReadScreen(Frame image) => reading;
    }

    /// <summary>
    /// Sözlükte olmayan parça için sahte çeviri: her sözcük ters çevrilir ve bir harf uzar ("SPEED" → "DEEPSE"). Aslına
    /// benzemez (benzer çeviri gösterilmez), boyu Türkçe gibi biraz uzundur; yerleşim böyle denenir.
    /// </summary>
    private sealed class Prefixed : ITextTranslator
    {
        /// <summary>CEVIRGEN_TV_TRANSLATIONS bir JSON dosyası gösterirse ({"İngilizce": "Türkçe"}) bilinen parçalar oradan çevrilir (tanıtım görselleri).</summary>
        private static readonly Dictionary<string, string> Known =
            Environment.GetEnvironmentVariable("CEVIRGEN_TV_TRANSLATIONS") is { } file && File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [] : [];

        public List<string> Texts { get; } = [];
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
        {
            lock (Texts) Texts.Add(text);
            if (Known.TryGetValue(text, out var known)) return Task.FromResult(known);
            return Task.FromResult(string.Join(' ', text.Split(' ').Select(word =>
                word.Count(char.IsLetter) < 2 ? word : new string([.. word.Reverse()]) + (word.Any(char.IsLower) ? "e" : "E"))));
        }
    }

    private sealed class Recorder : IRegionSender
    {
        public List<SortedDictionary<string, object>> Events { get; } = [];
        public void SendRegion(string id, string text, TextBox? box, double? lineHeight = null) => SendRegion(id, text, box, lineHeight, null, null);
        public void SendRegion(string id, string text, TextBox? box, double? lineHeight, IReadOnlyList<IconPlacement>? icons,
                               IReadOnlyList<ScreenTranslation.Item>? blocks)
        {
            lock (Events) Events.Add(SubtitleServer.RegionData(id, text, box, lineHeight, icons, blocks));
        }
        public void ClearRegions() { }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task SampleThroughTheRegionLine(string name)
    {
        if (Root is null || name == "-") return;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name + Suffix)));
        var root = json.RootElement;
        var place = root.GetProperty("area").GetProperty("region");
        var region = new CropRegion(Top: Percent(place, "top"), Bottom: Percent(place, "bottom"), Left: Percent(place, "left"), Right: Percent(place, "right"));
        var lines = root.GetProperty("lines").EnumerateArray().Select(line =>
        {
            var box = line.GetProperty("inCrop");
            return new TextLine(line.GetProperty("text").GetString()!,
                new TextBox(Percent(box, "left"), Percent(box, "top"), Percent(box, "right"), Percent(box, "bottom")),
                line.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number ? confidence.GetDouble() : null);
        }).ToList();
        var crop = RealFrameTests.Png.Load(Path.Combine(Root, name + "-alan.png"));
        // Tam kare: kırpıntı alanın yerine konur (alanın dışı siyah; hat yalnız alanı okur).
        var width = (int)Math.Round(crop.Width / (region.Right - region.Left));
        var height = (int)Math.Round(crop.Height / (region.Bottom - region.Top));
        var frame = Frame.Blank(width, height);
        var x0 = (int)Math.Round(region.Left * width);
        var y0 = (int)Math.Round(region.Top * height);
        for (var y = 0; y < crop.Height && y0 + y < height; y++)
            Array.Copy(crop.Pixels, y * crop.Stride, frame.Pixels, (y0 + y) * frame.Stride + x0 * 4, Math.Min(crop.Width, width - x0) * 4);

        var translator = new Prefixed();
        var recorder = new Recorder();
        var watcher = new RegionWatcher(new SavedReader(new Reading(lines)), translator, recorder)
        {
            TranslatesUiCommands = true, Game = () => "Grand Theft Auto VI", Log = output.WriteLine,
        };
        var area = new WatchedArea(root.GetProperty("area").GetProperty("id").GetString()!, root.GetProperty("area").GetProperty("name").GetString()!, region);
        for (var pass = 0; pass < 3; pass++) await watcher.PassAsync(frame, [area]);

        var last = recorder.Events.LastOrDefault();
        var text = last is null ? "{\"id\":\"-\",\"text\":\"\"}" : SubtitleServer.Json(last);
        await File.WriteAllTextAsync(Path.Combine(Root, name + "-olay.json"), text);
        output.WriteLine($"{name}: {width}x{height}, {lines.Count} satır, {recorder.Events.Count} olay; motora giden: {string.Join(" | ", translator.Texts)}");
        output.WriteLine(text);
    }

    private static double Percent(JsonElement box, string side) => box.GetProperty(side).GetDouble() / 100;
}
