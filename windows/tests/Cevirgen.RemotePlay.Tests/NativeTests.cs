// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;

namespace Cevirgen.RemotePlay.Tests;

/// <summary>Yerel kütüphaneyle koşan testler (Windows CI'da; DLL yoksa atlanır).</summary>
public class NativeTests
{
    [NativeFact]
    public async Task DiscoveryOfALoopbackAddressFindsNothingAndReturns()
    {
        var watch = Stopwatch.StartNew();
        var found = await ConsoleDiscovery.DiscoverAsync("127.0.0.1", TimeSpan.FromMilliseconds(600));
        Assert.Empty(found);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.4, 5);
    }

    /// <summary>
    /// Çevirgen: ağdaki gerçek PS5'e sorulur (COUCHSUBS_TEST_PS5 adresi verilirse). Paketlenen eski couchsubs_native.dll'de
    /// cs_discover_id yokken arama çökmeden kimliksiz aramayla PS5'i bulmalı.
    /// </summary>
    [NativeFact]
    public async Task ARealConsoleIsFoundWhenAnAddressIsGiven()
    {
        if (Environment.GetEnvironmentVariable("COUCHSUBS_TEST_PS5") is not { Length: > 0 } host) return;
        var found = await ConsoleDiscovery.DiscoverAsync(host, TimeSpan.FromSeconds(3));
        var console = Assert.Single(found);
        Assert.Equal(host, console.Host);
    }

    [NativeFact]
    public async Task RegistrationWithUnreachableConsoleFails()
    {
        var error = await Assert.ThrowsAsync<RegistrationException>(() => Registration.RegisterAsync("127.0.0.1", new byte[8], 12345678));
        // Kimsenin dinlemediği adres aramayı ICMP "port kapalı" ile reddeder; eşleştirme ekranı kapalı PS5 de böyle
        // davranır, bu yüzden #604'ten beri PairingClosed sayılır. Yanıt hiç gelmezse NoAnswer olur.
        Assert.Contains(error.Failure, new[] { RegistrationFailure.PairingClosed, RegistrationFailure.NoAnswer });
    }

    /// <summary>Bağlanamayan bir adreste yeniden deneme beklerken durdurup bırakmak çökmemeli ve bloklamamalı.</summary>
    [NativeFact]
    public async Task StopWhileReconnectingDoesNotCrashOrBlock()
    {
        var console = new RegisteredConsole("127.0.0.1", "test", Enumerable.Repeat((byte)0x31, 16).ToArray(), Enumerable.Repeat((byte)1, 16).ToArray());
        var session = new StreamSession(console, Enumerable.Repeat((byte)1, 8).ToArray());
        var disconnected = new TaskCompletionSource<SessionState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state =>
        {
            if (state.Phase == SessionPhase.Disconnected) disconnected.TrySetResult(state);
        };
        session.Start();
        var state = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotEqual(DisconnectKind.Stopped, state.Reason!.Kind);

        var watch = Stopwatch.StartNew();
        session.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(200));
        session.Dispose();
        // Bekleyen yeniden deneme bloğunun çalışacağı zamanı geçir; süreç hâlâ ayaktaysa çökme yok.
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    [NativeFact]
    public void WakeWithoutCredentialIsRejected()
    {
        var console = new RegisteredConsole("127.0.0.1", "test", new byte[16], new byte[16]);
        Assert.Throws<InvalidOperationException>(() => ConsoleDiscovery.Wake(console));
    }
}
