// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core.LgTv;
using Xunit;
using Tv = Cevirgen.Core.LgTv.LgTv;

namespace Cevirgen.Core.Tests;

/// <summary>
/// TV kimliği (USN/UDN, Samsung id/wifiMac) ve kimlikle yeniden bulma (Mac/iPhone <c>TVIdentityTests</c>, Android
/// <c>TvIdentityTest</c> ile aynı vakalar).
/// </summary>
public class TvIdentityTests
{
    [Fact]
    public void ParsesUsnFromSsdpResponse()
    {
        const string response = "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.5:1990/\r\nSERVER: WebOS/4.4.0 UPnP/1.0\r\n"
            + "ST: urn:lge-com:service:webos-second-screen:1\r\n"
            + "USN: uuid:7A2B9C4E-1111-2222-3333-ABCDEF012345::urn:lge-com:service:webos-second-screen:1\r\n\r\n";
        Assert.Equal("7a2b9c4e-1111-2222-3333-abcdef012345", TvIdentity.UuidFromSsdpResponse(response));
        Assert.Equal("7a2b9c4e-1111-2222-3333-abcdef012345", LgDiscovery.HitFromSsdpResponse(response)!.Uuid);
        Assert.Null(TvIdentity.UuidFromSsdpResponse("HTTP/1.1 200 OK\r\nLOCATION: http://x/\r\n\r\n"));
    }

    [Fact]
    public void ParsesUdnFromDescription()
    {
        const string xml = "<root><device><friendlyName>Salon</friendlyName><UDN>uuid:ABC-1</UDN>"
            + "<deviceList><device><UDN>uuid:child</UDN></device></deviceList></device></root>";
        Assert.Equal("abc-1", TvIdentity.UuidFromDescription(xml));
        Assert.Equal(new Tv("Salon", "10.0.0.4", TvBrand.AndroidTv, "abc-1"), AndroidTvDiscovery.FromDescription("10.0.0.4", xml));
        Assert.Null(AndroidTvDiscovery.FromDescription("10.0.0.4", "<root><device></device></root>"));
    }

    [Fact]
    public void NormalizesMac()
    {
        Assert.Equal("a4:30:7a:12:34:56", TvIdentity.NormalizedMac("A4-30-7A-12-34-56"));
        Assert.Equal("a4:30:7a:12:34:56", TvIdentity.NormalizedMac("a4307a123456"));
        Assert.Null(TvIdentity.NormalizedMac("00:00:00:00:00:00"));
        Assert.Null(TvIdentity.NormalizedMac("a4:30:7a"));
        Assert.Null(TvIdentity.NormalizedMac(null));
    }

    [Fact]
    public void ReadsSamsungIdentity()
    {
        const string json = """{"id":"uuid:top","device":{"name":"[TV] Samsung","id":"uuid:4C1E-99","wifiMac":"F4:7B:09:AA:BB:CC"}}""";
        Assert.Equal(new Tv("[TV] Samsung", "192.168.1.9", TvBrand.Samsung, "4c1e-99", "f4:7b:09:aa:bb:cc"), SamsungMessages.Tv("192.168.1.9", json));
    }

    [Fact]
    public void OldRecordWithoutIdentityStillDeserializes()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<Tv>("""{"Name":"Salon","Host":"192.168.1.5"}""")!;
        Assert.Equal(new Tv("Salon", "192.168.1.5"), old);
        Assert.Null(old.Uuid);
    }

    [Fact]
    public void FindsTvByUuidAfterRenameAndMove()
    {
        var saved = new Tv("Salon", "192.168.1.5", Uuid: "aaa");
        var renamed = new Tv("[LG] webOS TV OLED55", "192.168.1.77", Uuid: "aaa");
        var other = new Tv("Yatak odası", "192.168.1.8", Uuid: "bbb");
        var hit = TvLocator.Locate(saved, [other, renamed]);
        Assert.Equal(renamed, hit!.Value.Tv);
        Assert.Equal(TvMatch.Uuid, hit.Value.How);
    }

    [Fact]
    public void PicksTheRightOneOfTwoTvsWithTheSameName()
    {
        var saved = new Tv("LG TV", "192.168.1.5", Uuid: "aaa");
        var mine = new Tv("LG TV", "192.168.1.40", Uuid: "aaa");
        var neighbour = new Tv("LG TV", "192.168.1.41", Uuid: "bbb");
        Assert.Equal(mine, TvLocator.Moved(saved, [neighbour, mine]));
        // Kimliği farklı tek TV, adı aynı ya da ağdaki tek TV olsa da seçilmez.
        Assert.Null(TvLocator.Moved(saved, [neighbour]));
    }

    [Fact]
    public void AnotherTvAtTheSavedAddressDoesNotHideTheMove()
    {
        var saved = new Tv("Salon", "192.168.1.5", Uuid: "aaa");
        var moved = new Tv("Salon", "192.168.1.6", Uuid: "aaa");
        Assert.Equal(moved, TvLocator.Moved(saved, [new Tv("Salon", "192.168.1.5", Uuid: "bbb"), moved]));
        Assert.Null(TvLocator.Moved(saved, [new Tv("Salon", "192.168.1.5", Uuid: "aaa")]));
    }

    [Fact]
    public void MatchesByMacWhenUuidIsMissing()
    {
        var saved = new Tv("Samsung", "192.168.1.5", TvBrand.Samsung, Mac: "a4:30:7a:12:34:56");
        var found = new Tv("Renamed", "192.168.1.9", TvBrand.Samsung, "zzz", "A4-30-7A-12-34-56");
        Assert.Equal(TvMatch.Mac, TvLocator.Locate(saved, [found])!.Value.How);
    }

    [Fact]
    public void OldRecordFallsBackToNameAndLearnsIdentity()
    {
        var saved = new Tv("Salon", "192.168.1.5");
        var moved = new Tv("Salon", "192.168.1.6", Uuid: "aaa");
        var other = new Tv("Mutfak", "192.168.1.7", Uuid: "bbb");
        Assert.Equal("aaa", TvLocator.Moved(saved, [other, moved])!.Uuid);
        Assert.Equal(TvMatch.Name, TvLocator.Locate(saved, [other, moved])!.Value.How);
        var here = new Tv("[LG] webOS TV", "192.168.1.5", Uuid: "aaa");
        Assert.Null(TvLocator.Moved(saved, [here]));
        Assert.Equal(new Tv("Salon", "192.168.1.5", Uuid: "aaa"), TvLocator.Learned(saved, [here]));
        Assert.Null(TvLocator.Learned(new Tv("Salon", "192.168.1.5", Uuid: "aaa"), [here]));
    }

    private sealed class Control : ITvControl
    {
        public Task LaunchAsync(string appId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ForegroundAppIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public void Dispose() { }
    }

    [Fact]
    public async Task LocatorLogsAndReportsTheMove()
    {
        var saved = new Tv("Salon", "192.168.1.5", Uuid: "aaa");
        var lines = new List<string>();
        var reported = new List<Tv>();
        var locator = new TvLocator(saved, tv => tv.Host == "192.168.1.5" ? throw new IOException() : Task.FromResult<ITvControl>(new Control()),
            () => Task.FromResult<IReadOnlyList<Tv>>([new Tv("Yeni ad", "192.168.1.30", Uuid: "aaa")]), reported.Add, log: lines.Add);
        await locator.ConnectAsync();
        Assert.Equal(["192.168.1.30"], reported.Select(tv => tv.Host));
        Assert.Equal(["TV yeni adreste bulundu: 192.168.1.5 → 192.168.1.30 (UUID ile)"], lines);
    }

    [Fact]
    public async Task AndroidTvKeeperFollowsTheTvToItsNewAddress()
    {
        var host = "192.168.1.5";
        var moved = new List<Tv>();
        var lines = new List<string>();
        var locator = new AndroidTvLocator(new Tv("Android TV", host, TvBrand.AndroidTv), moved.Add,
            search: () => Task.FromResult<IReadOnlyList<Tv>>([new Tv("Salon", "192.168.1.9", TvBrand.AndroidTv, "cast-1")]),
            identify: h => Task.FromResult<Tv?>(new Tv("Salon", h, TvBrand.AndroidTv, "cast-1")),
            responds: _ => Task.FromResult(false), log: lines.Add);
        var tried = new List<string>();
        var keeper = new AndroidTvKeeper(() =>
        {
            tried.Add(locator.Host);
            return locator.Host == "192.168.1.5" ? throw new IOException("ADB kapalı") : Task.FromResult("Starting: Intent");
        }, () => false, _ => TvOpenProblem.DebugOff, locator: locator);
        await keeper.TickAsync();
        Assert.Equal(["192.168.1.5", "192.168.1.9"], tried);
        Assert.Equal("cast-1", moved.Single().Uuid);
        Assert.Equal(["TV yeni adreste bulundu: 192.168.1.5 → 192.168.1.9 (ağdaki tek TV)"], lines);
    }

    [Fact]
    public async Task AndroidTvLocatorLearnsIdentityOnceAndDoesNotSearchWhileHostAnswers()
    {
        var searches = 0;
        var identifies = 0;
        var moved = new List<Tv>();
        var locator = new AndroidTvLocator(new Tv("Android TV", "192.168.1.5", TvBrand.AndroidTv), moved.Add,
            search: () => { searches++; return Task.FromResult<IReadOnlyList<Tv>>([]); },
            identify: h => { identifies++; return Task.FromResult<Tv?>(new Tv("Salon", h, TvBrand.AndroidTv, "cast-1")); },
            responds: _ => Task.FromResult(true));
        Assert.Null(await locator.RelocateAsync());
        Assert.Equal(0, searches);
        await locator.LearnIdentityAsync();
        await locator.LearnIdentityAsync();
        Assert.Equal(1, identifies);
        Assert.Equal([new Tv("Android TV", "192.168.1.5", TvBrand.AndroidTv, "cast-1")], moved);
    }

    [Fact]
    public void SenderAddressChangeIsReportedOnlyWhenTheHomeAddressChanges()
    {
        Assert.True(SenderAddressWatcher.Changed(["192.168.1.20"], ["192.168.1.34"]));
        Assert.False(SenderAddressWatcher.Changed(["192.168.1.20", "10.0.0.2"], ["10.0.0.2", "192.168.1.20"]));
        Assert.False(SenderAddressWatcher.Changed(["192.168.1.20"], []));
        Assert.True(SenderAddressWatcher.Changed([], ["192.168.1.20"]));
        var addresses = new List<string> { "192.168.1.20" };
        var reports = new List<string>();
        var watcher = new SenderAddressWatcher((old, now) => reports.Add(SenderAddressWatcher.Line(old, now)), () => addresses.ToList());
        watcher.Check();
        Assert.Empty(reports);
        addresses[0] = "192.168.1.34";
        watcher.Check();
        Assert.Equal(["Bu cihazın adresi değişti: 192.168.1.20 → 192.168.1.34; TV'ye yeni adres bildiriliyor."], reports);
    }
}
