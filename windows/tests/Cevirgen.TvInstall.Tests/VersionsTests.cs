// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.TvInstall.Tests;

public class VersionsTests
{
    [Theory]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("0.7.0", "0.7.0", false)]
    [InlineData("0.7", "0.7.0", false)]
    [InlineData("5.0", "4.0", true)]
    [InlineData("5.0", "6.5", false)]
    public void ComparisonIsNumeric(string a, string b, bool newer) => Assert.Equal(newer, Versions.IsNewer(a, b));

    [Fact]
    public void BundledFileCarriesItsVersion()
    {
        Assert.Equal("0.7.0", Versions.OfBundledFile("cevirgen-androidtv_0.7.0.apk"));
        Assert.Equal("0.7.0", Versions.OfBundledFile("couchsubs-samsung_0.7.0.wgt"));
        Assert.Null(Versions.OfBundledFile("couchsubs.apk"));
    }

    /// Çevirgen: derleme çıktısında eski APK kalsa da en yenisi gömülü sayılır (1.0.1, 1.0.0'ın önünde).
    [Fact]
    public void NewestBundledFileWins()
    {
        Assert.Equal(@"tv-app\cevirgen-androidtv_1.0.1.apk",
            Versions.Newest([@"tv-app\cevirgen-androidtv_1.0.0.apk", @"tv-app\cevirgen-androidtv_1.0.1.apk", @"tv-app\notes.apk"]));
        Assert.Equal("1.0.1", Versions.OfBundledFile(@"C:\dev\gta6\cevirgen\dist\tv-app\cevirgen-androidtv_1.0.1.apk"));
        Assert.Equal("b_0.10.0.apk", Versions.Newest(["a_0.9.9.apk", "b_0.10.0.apk"]));
        Assert.Null(Versions.Newest(["x.apk"]));
    }
}
