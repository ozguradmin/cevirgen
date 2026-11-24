// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Shell;
using Xunit;

namespace Cevirgen.Shell.Tests;

public sealed class GameAreaDefaultsTests
{
    [Theory]
    [InlineData("Grand Theft Auto VI")]
    [InlineData("GTA 6")]
    public void Gta6HasSubtitleHelpAndOptionsAreas(string game)
    {
        var areas = Assert.IsType<AreaSet>(GameAreaDefaults.For(game));
        Assert.Equal([GameAreaDefaults.Gta6HelpId, GameAreaDefaults.Gta6OptionsId], areas.ExtraAreas.Select(area => area.Id));
        Assert.All(areas.ExtraAreas, area => Assert.True(area.Enabled));
        // Sağ alttaki seçenekler ekranın sağ kenarına kadar, uzun etiket ("DEFAULT", "ENABLE AUTO DRIVE") kesilmesin diye geniş.
        var options = areas.ExtraAreas[1].Region;
        Assert.Equal(1.0, options.Right);
        Assert.True(options.Left <= 0.8);
        Assert.NotNull(GameAreaDefaults.SubtitleSettingHint(game));
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
