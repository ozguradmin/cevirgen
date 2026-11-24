// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core.Tests;

/// <summary>Uzaktan ayarlardaki oyun kapakları (<c>games</c>) ve destek alanı (<c>support</c>): ayrıştırma, indirme, önbellek.</summary>
[Collection(nameof(SubtitleLog))]
public sealed class RemoteGamesTests : IDisposable
{
    private const string Base = "https://api.test/";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-games-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private const string Games = """
        {"version":1,"ads":[],"popups":[],"announcement":null,
         "games":[
           {"id":"gta-6","title":"Grand Theft Auto VI","cover":"https://cdn.test/gta-6.jpg","aliases":["GTA 6","GTA VI",""," ",7]},
           {"id":"rdr-2","title":"Red Dead Redemption 2","cover":"http://cdn.test/rdr-2.jpg"},
           {"id":"GTA-5","title":"Büyük harfli kimlik"},
           {"id":"bos-ad","title":"  "},
           {"id":"gta-6","title":"Aynı kimlik ikinci kez"},
           {"id":"elden-ring","title":"Elden Ring","cover":"https://cdn.test/missing.jpg"},
           "nesne değil"],
         "support":{"id":"kasim","text":"Bir arkadaşına anlat.","button":"Tanıtım","link":"https://example.org/tanitim","icon":"https://cdn.test/destek.png"}}
        """;

    [Fact]
    public void GamesKeepValidIdsAndHttpsCoversOnly()
    {
        var config = RemoteConfig.Parse(Games)!;
        Assert.Equal(["gta-6", "rdr-2", "elden-ring"], config.Games.Select(game => game.Id));
        var gta = config.Games[0];
        Assert.Equal("Grand Theft Auto VI", gta.Title);
        Assert.Equal("https://cdn.test/gta-6.jpg", gta.Cover?.AbsoluteUri);
        Assert.Equal(["GTA 6", "GTA VI"], gta.Aliases);
        // http kapak yok sayılır, oyun kapaksız kalır.
        Assert.Null(config.Games[1].Cover);
        Assert.Equal(config, RemoteConfig.Parse(Games));
        Assert.Empty(RemoteConfig.Parse("""{"version":1}""")!.Games);
        Assert.Empty(RemoteConfig.Parse("""{"games":{"id":"x"}}""")!.Games);
    }

    [Fact]
    public void GameIdsAndNamesMatchLikeTheServer()
    {
        Assert.True(RemoteGame.IsValidId("gta-6"));
        Assert.True(RemoteGame.IsValidId(new string('a', 64)));
        Assert.False(RemoteGame.IsValidId(new string('a', 65)));
        Assert.False(RemoteGame.IsValidId("GTA-6"));
        Assert.False(RemoteGame.IsValidId("gta 6"));
        Assert.False(RemoteGame.IsValidId(""));
        var gta = new RemoteGame("gta-6", "Grand Theft Auto VI", null, ["GTA VI Online"]);
        Assert.True(gta.Matches("GTA 6"));
        Assert.True(gta.Matches("grand theft auto vi"));
        Assert.True(gta.Matches("GTA VI Online"));
        Assert.False(gta.Matches("GTA 5"));
        Assert.False(gta.Matches(""));
    }

    [Fact]
    public void SupportIsReadOnlyWhenComplete()
    {
        var support = RemoteConfig.Parse(Games)!.Support!;
        Assert.Equal(("kasim", "Bir arkadaşına anlat.", "Tanıtım", "https://example.org/tanitim", "https://cdn.test/destek.png"),
            (support.Id, support.Text, support.Button, support.Link.AbsoluteUri, support.Icon?.AbsoluteUri));
        Assert.Null(RemoteConfig.Parse("""{"support":null}""")!.Support);
        Assert.Null(RemoteConfig.Parse("""{}""")!.Support);
        Assert.Null(RemoteConfig.Parse("""{"support":{"text":"x","button":"y","link":"http://example.org"}}""")!.Support);
        Assert.Null(RemoteConfig.Parse("""{"support":{"text":"","button":"y","link":"https://example.org"}}""")!.Support);
        Assert.Null(RemoteConfig.Parse("""{"support":{"text":"x","link":"https://example.org"}}""")!.Support);
        Assert.Null(RemoteConfig.Parse("""{"support":"metin"}""")!.Support);
        // http simge yok sayılır; alan simgesiz kalır. Kimlik yoksa yazı kimlik olur.
        var plain = RemoteConfig.Parse("""{"support":{"text":"x","button":"y","link":"https://example.org","icon":"http://cdn.test/a.png"}}""")!.Support!;
        Assert.Null(plain.Icon);
        Assert.Equal("x", plain.Id);
    }

    [Fact]
    public async Task CoversAndSupportIconAreDownloadedCachedAndPruned()
    {
        var server = new FakeCevirgenServer();
        server.Routes["https://cdn.test/gta-6.jpg"] = () => FakeCevirgenServer.Bytes([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        server.Routes["https://cdn.test/destek.png"] = () => FakeCevirgenServer.Bytes(FakeCevirgenServer.Png);
        using var client = new RemoteConfigClient(new Uri(Base), folder, server);
        var config = RemoteConfig.Parse(Games)!;

        var offline = await client.ResolveAsync(config, download: false);
        Assert.Equal(3, offline.Games.Count);
        Assert.All(offline.Games, game => Assert.Null(game.CoverFile));
        Assert.Null(offline.Support!.IconFile);

        var content = await client.ResolveAsync(config, download: true);
        Assert.Equal(["gta-6", "rdr-2", "elden-ring"], content.Games.Select(game => game.Game.Id));
        Assert.True(File.Exists(content.Games[0].CoverFile));
        // http kapak hiç istenmez; inmeyen kapak oyunu listeden düşürmez.
        Assert.Equal(0, server.CountFor("http://cdn.test/rdr-2.jpg"));
        Assert.Null(content.Games[2].CoverFile);
        Assert.True(File.Exists(content.Support!.IconFile));
        Assert.Equal(content, await client.ResolveAsync(config, download: true));
        Assert.Equal(1, server.CountFor("https://cdn.test/gta-6.jpg"));

        // Oyun ve destek alanı sunucudan kalkınca görselleri silinir.
        await client.ResolveAsync(RemoteConfig.Empty, download: true);
        Assert.False(File.Exists(content.Games[0].CoverFile));
        Assert.False(File.Exists(content.Support.IconFile));
    }
}
