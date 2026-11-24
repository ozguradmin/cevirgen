// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text;
using System.Text.Json;
using Cevirgen.Core;
using Cevirgen.Core.LgTv;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell.Tests;

/// <summary>Çevirgen sunucusunun sahtesi (uzaktan ayarlar, görseller, sayım).</summary>
internal sealed class FakeApi : HttpMessageHandler
{
    public const string Base = "https://api.test/";
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public string Config { get; set; } = """{"version":1,"ads":[],"popups":[],"announcement":null}""";
    public bool Offline { get; set; }
    public List<(Uri Url, string Body)> Requests { get; } = [];

    public List<JsonElement> Pings
    {
        get
        {
            lock (Requests)
                return Requests.Where(r => r.Url.AbsoluteUri == Base + "v1/ping").Select(r => JsonDocument.Parse(r.Body).RootElement.Clone()).ToList();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add((request.RequestUri!, body));
        if (Offline) throw new HttpRequestException("çevrimdışı");
        var url = request.RequestUri!.AbsoluteUri;
        if (url == Base + "v1/config") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Config, Encoding.UTF8, "application/json") };
        if (url == Base + "v1/ping") return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (url.StartsWith("https://cdn.test/") && !url.Contains("missing")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static string ConfigWith(string announcementId, string adId = "a1") => $$$"""
        {"version":1,
         "ads":[{"id":"{{{adId}}}","title":"Çevirgen TV","text":"TV'de altyazı","icon":"https://cdn.test/{{{adId}}}.png","link":"https://example.org/tv"},
                {"id":"broken","title":"Görselsiz","text":"x","icon":"https://cdn.test/missing.png","link":"https://example.org/x"}],
         "popups":[{"id":"p1","image":"https://cdn.test/popup.png","link":"https://example.org/p"}],
         "announcement":{"id":"{{{announcementId}}}","text":"Duyuru {{{announcementId}}}","link":"https://example.org/duyuru"}}
        """;
}

/// <summary>Çevirgen: denetleyicinin uzaktan ayarları, duyurusu ve anonim sayımı (arayüzden bağımsız).</summary>
public sealed class CevirgenRemoteTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-remote-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> owned = [];

    public void Dispose()
    {
        foreach (var item in owned) item.Dispose();
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private FakeServices Services(FakeApi api, bool remote = true, bool counter = false)
    {
        var services = new FakeServices { DataFolder = folder };
        if (remote)
        {
            services.RemoteConfig = new RemoteConfigClient(new Uri(FakeApi.Base), folder, api);
            owned.Add(services.RemoteConfig);
        }
        if (counter)
        {
            services.Counter = new UsageCounter(new Uri(FakeApi.Base), Path.Combine(folder, UsageCounter.IdFileName), "1.1.0", handler: api);
            owned.Add(services.Counter);
        }
        return services;
    }

    /// <param name="own">false: testin kendisi kapatır (Dispose iki kez çağrılmasın).</param>
    private AppController Make(FakeServices services, MemorySettings? settings = null, bool own = true)
    {
        var controller = AppControllerTests.Make(services, settings: settings);
        if (own) owned.Add(controller);
        return controller;
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs && !condition(); waited += 20) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task ServerContentReachesAdsPopupsAndTheAnnouncement()
    {
        var api = new FakeApi { Config = FakeApi.ConfigWith("d1") };
        var controller = Make(Services(api));
        var changed = new List<string?>();
        var announcements = new List<Announcement?>();
        controller.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        controller.AnnouncementChanged += announcements.Add;

        await controller.RefreshRemoteConfigAsync();
        await Until(() => controller.CurrentAnnouncement is not null);
        // Görseli inmeyen reklam atlanır.
        var ad = Assert.Single(controller.RemoteAds);
        Assert.Equal(("a1", "Çevirgen TV", "https://example.org/tv"), (ad.Ad.Id, ad.Ad.Title, ad.Ad.Link.AbsoluteUri));
        Assert.True(File.Exists(ad.IconFile));
        Assert.Equal("p1", Assert.Single(controller.RemotePopups).Popup.Id);
        Assert.Equal("d1", controller.CurrentAnnouncement!.Id);
        Assert.Contains(nameof(AppController.RemoteAds), changed);
        Assert.Contains(nameof(AppController.RemotePopups), changed);
        Assert.Contains(nameof(AppController.CurrentAnnouncement), changed);
        Assert.Equal("d1", Assert.Single(announcements)!.Id);
        Assert.True(File.Exists(Path.Combine(folder, RemoteConfig.CacheFileName)));

        Uri? opened = null;
        controller.OpenInBrowser = url => opened = url;
        controller.OpenAnnouncement();
        Assert.Equal("https://example.org/duyuru", opened?.AbsoluteUri);
    }

    [Fact]
    public async Task OfflineStartUsesTheCacheWithoutWaitingForTheNetwork()
    {
        var api = new FakeApi { Config = FakeApi.ConfigWith("d1") };
        var first = Make(Services(api), own: false);
        await first.RefreshRemoteConfigAsync();
        await Until(() => first.RemoteAds.Count == 1);
        first.Dispose();

        api.Offline = true;
        var second = Make(Services(api));
        // Önbellek kurucu biter bitmez hazır.
        Assert.Equal("a1", Assert.Single(second.RemoteAds).Ad.Id);
        Assert.Single(second.RemotePopups);
        Assert.Equal("d1", second.CurrentAnnouncement?.Id);
        await second.RefreshRemoteConfigAsync();
        Assert.Single(second.RemoteAds);

        // Çevrimiçi olunca yeni içerik gelir, kalkan reklam düşer.
        api.Offline = false;
        api.Config = FakeApi.ConfigWith("d2", adId: "a2");
        await second.RefreshRemoteConfigAsync();
        await Until(() => second.CurrentAnnouncement?.Id == "d2");
        Assert.Equal("a2", Assert.Single(second.RemoteAds).Ad.Id);
    }

    [Fact]
    public async Task DismissedAnnouncementStaysDismissedAcrossRestarts()
    {
        var api = new FakeApi { Config = FakeApi.ConfigWith("d1") };
        var controller = Make(Services(api), own: false);
        var announcements = new List<Announcement?>();
        controller.AnnouncementChanged += announcements.Add;
        await controller.RefreshRemoteConfigAsync();
        await Until(() => controller.CurrentAnnouncement is not null);

        controller.DismissAnnouncement("d1");
        Assert.Null(controller.CurrentAnnouncement);
        Assert.Equal([ "d1", null ], announcements.Select(a => a?.Id));
        Assert.True(File.Exists(Path.Combine(folder, AppController.DismissedAnnouncementsFileName)));
        controller.Dispose();

        var restarted = Make(Services(api));
        Assert.Null(restarted.CurrentAnnouncement);
        await restarted.RefreshRemoteConfigAsync();
        Assert.Null(restarted.CurrentAnnouncement);
        // Yeni bir duyuru gösterilir.
        api.Config = FakeApi.ConfigWith("d2");
        await restarted.RefreshRemoteConfigAsync();
        await Until(() => restarted.CurrentAnnouncement?.Id == "d2");
    }

    [Fact]
    public void WithoutServicesNothingHappens()
    {
        var controller = Make(new FakeServices());
        Assert.Empty(controller.RemoteAds);
        Assert.Empty(controller.RemotePopups);
        Assert.Null(controller.CurrentAnnouncement);
        Assert.True(controller.RefreshRemoteConfigAsync().IsCompletedSuccessfully);
        controller.DismissAnnouncement("x");
        controller.AnonymousCount = false;
        Assert.False(controller.AnonymousCount);
    }

    [Fact]
    public void AnonymousCountIsOnByDefaultAndSurvivesOldSettingsFiles()
    {
        Assert.True(new UserSettings().AnonymousCount);
        Assert.True(JsonSerializer.Deserialize<UserSettings>("{}")!.AnonymousCount);
        var off = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(new UserSettings { AnonymousCount = false }))!;
        Assert.False(off.AnonymousCount);
        Assert.Contains("Ayarlardan kapatabilirsin", AppController.AnonymousCountDisclosure);
        Assert.Contains("IP adresin saklanmaz", AppController.AnonymousCountDisclosure);
    }

    [Fact]
    public async Task CountPingsAtStartAndTurningItOffDeletesTheId()
    {
        var api = new FakeApi();
        var settings = new MemorySettings();
        var controller = Make(Services(api, remote: false, counter: true), settings);
        await Until(() => api.Pings.Count >= 1);
        var ping = api.Pings[0];
        var idFile = Path.Combine(folder, UsageCounter.IdFileName);
        var id = File.ReadAllText(idFile);
        Assert.Equal(id, ping.GetProperty("id").GetString());
        Assert.Equal(("1.1.0", "ps5", false), (ping.GetProperty("v").GetString(), ping.GetProperty("p").GetString(), ping.GetProperty("playing").GetBoolean()));

        var changed = new List<string?>();
        controller.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        controller.AnonymousCount = false;
        Assert.Contains(nameof(AppController.AnonymousCount), changed);
        Assert.False(settings.Load().AnonymousCount);
        Assert.False(File.Exists(idFile));
        var count = api.Pings.Count;
        await Task.Delay(200);
        Assert.Equal(count, api.Pings.Count);
        Assert.False(File.Exists(idFile));

        controller.AnonymousCount = true;
        await Until(() => api.Pings.Count > count);
        Assert.NotEqual(id, api.Pings[^1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task CountIsOffFromTheStartWhenTheSettingIsOff()
    {
        var api = new FakeApi();
        var settings = new MemorySettings();
        settings.Save(new UserSettings { AnonymousCount = false });
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(folder).FullName, UsageCounter.IdFileName), UsageCounter.NewId());
        var controller = Make(Services(api, remote: false, counter: true), settings);
        await Task.Delay(200);
        Assert.Empty(api.Pings);
        Assert.False(controller.AnonymousCount);
        Assert.False(File.Exists(Path.Combine(folder, UsageCounter.IdFileName)));
    }

    [Fact]
    public async Task TranslationStartAndStopPingImmediately()
    {
        var api = new FakeApi();
        var services = Services(api, remote: false, counter: true);
        var store = new MemoryStore();
        store.Save(StoreKeys.AccountId, Enumerable.Repeat((byte)1, 8).ToArray());
        store.Save(StoreKeys.Console, new RegisteredConsole("192.168.1.253", "PS5", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)2, 16).ToArray()));
        store.Save(StoreKeys.Tv, new LgTv("LG TV", "127.0.0.1"));
        store.Save(StoreKeys.TvKey, "tv-key");
        var controller = AppControllerTests.Make(services, store);
        owned.Add(controller);
        await Until(() => api.Pings.Count >= 1);

        await controller.StartAsync();
        Assert.True(controller.IsRunning);
        await Until(() => api.Pings.Any(p => p.GetProperty("playing").GetBoolean()));
        var count = api.Pings.Count;
        controller.Stop();
        await Until(() => api.Pings.Count > count && !api.Pings[^1].GetProperty("playing").GetBoolean());
    }

    [Theory]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.Ps5, null, "ps5")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.RemotePlayWindow, null, "pc")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, null, "pc")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "obs64", "capture")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "OBS64.exe", "capture")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "Streamlabs OBS", "capture")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "4KCaptureUtility", "capture")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "Obsidian", "pc")]
    [InlineData(GameConsoleKind.PlayStation, FrameSource.CaptureWindow, "GTA6", "pc")]
    [InlineData(GameConsoleKind.Xbox, FrameSource.Ps5, null, "xbox")]
    [InlineData(GameConsoleKind.Xbox, FrameSource.CaptureWindow, "obs64", "xbox")]
    public void PlatformFollowsTheConsoleAndPictureSource(GameConsoleKind kind, FrameSource source, string? process, string expected)
    {
        var settings = new UserSettings { FrameSource = source, CaptureWindow = process is null ? null : new WindowCaptureTarget(process, "Pencere") };
        Assert.Equal(expected, UsagePlatform.For(kind, settings));
    }
}
