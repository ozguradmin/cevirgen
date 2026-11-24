// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text;
using System.Text.Json;

namespace CouchSubs.Core.Tests;

/// <summary>Çevirgen sunucusunun sahtesi: adrese göre yanıt verir, gelen istekleri (yöntem, adres, gövde, başlıklar) kaydeder.</summary>
internal sealed class FakeCevirgenServer : HttpMessageHandler
{
    public sealed record Request(HttpMethod Method, Uri Url, string Body, string Headers);

    public List<Request> Requests { get; } = [];
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
    /// <summary>true iken her istek ağ hatasıyla düşer (çevrimdışı).</summary>
    public bool Offline { get; set; }

    public int CountFor(string url)
    {
        lock (Requests) return Requests.Count(r => r.Url.AbsoluteUri == url);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add(new Request(request.Method, request.RequestUri!, body, request.Headers.ToString()));
        if (Offline) throw new HttpRequestException("çevrimdışı");
        var response = Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes, bool withLength = true)
    {
        HttpContent content = withLength ? new ByteArrayContent(bytes) : new StreamContent(new UnknownLengthStream(bytes));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Uzunluğu bilinmeyen akış (Content-Length yok; sınır okurken denetlenmeli).</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    /// <summary>1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
}

/// <summary>Çevirgen API adresi, /v1/config biçimi, önbellek ve görsel indirme kuralları.</summary>
public sealed class CevirgenApiTests : IDisposable
{
    private const string Base = "https://api.test/";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-api-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private const string Sample = """
        {"version":1,
         "ads":[{"id":"a1","title":"Başlık","text":"Kısa metin","icon":"https://cdn.test/simge.png","link":"https://example.org/a"},
                {"id":"a2","title":"Düz http","text":"x","icon":"http://cdn.test/kotu.png","link":"https://example.org/b"},
                {"id":"a3","title":"","text":"başlıksız","icon":"https://cdn.test/c.png","link":"https://example.org/c"},
                {"id":"a4","title":{"tr":"Türkçe","en":"English"},"text":"çok dilli","icon":"https://cdn.test/d.png","link":"https://example.org/d"}],
         "popups":[{"id":"p1","image":"https://cdn.test/popup.png","link":"https://example.org/p"},
                   {"id":"p2","image":"https://cdn.test/popup2.png","link":"javascript:alert(1)"}],
         "announcement":{"id":"duyuru-1","text":"Yeni sürüm çıktı","link":"https://example.org/surum"}}
        """;

    [Fact]
    public void BaseUrlComesFromEnvironmentThenBuildThenTheConstant()
    {
        Assert.Equal(CevirgenApi.DefaultUrl + "/", CevirgenApi.BaseUrl(_ => null, typeof(CevirgenApiTests).Assembly).AbsoluteUri);
        Assert.Equal("https://staging.test/api/", CevirgenApi.BaseUrl(_ => "https://staging.test/api", typeof(CevirgenApiTests).Assembly).AbsoluteUri);
        // Yerel deneme için yalnız localhost'ta http; başka yerde http ve bozuk değer yok sayılır.
        Assert.Equal("http://127.0.0.1:8787/", CevirgenApi.BaseUrl(_ => "http://127.0.0.1:8787", typeof(CevirgenApiTests).Assembly).AbsoluteUri);
        Assert.Equal(CevirgenApi.DefaultUrl + "/", CevirgenApi.BaseUrl(_ => "http://evil.test", typeof(CevirgenApiTests).Assembly).AbsoluteUri);
        Assert.Null(CevirgenApi.Parse("$(CevirgenApiUrl)"));
        Assert.Null(CevirgenApi.Parse("https://a.test/?id=1"));
        Assert.Equal("https://a.test/v1/config", CevirgenApi.ConfigUrl(CevirgenApi.Parse("https://a.test")!).AbsoluteUri);
        Assert.Equal("https://a.test/x/v1/ping", CevirgenApi.PingUrl(CevirgenApi.Parse("https://a.test/x/")!).AbsoluteUri);
    }

    [Fact]
    public void ParseKeepsOnlyHttpsEntriesAndReadsTheAnnouncement()
    {
        var config = RemoteConfig.Parse(Sample)!;
        Assert.Equal(["a1", "a4"], config.Ads.Select(ad => ad.Id));
        Assert.Equal("Türkçe", config.Ads[1].Title);
        Assert.Equal(("Başlık", "Kısa metin", "https://cdn.test/simge.png", "https://example.org/a"),
            (config.Ads[0].Title, config.Ads[0].Text, config.Ads[0].Icon.AbsoluteUri, config.Ads[0].Link.AbsoluteUri));
        Assert.Equal(["p1"], config.Popups.Select(popup => popup.Id));
        Assert.Equal(new Announcement("duyuru-1", "Yeni sürüm çıktı", new Uri("https://example.org/surum")), config.Announcement);
        Assert.Equal(config, RemoteConfig.Parse(Sample));

        Assert.Null(RemoteConfig.Parse("not json"));
        Assert.Null(RemoteConfig.Parse("[1,2]"));
        Assert.Equal(RemoteConfig.Empty, RemoteConfig.Parse("""{"version":1,"ads":[],"popups":[],"announcement":null}"""));
        // Bağlantısı https olmayan duyuru bağlantısız gösterilir; metni boşsa duyuru yok.
        Assert.Null(RemoteConfig.Parse("""{"announcement":{"id":"x","text":"Merhaba","link":"http://a.test"}}""")!.Announcement!.Link);
        Assert.Null(RemoteConfig.Parse("""{"announcement":{"id":"x","text":" "}}""")!.Announcement);
    }

    [Fact]
    public async Task FetchCachesTheLastGoodConfigAndOfflineKeepsIt()
    {
        var server = new FakeCevirgenServer();
        server.Routes[Base + "v1/config"] = () => FakeCevirgenServer.Json(Sample);
        using var client = new RemoteConfigClient(new Uri(Base), folder, server);
        Assert.Null(client.LoadCached());

        var fetched = await client.FetchAsync();
        Assert.NotNull(fetched);
        Assert.Equal(fetched, client.LoadCached());
        // Düz GET: gövde, sorgu, çerez ya da kimlik yok.
        var request = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Get, Base + "v1/config", ""), (request.Method, request.Url.AbsoluteUri, request.Body));
        Assert.DoesNotContain("Cookie", request.Headers);
        Assert.DoesNotContain("Authorization", request.Headers);

        server.Offline = true;
        Assert.Null(await client.FetchAsync());
        server.Offline = false;
        server.Routes[Base + "v1/config"] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.Null(await client.FetchAsync());
        server.Routes[Base + "v1/config"] = () => FakeCevirgenServer.Json("{bozuk");
        Assert.Null(await client.FetchAsync());
        Assert.Equal(fetched, client.LoadCached());
    }

    [Fact]
    public async Task ImagesAreCachedByUrlHashAndAdsWithoutImagesAreSkipped()
    {
        var server = new FakeCevirgenServer();
        server.Routes["https://cdn.test/simge.png"] = () => FakeCevirgenServer.Bytes(FakeCevirgenServer.Png);
        server.Routes["https://cdn.test/popup.png"] = () => FakeCevirgenServer.Bytes(FakeCevirgenServer.Png, withLength: false);
        // a4'ün simgesi yok (404): reklam atlanır.
        using var client = new RemoteConfigClient(new Uri(Base), folder, server);
        var config = RemoteConfig.Parse(Sample)!;

        var offline = await client.ResolveAsync(config, download: false);
        Assert.Empty(offline.Ads);
        Assert.Empty(server.Requests);

        var content = await client.ResolveAsync(config, download: true);
        var ad = Assert.Single(content.Ads);
        Assert.Equal("a1", ad.Ad.Id);
        Assert.Equal(Path.Combine(client.AssetFolder, RemoteConfigClient.ImageFileName(new Uri("https://cdn.test/simge.png"))), ad.IconFile);
        Assert.Equal(FakeCevirgenServer.Png, File.ReadAllBytes(ad.IconFile));
        Assert.Equal("p1", Assert.Single(content.Popups).Popup.Id);
        Assert.Equal(config.Announcement, content.Announcement);
        // http simgesi hiç istenmez.
        Assert.Equal(0, server.CountFor("http://cdn.test/kotu.png"));

        // İkinci kez diskten: yeni istek yok; çevrimdışıyken de aynı içerik.
        var before = server.Requests.Count;
        Assert.Equal(content, await client.ResolveAsync(config, download: true));
        Assert.Equal(1, server.CountFor("https://cdn.test/simge.png"));
        Assert.Equal(content.Ads, (await client.ResolveAsync(config, download: false)).Ads);
        Assert.True(server.Requests.Count > before); // yalnız eksik a4 simgesi yeniden denendi
        Assert.Equal(2, server.CountFor("https://cdn.test/d.png"));

        // Reklam sunucudan kalkınca görseli silinir.
        await client.ResolveAsync(RemoteConfig.Empty, download: true);
        Assert.False(File.Exists(ad.IconFile));
    }

    [Fact]
    public async Task ImagesOverTwoMegabytesOrNotImagesAreRejected()
    {
        var big = new byte[RemoteConfigClient.MaxImageBytes + 1];
        FakeCevirgenServer.Png.CopyTo(big, 0);
        var server = new FakeCevirgenServer();
        server.Routes["https://cdn.test/big.png"] = () => FakeCevirgenServer.Bytes(big);
        server.Routes["https://cdn.test/big-stream.png"] = () => FakeCevirgenServer.Bytes(big, withLength: false);
        server.Routes["https://cdn.test/page.png"] = () => FakeCevirgenServer.Bytes("<html>not an image</html>"u8.ToArray());
        var exact = new byte[RemoteConfigClient.MaxImageBytes];
        FakeCevirgenServer.Png.CopyTo(exact, 0);
        server.Routes["https://cdn.test/exact.png"] = () => FakeCevirgenServer.Bytes(exact, withLength: false);
        using var client = new RemoteConfigClient(new Uri(Base), folder, server);

        Assert.Null(await client.ImageAsync(new Uri("https://cdn.test/big.png")));
        Assert.Null(await client.ImageAsync(new Uri("https://cdn.test/big-stream.png")));
        Assert.Null(await client.ImageAsync(new Uri("https://cdn.test/page.png")));
        Assert.Null(await client.ImageAsync(new Uri("http://cdn.test/exact.png")));
        Assert.NotNull(await client.ImageAsync(new Uri("https://cdn.test/exact.png")));
        Assert.Single(Directory.GetFiles(client.AssetFolder));
        server.Offline = true;
        Assert.Null(await client.ImageAsync(new Uri("https://cdn.test/other.png")));
    }

    [Fact]
    public void ImageSignaturesAndFileNames()
    {
        Assert.True(RemoteConfigClient.IsImage(FakeCevirgenServer.Png));
        Assert.True(RemoteConfigClient.IsImage([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.True(RemoteConfigClient.IsImage("GIF89a...."u8));
        Assert.True(RemoteConfigClient.IsImage("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.False(RemoteConfigClient.IsImage("<svg></svg>"u8));
        var name = RemoteConfigClient.ImageFileName(new Uri("https://cdn.test/a.PNG?x=1"));
        Assert.Matches("^[0-9a-f]{32}\\.png$", name);
        Assert.NotEqual(name, RemoteConfigClient.ImageFileName(new Uri("https://cdn.test/a.PNG?x=2")));
        Assert.EndsWith(".img", RemoteConfigClient.ImageFileName(new Uri("https://cdn.test/v1/assets/simge")));
    }
}

/// <summary>Anonim kullanım sayımı: gövde, kurulum kimliği, kapatınca silme, sessiz hata.</summary>
public sealed class UsageCounterTests : IDisposable
{
    private const string Base = "https://api.test/";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-count-" + Guid.NewGuid().ToString("N"));
    private string IdFile => Path.Combine(folder, UsageCounter.IdFileName);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static FakeCevirgenServer Server()
    {
        var server = new FakeCevirgenServer();
        server.Routes[Base + "v1/ping"] = () => new HttpResponseMessage(HttpStatusCode.NoContent);
        return server;
    }

    [Fact]
    public async Task PingSendsTheServerFormatWithAPersistentRandomId()
    {
        var server = Server();
        using var counter = new UsageCounter(new Uri(Base), IdFile, "1.1.0+abc123", handler: server);
        Assert.True(await counter.PingAsync("ps5", playing: true));
        Assert.True(await counter.PingAsync("pc", playing: false));

        var id = File.ReadAllText(IdFile);
        Assert.True(UsageCounter.IsValidId(id));
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.Equal((HttpMethod.Post, Base + "v1/ping"), (r.Method, r.Url.AbsoluteUri)));
        using var first = JsonDocument.Parse(server.Requests[0].Body);
        Assert.Equal(["id", "v", "p", "playing"], first.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(id, first.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.1.0+abc123", first.RootElement.GetProperty("v").GetString());
        Assert.Equal("ps5", first.RootElement.GetProperty("p").GetString());
        Assert.True(first.RootElement.GetProperty("playing").GetBoolean());
        using var second = JsonDocument.Parse(server.Requests[1].Body);
        Assert.Equal(id, second.RootElement.GetProperty("id").GetString());
        Assert.False(second.RootElement.GetProperty("playing").GetBoolean());
        Assert.DoesNotContain("Cookie", server.Requests[0].Headers);

        // Yeniden açılışta aynı kimlik.
        using var again = new UsageCounter(new Uri(Base), IdFile, "1.1.0", handler: server);
        Assert.Equal(id, again.InstallId);
    }

    [Fact]
    public async Task TurningOffDeletesTheIdAndStopsPings()
    {
        var server = Server();
        using var counter = new UsageCounter(new Uri(Base), IdFile, "1.1.0", handler: server);
        await counter.PingAsync("xbox", false);
        var first = File.ReadAllText(IdFile);

        counter.SetEnabled(false);
        Assert.False(File.Exists(IdFile));
        Assert.Null(counter.InstallId);
        Assert.False(await counter.PingAsync("xbox", false));
        Assert.Single(server.Requests);
        Assert.False(File.Exists(IdFile));

        counter.SetEnabled(true);
        Assert.True(await counter.PingAsync("xbox", false));
        var second = File.ReadAllText(IdFile);
        Assert.NotEqual(first, second);

        // Kapalı başlatılan sayaç eski kimliği de siler.
        using var off = new UsageCounter(new Uri(Base), IdFile, "1.1.0", enabled: false, handler: server);
        Assert.False(File.Exists(IdFile));
    }

    [Fact]
    public async Task FailuresAreSilent()
    {
        var server = Server();
        server.Offline = true;
        using var counter = new UsageCounter(new Uri(Base), IdFile, "1.1.0", handler: server);
        Assert.False(await counter.PingAsync("ps5", false));
        server.Offline = false;
        server.Routes[Base + "v1/ping"] = () => new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.False(await counter.PingAsync("ps5", false));
    }

    [Fact]
    public void BodyAndVersionMatchTheServerRules()
    {
        using var body = JsonDocument.Parse(UsageCounter.Body("x", "1.0", "playstation", false));
        Assert.Equal("unknown", body.RootElement.GetProperty("p").GetString());
        Assert.Equal("1.1.0-beta.2", UsageCounter.CleanVersion(" 1.1.0-beta.2 "));
        Assert.Equal("0.0.0", UsageCounter.CleanVersion(""));
        Assert.Equal(24, UsageCounter.CleanVersion(new string('9', 40)).Length);
        Assert.Equal("100", UsageCounter.CleanVersion("1 0/0"));
        Assert.True(UsageCounter.IsValidId(UsageCounter.NewId()));
        Assert.False(UsageCounter.IsValidId("00000000-0000-1000-8000-000000000000"));
        Assert.False(UsageCounter.IsValidId(UsageCounter.NewId().ToUpperInvariant()));
        Assert.Contains("5 dakikada bir", UsageCounter.Disclosure);
    }
}

/// <summary>
/// Çevirgen sunucusunun asıl adresi (kendi alan adı) ve yedeği (workers.dev): asıl adrese ağ/TLS hatasıyla ulaşılamazsa
/// yapılandırma ve anonim sayım yedeğe gider; çalışan adres hatırlanır.
/// </summary>
public sealed class CevirgenEndpointTests : IDisposable
{
    private const string Primary = "https://primary.test/";
    private const string Fallback = "https://fallback.test/";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cevirgen-endpoint-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>Asıl adres TLS/ağ hatası verir (sertifika henüz hazır değil); yedek çalışır.</summary>
    private sealed class BrokenPrimary : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool PrimaryBroken { get; set; } = true;
        public HttpStatusCode? PrimaryStatus { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            lock (Requests) Requests.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "primary.test")
            {
                if (PrimaryStatus is { } status) return new HttpResponseMessage(status);
                if (PrimaryBroken) throw new HttpRequestException("The SSL connection could not be established");
            }
            var body = request.RequestUri.AbsolutePath.EndsWith("config") ? """{"version":1,"ads":[],"popups":[]}""" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void PrimaryIsTheCustomDomainAndWorkersDevIsTheFallback()
    {
        Assert.Equal("https://cevirgen.ozgurguler.tech", CevirgenApi.DefaultUrl);
        Assert.Equal("https://cevirgen-api.ozgurglr256.workers.dev", CevirgenApi.FallbackUrl);
        var urls = CevirgenApi.BaseUrls(_ => null, typeof(CevirgenEndpointTests).Assembly);
        Assert.Equal(["https://cevirgen.ozgurguler.tech/", "https://cevirgen-api.ozgurglr256.workers.dev/"], urls.Select(u => u.AbsoluteUri));
        // Yerel deneme adresi verilince yedeğe kaçılmaz.
        Assert.Equal(["http://127.0.0.1:8787/"], CevirgenApi.BaseUrls(_ => "http://127.0.0.1:8787", typeof(CevirgenEndpointTests).Assembly).Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task ConfigFallsBackOnTlsFailureAndRemembersTheWorkingAddress()
    {
        var server = new BrokenPrimary();
        using var client = new RemoteConfigClient([new Uri(Primary), new Uri(Fallback)], folder, server);
        Assert.NotNull(await client.FetchAsync());
        Assert.Equal(["https://primary.test/v1/config", "https://fallback.test/v1/config"], server.Requests.Select(u => u.AbsoluteUri));
        // İkinci istek doğrudan çalışan adrese gider.
        server.Requests.Clear();
        Assert.NotNull(await client.FetchAsync());
        Assert.Equal(["https://fallback.test/v1/config"], server.Requests.Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task EdgeErrorsAlsoFallBackButTheLastAnswerIsReturned()
    {
        var server = new BrokenPrimary { PrimaryStatus = HttpStatusCode.BadGateway };
        using var client = new RemoteConfigClient([new Uri(Primary), new Uri(Fallback)], folder, server);
        Assert.NotNull(await client.FetchAsync());
        Assert.Equal(2, server.Requests.Count);

        // Tek adres varsa 5xx olduğu gibi döner (null; hata fırlatmaz).
        var only = new BrokenPrimary { PrimaryStatus = HttpStatusCode.ServiceUnavailable };
        using var single = new RemoteConfigClient(new Uri(Primary), folder, only);
        Assert.Null(await single.FetchAsync());
        Assert.Single(only.Requests);
    }

    [Fact]
    public async Task PingFallsBackToo()
    {
        var server = new BrokenPrimary();
        using var counter = new UsageCounter([new Uri(Primary), new Uri(Fallback)], Path.Combine(folder, "install-id"), "1.2.0", handler: server);
        Assert.True(await counter.PingAsync("windows", playing: false));
        Assert.Equal(["https://primary.test/v1/ping", "https://fallback.test/v1/ping"], server.Requests.Select(u => u.AbsoluteUri));

        // Asıl adres düzelince yine de çalışan (yedek) adreste kalınır; o da bozulursa asıla dönülür.
        server.PrimaryBroken = false;
        server.Requests.Clear();
        Assert.True(await counter.PingAsync("windows", playing: false));
        Assert.Equal(["https://fallback.test/v1/ping"], server.Requests.Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task BothDownMeansNoCrash()
    {
        var server = new FakeCevirgenServer { Offline = true };
        using var client = new RemoteConfigClient([new Uri(Primary), new Uri(Fallback)], folder, server);
        Assert.Null(await client.FetchAsync());
        Assert.Equal(2, server.Requests.Count);
    }
}
