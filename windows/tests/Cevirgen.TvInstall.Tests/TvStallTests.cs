// SPDX-License-Identifier: AGPL-3.0-only

using System.Net.Sockets;
using Cevirgen.TvInstall.Samsung;

namespace Cevirgen.TvInstall.Tests;

/// <summary>
/// TV uygulaması güncellemesi takılınca: adımın süresi dolar, bağlantı kapatılır, bir kez daha denenir; yine takılırsa
/// kullanıcıya "bağlantı koptu, tekrar dene" gösterilir. Önceden masaüstündeki Samsung ve Android TV kurulumunda hiçbir
/// adımın süre sınırı yoktu, kart "kuruluyor"da kalıyordu.
/// </summary>
public class TvStallTests
{
    private static Func<string, TimeSpan?> Short(int milliseconds) =>
        step => step is "signin" or "play" ? null : TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public async Task StalledStepClosesTheConnectionAndFails()
    {
        using var stall = new TvStall(CancellationToken.None, Short(100));
        var closed = false;
        stall.OnStall(() => closed = true);
        var error = await Assert.ThrowsAsync<TvStepTimeoutException>(() => stall.RunAsync<int>(async token =>
        {
            stall.Enter("installing");
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }));
        Assert.True(closed);
        Assert.Equal("installing", error.Step);
        Assert.Contains("paket gönderme ve kurulum", error.Message);
    }

    /// <summary>TizenSdb'nin kurulumu iptal belirteci almıyor: takılan iş bırakılır, bekleme yine biter.</summary>
    [Fact]
    public async Task WorkThatIgnoresCancellationIsLeftBehind()
    {
        var grace = TvStall.Grace;
        TvStall.Grace = TimeSpan.FromMilliseconds(50);
        try
        {
            using var stall = new TvStall(CancellationToken.None, Short(100));
            var never = new TaskCompletionSource<int>();
            var started = DateTime.UtcNow;
            await Assert.ThrowsAsync<TvStepTimeoutException>(() => stall.RunAsync(_ => never.Task));
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }
        finally
        {
            TvStall.Grace = grace;
        }
    }

    [Fact]
    public async Task EachStepGetsItsOwnTime()
    {
        using var stall = new TvStall(CancellationToken.None, Short(300));
        var result = await stall.RunAsync(async token =>
        {
            foreach (var step in new[] { "reading", "closing", "installing", "launching" })
            {
                stall.Enter(step);
                await Task.Delay(150, token);
            }
            return 42;
        });
        Assert.Equal(42, result);
        Assert.False(stall.Stalled);
    }

    /// <summary>Samsung girişi ve TV'deki Play sayfası kullanıcıyı bekler: süre işlemez.</summary>
    [Fact]
    public async Task StepsWaitingForTheUserHaveNoLimit()
    {
        using var stall = new TvStall(CancellationToken.None, Short(100));
        var result = await stall.RunAsync(async token =>
        {
            stall.Enter("signin");
            await Task.Delay(400, token);
            return "tamam";
        });
        Assert.Equal("tamam", result);
    }

    /// <summary>Takılan bağlantının hatasını yutan adım ("açılamadı, kurulum yine tamam") da başarı sayılmaz.</summary>
    [Fact]
    public async Task SwallowedErrorAfterStallStillCountsAsStall()
    {
        using var stall = new TvStall(CancellationToken.None, Short(100));
        await Assert.ThrowsAsync<TvStepTimeoutException>(() => stall.RunAsync(async token =>
        {
            stall.Enter("launching");
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { }
            return 1;
        }));
    }

    [Fact]
    public async Task UserCancellationIsNotAStall()
    {
        using var cancel = new CancellationTokenSource();
        using var stall = new TvStall(cancel.Token, Short(5_000));
        cancel.CancelAfter(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stall.RunAsync<int>(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }));
        Assert.False(stall.Stalled);
    }

    [Fact]
    public async Task StallIsRetriedOnceWithAFreshAttempt()
    {
        var lines = new List<string>();
        var attempts = new List<int>();
        var result = await TvStall.RetryOnceAsync(attempt =>
        {
            attempts.Add(attempt);
            return attempt == 1 ? throw new TvStepTimeoutException("installing", TimeSpan.FromSeconds(360)) : Task.FromResult("0.9.104");
        }, error => error is TvStepTimeoutException, lines.Add);
        Assert.Equal("0.9.104", result);
        Assert.Equal([1, 2], attempts);
        Assert.Contains(lines, line => line.Contains("bir kez daha deneniyor") && line.Contains("360 sn"));
    }

    [Fact]
    public async Task SecondStallIsReported()
    {
        var attempts = 0;
        var error = await Assert.ThrowsAsync<TvStepTimeoutException>(() => TvStall.RetryOnceAsync<string>(_ =>
        {
            attempts++;
            throw new TvStepTimeoutException("launching", TimeSpan.FromSeconds(60));
        }, e => e is TvStepTimeoutException, null));
        Assert.Equal(2, attempts);
        Assert.Contains("bir kez daha denendi", TvStall.GaveUp(error));
    }

    [Fact]
    public async Task OtherErrorsAreNotRetried()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<SamsungInstallException>(() => TvStall.RetryOnceAsync<string>(_ =>
        {
            attempts++;
            throw new SamsungInstallException(SamsungInstallError.NotEnoughSpace, "[116]");
        }, SamsungTvInstaller.IsRetryable, null));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void SamsungRetriesStallsAndDroppedConnectionsOnly()
    {
        Assert.True(SamsungTvInstaller.IsRetryable(new TvStepTimeoutException("installing", TimeSpan.FromSeconds(1))));
        Assert.True(SamsungTvInstaller.IsRetryable(new IOException("forcibly closed")));
        Assert.True(SamsungTvInstaller.IsRetryable(new SocketException()));
        Assert.True(SamsungTvInstaller.IsRetryable(new SamsungInstallException(SamsungInstallError.ConnectionLost)));
        // Geliştirici modu kapalı, Host PC IP yanlış, sertifika: yeniden denemek düzeltmez.
        Assert.False(SamsungTvInstaller.IsRetryable(new SamsungInstallException(SamsungInstallError.Unreachable)));
        Assert.False(SamsungTvInstaller.IsRetryable(new SamsungInstallException(SamsungInstallError.CertificateRejected)));
        Assert.False(SamsungTvInstaller.IsRetryable(new OperationCanceledException()));
    }

    /// <summary>Güncellemeden önce TV'de çalışan eski uygulama kapatılır; kapatılamasa da kurulum sürer.</summary>
    [Fact]
    public async Task SamsungClosesTheRunningAppBeforeUpdating()
    {
        var sent = new List<string>();
        var paused = false;
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "tvinstall.log");
        await SamsungTvInstaller.CloseWithAsync(command =>
        {
            sent.Add(command);
            return command.EndsWith("CouchSubsT.CouchSubs") ? Task.FromResult("app_id[CouchSubsT.CouchSubs] kill success")
                : throw new IOException("bilinmeyen komut");
        }, new InstallLog(path), () => { paused = true; return Task.CompletedTask; });
        Assert.Equal(["0 was_kill CouchSubsT.CouchSubs", "0 was_kill CouchSubsT"], sent);
        Assert.True(paused);
        Assert.Contains("Güncellemeden önce eski uygulama kapatılıyor", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void EveryStepHasALimitExceptTheOnesWaitingForTheUser()
    {
        foreach (var step in new[] { "connecting", "approve", "reading", "certificate", "signing", "closing", "installing", "uninstalling", "launching" })
            Assert.NotNull(TvStall.LimitFor(step));
        Assert.Null(TvStall.LimitFor("signin"));
        Assert.Null(TvStall.LimitFor("play"));
    }
}
