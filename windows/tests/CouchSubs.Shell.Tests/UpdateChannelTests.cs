// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Text;

namespace CouchSubs.Shell.Tests;

/// <summary>Çevirgen güncelleme kanalı: kendi depomuz; 404 (özel depo), ağ hatası ve bozuk yanıt "güncelleme yok".</summary>
public sealed class UpdateChannelTests
{
    private sealed class GitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<string?> Check(Func<HttpRequestMessage, HttpResponseMessage> respond, string current = "1.1.0")
    {
        using var http = new HttpClient(new GitHub(respond));
        return await UpdateChannel.NewerTagAsync(http, current);
    }

    [Fact]
    public void PointsToTheCevirgenRepository()
    {
        Assert.Equal("https://github.com/ozguradmin/cevirgen", UpdateChannel.Repository);
        Assert.Equal("https://api.github.com/repos/ozguradmin/cevirgen/releases/latest", UpdateChannel.LatestReleaseApi);
        Assert.Equal("https://github.com/ozguradmin/cevirgen/releases/latest", UpdateChannel.DownloadPage.AbsoluteUri);
        Assert.False(UpdateChannel.Prerelease);
    }

    [Fact]
    public async Task NewerReleaseIsOfferedOlderIsNot()
    {
        var handler = new GitHub(_ => Json("""{"tag_name":"v1.2.0"}"""));
        using var http = new HttpClient(handler);
        Assert.Equal("1.2.0", await UpdateChannel.NewerTagAsync(http, "1.1.0"));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(UpdateChannel.LatestReleaseApi, request.RequestUri!.AbsoluteUri);
        Assert.Contains("Cevirgen", request.Headers.UserAgent.ToString());
        Assert.Null(await Check(_ => Json("""{"tag_name":"v1.1.0"}""")));
        Assert.Null(await Check(_ => Json("""{"tag_name":"v1.0.9"}""")));
    }

    [Fact]
    public async Task PrivateRepositoryNetworkErrorsAndBadAnswersAreQuiet()
    {
        Assert.Null(await Check(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"Not Found"}""") }));
        Assert.Null(await Check(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.Null(await Check(_ => throw new HttpRequestException("çevrimdışı")));
        Assert.Null(await Check(_ => throw new TaskCanceledException("zaman aşımı")));
        Assert.Null(await Check(_ => Json("not json")));
        Assert.Null(await Check(_ => Json("""{"name":"etiketsiz"}""")));
    }

    [Fact]
    public async Task ControllerShowsNoBannerWhenTheCheckFindsNothing()
    {
        var updater = new QuietUpdater();
        var controller = AppControllerTests.Make(new FakeServices { Updater = updater });
        await controller.CheckForUpdateAsync();
        Assert.Null(controller.UpdateVersion);
        Assert.Null(controller.UpdateError);
        Assert.Equal(UpdateChannel.DownloadPage, updater.DownloadPage);
    }

    private sealed class QuietUpdater : IUpdater
    {
        public bool CanUpdateItself => false;
        public Uri DownloadPage => UpdateChannel.DownloadPage;
        public Task<string?> CheckAsync() => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<ReleaseNoteRun>> ReleaseNotesAsync(string version) => Task.FromResult<IReadOnlyList<ReleaseNoteRun>>([]);
        public Task InstallAsync(IProgress<double> progress, Action? beforeRestart = null) => Task.CompletedTask;
    }
}
