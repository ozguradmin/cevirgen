// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Shell;
using Xunit;

namespace Cevirgen.Shell.Tests;

public sealed class GameAreaDefaultsTests
{
    [Theory]
    [InlineData("Grand Theft Auto VI")]
    [InlineData("GTA 6")]
    public void Gta6HasSubtitleAndOptionsAreas(string game)
    {
        var areas = Assert.IsType<AreaSet>(GameAreaDefaults.For(game));
        Assert.Equal([GameAreaDefaults.Gta6OptionsId], areas.ExtraAreas.Select(area => area.Id));
        Assert.All(areas.ExtraAreas, area => Assert.True(area.Enabled));
        // Sağ alttaki seçenekler ekranın sağ kenarına kadar, uzun etiket ("DEFAULT", "ENABLE AUTO DRIVE") kesilmesin diye geniş.
        var options = areas.ExtraAreas[0].Region;
        Assert.Equal(1.0, options.Right);
        Assert.True(options.Left <= 0.8);
        Assert.NotNull(GameAreaDefaults.SubtitleSettingHint(game));
    }

    [Fact]
    public void AreaRecordingWritesFrameCropAndLinesAtMostEvery600Ms()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var now = new DateTime(2026, 11, 24, 18, 0, 0);
        var recorder = new DiagnosticRecorder(root, (_, _) => [1, 2, 3], () => now);
        var frame = new Cevirgen.Core.Frame(new byte[16 * 9 * 4], 16, 9, 16 * 4);
        var crop = new Cevirgen.Core.Frame(new byte[4 * 4 * 4], 4, 4, 4 * 4);
        var area = new Cevirgen.Core.WatchedArea("gta6-ipucu", "İpucu", new Cevirgen.Core.CropRegion(0.25, 0.40, 0.0125, 0.2365));
        var reading = new Cevirgen.Core.Reading([new Cevirgen.Core.TextLine("Hold to exit cinematic camera.",
            new Cevirgen.Core.TextBox(0.1, 0.2, 0.9, 0.4), 0.97)]);
        var info = new AreaDiagnostic(DateTimeOffset.Now, area, reading, "Hold", "Basılı tut");
        var snapshot = new DiagnosticSnapshot(DateTimeOffset.Now, new Cevirgen.Core.CropRegion(), [area], null, null, null, "GTA 6");
        Assert.False(recorder.RecordArea(frame, crop, info, snapshot));
        recorder.Start();
        Assert.True(recorder.RecordArea(frame, crop, info, snapshot));
        Assert.False(recorder.RecordArea(frame, crop, info, snapshot));
        now = now.AddMilliseconds(700);
        Assert.True(recorder.RecordArea(frame, crop, info, snapshot));
        var json = File.ReadAllText(Path.Combine(recorder.SessionFolder!, "0001-alan.json"));
        Assert.Contains("Hold to exit cinematic camera.", json);
        Assert.Contains("inFrame", json);
        Assert.True(File.Exists(Path.Combine(recorder.SessionFolder!, "0002-alan.jpg")));
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("Elden Ring")]
    [InlineData(null)]
    public void OtherGamesHaveNoDefaults(string? game)
    {
        Assert.Null(GameAreaDefaults.For(game));
        Assert.Null(GameAreaDefaults.SubtitleSettingHint(game));
    }
}
