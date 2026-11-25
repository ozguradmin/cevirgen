// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;
using Cevirgen.TvInstall.Android;

namespace Cevirgen.TvInstall;

/// <summary>
/// Android TV kurulumunun önü ve arkası (Windows uygulaması ve Mac'in yardımcısı aynı yolu kullanır):
/// <list type="bullet">
/// <item>TV'den kaldırılan uygulamanın kaydı paket yöneticisinde kalabilir (veri saklanarak kaldırma, arşivleme): dumpsys
/// sürümü yine söyler ama uygulama ana kullanıcıda (User 0) kurulu değildir; TV uygulamayı yalnız ana kullanıcıdan
/// kaldırıp çocuk/ikincil profilde bırakmış da olabilir (#528). Kurulum aracı dumpsys'e bakıp "güncel" der ve kurmazdı;
/// başka profilde kuruluysa önce APK göndermeden geri getirilir (pm install-existing), olmazsa kalıntı kaldırılır ve
/// uygulama baştan kurulur (#352, #389).</item>
/// <item>"TV uygulamasını yeniden kur"da uygulama TV'de güncelse de açılır.</item>
/// </list>
/// </summary>
public static partial class AndroidTvApp
{
    private const string Package = AndroidTvInstaller.PackageName;

    /// <summary>dumpsys'teki kullanıcı satırları: "User 0: ceDataInode=… installed=true hidden=false …".</summary>
    [GeneratedRegex(@"^\s*User (\d+):.*?\binstalled=(true|false)\b", RegexOptions.Multiline)]
    private static partial Regex UserInstalled();

    /// <summary>pm install-existing komutu: başka profilde kurulu APK'yı ana kullanıcıya geri getirir (#528).</summary>
    public const string InstallExistingCommand = $"shell:pm install-existing --user 0 {Package}";

    private static Dictionary<string, bool> Users(string dumpsys) =>
        UserInstalled().Matches(dumpsys).GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups[2].Value == "true");

    /// <summary>
    /// dumpsys sürüm söylüyor ama uygulama TV'nin ana kullanıcısında (User 0) kurulu değil: kaydı kalmış (#352, #389) ya da
    /// TV uygulamayı yalnız ana kullanıcıdan kaldırmış, çocuk/ikincil profilde kurulu kalmış (#528).
    /// Kullanıcı satırı yoksa (eski Android) kalıntı sayılmaz. Telefondaki AndroidTvInstaller.isLeftover ile aynıdır.
    /// </summary>
    public static bool IsLeftover(string dumpsys)
    {
        if (AndroidTvInstaller.VersionFromDumpsys(dumpsys) is null) return false;
        var users = Users(dumpsys);
        if (users.Count == 0) return false;
        return (users.TryGetValue("0", out var main) && !main) || !users.Values.Any(installed => installed);
    }

    /// <summary>Kalıntı ama APK başka bir profilde kurulu: pm install-existing ile APK göndermeden geri getirilir (#528).</summary>
    public static bool CanInstallExisting(string dumpsys) => IsLeftover(dumpsys) && Users(dumpsys).Values.Any(installed => installed);

    /// <summary>pm install-existing başarılı mı ("Package com.couchsubs.tv installed for user: 0")?</summary>
    public static bool InstallExistingSucceeded(string output) => output.Contains("installed for user", StringComparison.Ordinal);

    /// <summary>
    /// Kalıntıyı kaldırır, uygulamayı gerekirse kurar; <paramref name="openIfInstalled"/> ise güncel uygulamayı da açar.
    /// Her adımın süre sınırı vardır (<see cref="TvStall"/>): TV yanıt vermeyi keserse bağlantı kapatılır ve yeni bağlantıyla
    /// bir kez daha denenir; ikinci denemede güncel bulunan uygulama da açılır.
    /// </summary>
    /// <param name="stallLimits">Testler için adım süreleri; verilmezse <see cref="TvStall.LimitFor"/>.</param>
    public static async Task<(string Version, bool Installed)> EnsureInstalledAsync(AndroidTvInstaller installer, string host,
        string keyDirectory, string apkPath, string version, bool openIfInstalled, Action? onApprovalNeeded, Action<string>? log,
        CancellationToken cancellationToken = default, Func<string, TimeSpan?>? stallLimits = null)
    {
        return await LoggedAsync($"TV {host}:{AndroidTvInstaller.DefaultPort}, paket {version}", log, async () =>
        {
            installer.Installed = false;
            if (installer.ReplaceForeign)
                log?.Invoke($"Android TV: kullanıcı onayladı; {Package} kimliğini kullanan başka imzalı uygulama varsa kaldırılıp Çevirgen kurulacak.");
            var result = await TvStall.RetryOnceAsync(async attempt =>
            {
                using var stall = new TvStall(cancellationToken, stallLimits);
                installer.Stall = stall;
                try
                {
                    return await stall.RunAsync(async token =>
                    {
                        await RemoveLeftoverAsync(host, keyDirectory, onApprovalNeeded, log, stall, token);
                        var found = await installer.EnsureInstalledAsync(apkPath, version, token);
                        if (!found.Installed && (openIfInstalled || attempt > 1))
                        {
                            stall.Enter("launching");
                            await using var adb = await ConnectAsync(host, keyDirectory, onApprovalNeeded, token);
                            stall.OnStall(() => _ = adb.DisposeAsync().AsTask().ContinueWith(t => _ = t.Exception, TaskScheduler.Default));
                            var started = await adb.RunAsync(AndroidTvInstaller.StartCommand(installer.LaunchParams), cancellationToken: token);
                            log?.Invoke($"Android TV: uygulama güncel ({found.Version}), açıldı: {started.Trim()}");
                        }
                        return found;
                    });
                }
                finally
                {
                    installer.Stall = null;
                }
            }, IsRetryable, log);
            return (result.Version, result.Installed || installer.Installed);
        });
    }

    /// <summary>Takılma ya da bağlantı kopması: yeni bağlantıyla bir kez daha denenir.</summary>
    internal static bool IsRetryable(Exception error) =>
        error is TvStepTimeoutException or IOException or System.Net.Sockets.SocketException;

    /// <summary>
    /// Kurulumun başı, sonucu ve hatası günlüğe telefonlardaki satırlarla yazılır ("--- Android TV kurulumu … başlıyor",
    /// "Kurulum tamam.", "HATA …"). Önceden masaüstünde Android TV kurulumu yalnız kalıntı temizliğini yazıyordu; hata
    /// olunca gönderilecek günlükte kurulumun izi yoktu.
    /// </summary>
    internal static async Task<(string Version, bool Installed)> LoggedAsync(string target, Action<string>? log,
        Func<Task<(string Version, bool Installed)>> install)
    {
        log?.Invoke($"--- Android TV kurulumu başlıyor: {target}");
        try
        {
            var result = await install();
            log?.Invoke(result.Installed ? "Kurulum tamam." : $"TV'de {result.Version} sürümü var; kurulum gerekmedi.");
            return result;
        }
        catch (AndroidInstallException error)
        {
            log?.Invoke($"HATA {error.Message}");
            throw;
        }
        catch (TvStepTimeoutException error)
        {
            // Arayüz "Kurulum sırasında TV bağlantısı koptu" ve "Tekrar dene" gösterir (ConnectionLost).
            log?.Invoke($"HATA ConnectionLost: {TvStall.GaveUp(error)}");
            throw;
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("Kurulum iptal edildi.");
            throw;
        }
        catch (Exception error)
        {
            log?.Invoke($"Beklenmeyen hata: {error}");
            throw;
        }
    }

    private static async Task RemoveLeftoverAsync(string host, string keyDirectory, Action? onApprovalNeeded, Action<string>? log,
        TvStall stall, CancellationToken cancellationToken)
    {
        stall.Enter("approve");
        await using var adb = await ConnectAsync(host, keyDirectory, onApprovalNeeded, cancellationToken);
        stall.OnStall(() => _ = adb.DisposeAsync().AsTask().ContinueWith(t => _ = t.Exception, TaskScheduler.Default));
        stall.Enter("reading");
        var dumpsys = await adb.RunAsync($"shell:dumpsys package {Package}", cancellationToken: cancellationToken);
        if (!IsLeftover(dumpsys)) return;
        // Kalıntı başka imzalı bir uygulamanınsa (bizim kimliğimizle) geri getirilmez ve sessizce silinmez: kurulum adımı
        // kullanıcıya sorar (AndroidInstallError.ForeignApp).
        if (AndroidTvInstaller.IsForeign(dumpsys))
        {
            log?.Invoke("Android TV: paket kaydı başka bir imzayla kalmış; kullanıcı onayı olmadan dokunulmuyor.");
            return;
        }
        if (CanInstallExisting(dumpsys))
        {
            var existing = await adb.RunAsync(InstallExistingCommand, cancellationToken: cancellationToken);
            log?.Invoke($"Android TV: uygulama başka bir profilde kurulu; ana kullanıcıya geri getiriliyor: {existing.Trim()}");
            if (InstallExistingSucceeded(existing)) return;
        }
        var removed = await adb.RunAsync($"shell:pm uninstall {Package}", cancellationToken: cancellationToken);
        log?.Invoke($"Android TV: paket kaydı kalmış ama uygulama kurulu değil; kalıntı kaldırıldı: {removed.Trim()}");
    }

    private static async Task<AdbConnection> ConnectAsync(string host, string keyDirectory, Action? onApprovalNeeded,
        CancellationToken cancellationToken)
    {
        using var key = AndroidTvInstaller.LoadOrCreateKey(keyDirectory);
        return await AdbConnection.ConnectAsync(host, AndroidTvInstaller.DefaultPort, key, onApprovalNeeded,
            cancellationToken: cancellationToken);
    }
}
