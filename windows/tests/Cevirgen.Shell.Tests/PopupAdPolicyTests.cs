// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Shell.Tests;

/// <summary>Açılır reklam: günde bir, yalnız açılışta ya da çeviri durunca; çeviride, kurulumda, yayın penceresinde, görselsiz asla.</summary>
public class PopupAdPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 15, 0, 0, DateTimeKind.Local);

    [Fact]
    public void ShownOncePerDay()
    {
        Assert.True(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, null, false, true, false, true));
        Assert.False(PopupAdPolicy.ShouldShow(PopupAdMoment.TranslationStopped, Now, Now.AddHours(-2), false, true, false, true));
        Assert.True(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, Now.AddDays(-1), false, true, false, true));
    }

    [Fact]
    public void NeverWhileTranslatingInSetupOrStreamWindowOrWithoutAd()
    {
        Assert.False(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, null, running: true, true, false, true));
        Assert.False(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, null, false, setupDone: false, false, true));
        Assert.False(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, null, false, true, streamWindowOpen: true, true));
        Assert.False(PopupAdPolicy.ShouldShow(PopupAdMoment.AppLaunch, Now, null, false, true, false, hasAd: false));
    }

    /// <summary>Görsel adı ya da geçerli https bağlantısı olmayan reklam atlanır; dosya bozuksa ya da popups yoksa liste boş.</summary>
    [Fact]
    public void ParseSkipsAdsWithoutImageOrLink()
    {
        const string json = """
            {"popups":[{"id":"a","image":"a.jpg","link":"https://enmera.com.tr"},{"id":"b","image":"","link":"https://enmera.com.tr"},
                       {"id":"c","link":"https://enmera.com.tr"},{"id":"d","image":"d.jpg","link":"bozuk"}],"ads":[]}
            """;
        Assert.Equal(new[] { "a" }, PopupAdPolicy.Parse(json).Select(ad => ad.Id));
        Assert.Empty(PopupAdPolicy.Parse(null));
        Assert.Empty(PopupAdPolicy.Parse("bozuk"));
        Assert.Empty(PopupAdPolicy.Parse("""{"ads":[]}"""));
        Assert.Null(PopupAdPolicy.Pick(Array.Empty<PopupAdEntry>(), Now));
    }

    /// <summary>Çevirgen: uygulamayla gelen reklam listesi boş; üst projenin reklamları gösterilmez.</summary>
    [Fact]
    public void ShippedCatalogHasNoUpstreamAds()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "design", "yeni-arayuz", "reklamlar.json"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var design = Path.Combine(folder!.FullName, "design", "yeni-arayuz");
        Assert.Empty(PopupAdPolicy.Parse(File.ReadAllText(Path.Combine(design, "reklamlar.json"))));
    }
}
