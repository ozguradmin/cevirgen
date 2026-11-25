// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using TizenSdb;
using TizenSdb.SdbClient;
using TizenSdb.SigningManager;

namespace Cevirgen.TvInstall.Samsung;

public enum SamsungInstallError
{
    /// <summary>TV'de geliştirici modu kapalı ya da TV'ye girilen bilgisayar IP'si bu bilgisayarınki değil.</summary>
    Unreachable,
    LoginFailed,
    CertificateFailed,
    /// <summary>TV, Tizen 2.4'ten (2016) eski.</summary>
    TooOld,
    InstallFailed,
    /// <summary>TV'de yer yok ([116]).</summary>
    NotEnoughSpace,
    /// <summary>TV'de başka sertifikayla imzalı eski kopya var ve kaldırılamadı.</summary>
    CertificateMismatch,
    /// <summary>TV imza sertifikasını reddetti (internet, saat ya da sertifika); bir sonraki denemede yenisi alınır.</summary>
    CertificateRejected,
    /// <summary>TV'nin saati geride; sertifika ona göre henüz geçerli değil.</summary>
    TvClockWrong,
    /// <summary>TV, silinmiş eski kopyanın kaydı yüzünden paket kimliğini engelliyor ([118, -22]).</summary>
    PackageBlocked,
    /// <summary>Kurulum sırasında TV bağlantısı koptu.</summary>
    ConnectionLost,
    /// <summary>TV'de Çevirgen'in kimliğiyle (CouchSubsT) başka bir uygulama kurulu; kaldırmak için kullanıcı onayı gerekir.</summary>
    ForeignApp,
}

/// <param name="detail">Kullanıcıya da gösterilen ayrıntı: TV'nin hata satırı, Samsung'un yanıtı ya da istisna iletisi.</param>
public sealed class SamsungInstallException(SamsungInstallError error, string? detail = null)
    : Exception(detail is null ? error.ToString() : $"{error}: {detail}")
{
    public SamsungInstallError Error { get; } = error;

    public string? Detail { get; } = detail;
}

/// <summary>Kurulumun TV ile konuşan kısmı; testlerde gerçek TV yerine sahtesi kullanılır.</summary>
internal interface ISamsungTvShell
{
    /// <summary>Hazır (imzalı) paketi gönderip <c>vd_appinstall</c> ile kurar; TV'nin bütün çıktısını döner.</summary>
    Task<string> InstallAsync(CancellationToken cancellationToken);

    Task<string> ShellAsync(string command, CancellationToken cancellationToken);
}

/// <summary>
/// Çevirgen'in Samsung TV uygulamasını geliştirici modu açık TV'ye kurar: TV'nin DUID'ini okur, gerekirse Samsung
/// hesabıyla TV'ye özel sertifika alır, paketi imzalar ve kurar. sdb bağlantısı ve imza TizenSdb.Core'dan (MIT).
/// </summary>
public sealed partial class SamsungTvInstaller(string host, string profilesDirectory, SamsungCertificates certificates)
{
    public const int SdbPort = 26101;
    // Samsung paketinin kimliği ve adı eski ad (CouchSubs) ile bilerek korunur: TV'de kurulu paket bu kimliktedir.
    public const string AppId = "CouchSubsT.CouchSubs";
    internal const string PackageId = "CouchSubsT";

    /// <summary>
    /// Desteklenen en eski Tizen: 2.4 (2016, deneysel; #71'e göre elle kurulumu çalışıyor), telefonlardaki kurulumla
    /// aynı. Sayfa ES5 ile yazıldığından eski tarayıcılarda da çalışır. Tizen 2.x'te paket imzalanmadan önce
    /// sadeleştirilir (<see cref="LegacyTizenPackage"/>). 2.3 ve eskisi reddedilir.
    /// </summary>
    internal const string MinimumTizen = "2.4";

    /// <summary>
    /// Bu sürüme kadar (dahil) TV, kurulumdan önce dağıtıcı sertifikasının cihaz profilini (device-profile.xml) ister;
    /// yenilerde izin imzanın içindedir. Eşik ve hedef klasör Apps2Samsung'un TizenPermitInstall'ından (MIT).
    /// </summary>
    internal const string DeviceProfileUpTo = "4.0";

    /// <summary>TV'nin bildirdiği Tizen sürümü desteklenmiyor mu? Sürüm okunamadıysa (boş) kurulum denenir.</summary>
    internal static bool IsTooOld(string platform) => platform.Length > 0 && Versions.IsNewer(MinimumTizen, platform);

    /// <summary>Tizen 2.4–4.0 (2016–2018): kurulumdan önce cihaz profili TV'ye gönderilmeli.</summary>
    internal static bool NeedsDeviceProfile(string platform) =>
        platform.Length > 0 && !IsTooOld(platform) && !Versions.IsNewer(platform, DeviceProfileUpTo);

    /// <summary>Tizen 2.x'te cihaz profilinin konduğu klasör (Tizen Studio ve Apps2Samsung ile aynı).</summary>
    internal const string LegacyDeviceProfileDirectory = "/home/developer";

    /// <summary>
    /// Cihaz profilinin TV'deki yolu. Tizen 3.0–4.0'da <c>sdk_toolpath</c>; Tizen 2.x (2016) TV'ler profili yalnız
    /// <c>/home/developer</c>'da arar (Samsung'un "Permitting Device to Install Apps" belgesi, Apps2Samsung'un
    /// TizenPermitInstall.TargetPath'i). #71'de 40KU7000'e kurulan paket profili oraya gönderen Apps2Samsung'la kuruldu;
    /// Çevirgen'in profili <c>/opt/usr/apps/tmp</c>'ye gönderdiği her 2.4 kurulumu imza biçiminden bağımsız
    /// <c>install failed[118]</c> verdi (#497, #621, #638, #890).
    /// </summary>
    internal static string DeviceProfileRemotePath(string platform, string toolPath) =>
        (LegacyTizenPackage.Applies(platform) ? LegacyDeviceProfileDirectory : toolPath) + "/device-profile.xml";

    /// <summary>Sertifika yoksa Samsung girişini başlatır; yoksa null döner ve kurulum giriş adımında durur.</summary>
    public Func<CancellationToken, Task<SamsungAccount>>? SignIn { get; init; }

    public Action<string>? Progress { get; init; }

    /// <summary>Her adım ve TV'nin bütün yanıtları buraya yazılır.</summary>
    public InstallLog Log { get; init; } = new(null);

    /// <summary>
    /// TV'deki sürüm güncel olduğu için kurulum gerekmese de uygulama açılsın ("TV uygulamasını yeniden kur"). Önceden
    /// yalnız kurulumdan sonra açılıyordu; düğmeye basmak Samsung'da kapalı uygulamayı geri getirmiyordu.
    /// </summary>
    public bool OpenIfInstalled { get; init; }

    /// <summary>
    /// Kullanıcı "Kaldırıp Kur" dedi: TV'de Çevirgen'in kimliğini kullanan başka uygulama kaldırılıp Çevirgen kurulur.
    /// Verilmezse (varsayılan) böyle bir uygulamada <see cref="SamsungInstallError.ForeignApp"/> fırlatılır, hiçbir şey silinmez.
    /// </summary>
    public bool ReplaceForeign { get; init; }

    /// <summary>
    /// TV'de geliştirici modu açık ve bu bilgisayar kabul ediliyor mu? Samsung yalnız geliştirici modunda girilen IP'den
    /// gelen bağlantıyı kabul eder; başka bilgisayardan port açık görünse de bağlantı hemen kapanır.
    /// </summary>
    public static async Task<bool> IsDevModeOnAsync(string host, TimeSpan? timeout = null)
    {
        using var tcp = new TcpClient();
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        try
        {
            await tcp.ConnectAsync(host, SdbPort, limit.Token);
            return true;
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Kurulu TV uygulamasını geliştirici bağlantısıyla (sdb, <c>was_execute</c>) açar; arka plandaysa öne getirir.
    /// Uzaktan kumanda bağlantısıyla (8002) açma bazı TV'lerde (Tizen 9.0) sessizce çalışmıyordu; sdb izin sormaz ve
    /// TV'de bildirim çıkarmaz. Geliştirici modu kapalıysa ya da Host PC IP başka bilgisayarınsa false döner.
    /// </summary>
    public async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        Log.Write($"--- Samsung: TV uygulaması bağlı değil, açılıyor (TV {host})");
        try
        {
            await using var device = await ConnectAsync(cancellationToken);
            var reply = await device.ShellCommandAsync($"0 was_execute {AppId}", cancellationToken);
            Log.Write($"Uygulama açılıyor: {reply.Trim()}");
            return Launched(reply);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Write($"Uygulama açılamadı: {error.Message}");
            return false;
        }
    }

    /// <summary><c>was_execute</c> yanıtı uygulamanın açıldığını söylüyor mu ("app_id[…] launched").</summary>
    internal static bool Launched(string reply) =>
        reply.Contains("] launched", StringComparison.Ordinal) && !reply.Contains("fail", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu TV için kayıtlı geçerli sertifika var mı (giriş gerekmeden kurulabilir mi)?</summary>
    public async Task<bool> HasCertificateAsync(CancellationToken cancellationToken = default)
    {
        await using var device = await ConnectAsync(cancellationToken);
        var duid = await ReadDuidAsync(device, cancellationToken);
        var profile = SigningProfile.In(ProfileDirectory(duid));
        return profile.IsValid(DateTime.UtcNow) && profile.IsFor(duid);
    }

    /// <summary>Paketi imzalayıp kurar ve uygulamayı açar (TV'deki sürüme bakmadan).</summary>
    /// <param name="wgtPath">İmzasız ya da imzalı .wgt; dosyanın kendisine dokunulmaz.</param>
    public Task InstallAsync(string wgtPath, CancellationToken cancellationToken = default) =>
        EnsureInstalledAsync(wgtPath, version: null, cancellationToken);

    /// <summary>
    /// TV'deki Çevirgen sürümüne bakar (<c>0 vd_applist</c>); yoksa ya da <paramref name="version"/>'dan eskiyse
    /// imzalayıp kurar. Bilgisayar yalnız kendi kurduğu sürümü hatırlıyordu; TV'de başka yoldan kurulmuş ya da eski
    /// kalmış uygulama (ör. 0.9.10 gelirken TV'de 0.9.7) hiç güncellenmiyordu.
    /// </summary>
    /// <param name="version">Paketin sürümü; null ise TV'deki sürüme bakılmadan kurulur.</param>
    /// <returns>TV'deki sürüm ve bu çağrının kurup kurmadığı.</returns>
    public async Task<(string Version, bool Installed)> EnsureInstalledAsync(string wgtPath, string? version,
        CancellationToken cancellationToken = default)
    {
        Log.Write($"--- Samsung kurulumu başlıyor: TV {host}, paket {Path.GetFileName(wgtPath)}");
        installCompleted = false;
        try
        {
            // Her adımın süre sınırı var (TvStall); takılırsa ya da bağlantı koparsa yeni bağlantıyla bir kez daha denenir.
            // İkinci denemede güncel bulunan uygulama da açılır (ilk deneme kurup açamadan takılmış olabilir).
            var result = await TvStall.RetryOnceAsync(async attempt =>
            {
                using var stall = new TvStall(cancellationToken, StallLimits);
                stallWatch = stall;
                try { return await stall.RunAsync(token => InstallCoreAsync(wgtPath, version, OpenIfInstalled || attempt > 1, token)); }
                finally { stallWatch = null; }
            }, IsRetryable, Log.Write);
            result = (result.Version, result.Installed || installCompleted);
            Log.Write(result.Installed ? "Kurulum tamam." : $"TV'de {result.Version} sürümü var; kurulum gerekmedi.");
            return result;
        }
        catch (TvStepTimeoutException error)
        {
            var detail = TvStall.GaveUp(error);
            Log.Write($"HATA {SamsungInstallError.ConnectionLost}: {detail}");
            throw new SamsungInstallException(SamsungInstallError.ConnectionLost, detail);
        }
        catch (SamsungInstallException error)
        {
            Log.Write($"HATA {error.Error}: {error.Detail}");
            throw;
        }
        catch (OperationCanceledException)
        {
            Log.Write("Kurulum iptal edildi.");
            throw;
        }
        catch (Exception error)
        {
            Log.Write($"Beklenmeyen hata: {error}");
            throw;
        }
    }

    /// <summary>Takılma ya da bağlantı kopması: yeni bağlantıyla bir kez daha denenir.</summary>
    internal static bool IsRetryable(Exception error) =>
        error is TvStepTimeoutException or IOException or SocketException ||
        error is SamsungInstallException { Error: SamsungInstallError.ConnectionLost };

    /// <summary>Testler için adım süreleri; verilmezse <see cref="TvStall.LimitFor"/>.</summary>
    internal Func<string, TimeSpan?>? StallLimits { get; init; }

    /// <summary>Sürmekte olan denemenin bekçisi.</summary>
    private TvStall? stallWatch;

    /// <summary>Bu çağrıda paket kuruldu mu (ikinci deneme güncel bulsa da "kuruldu" sayılır).</summary>
    private bool installCompleted;

    /// <summary>Yeni adım: bekçinin süresi baştan başlar; <paramref name="report"/> ise arayüze de bildirilir.</summary>
    private void Step(string step, bool report = true)
    {
        stallWatch?.Enter(step);
        if (report) Progress?.Invoke(step);
    }

    private async Task<(string Version, bool Installed)> InstallCoreAsync(string wgtPath, string? version, bool openIfCurrent,
        CancellationToken cancellationToken)
    {
        Step("connecting", report: false);
        await using var device = await ConnectAsync(cancellationToken);
        // Takılınca bağlantı kapatılır: iptal belirteci almayan TizenSdb kurulumu da bekleyen okumadan hatayla döner.
        stallWatch?.OnStall(() => _ = device.DisposeAsync().AsTask().ContinueWith(t => _ = t.Exception, TaskScheduler.Default));
        Step("reading");
        var capabilities = await device.CapabilityAsync(cancellationToken);
        var platform = capabilities.GetValueOrDefault("platform_version", "").Trim();
        var toolPath = capabilities.GetValueOrDefault("sdk_toolpath")?.Trim() is { Length: > 0 } path ? path : "/opt/usr/apps/tmp";
        Log.Write($"TV: Tizen {(platform.Length > 0 ? platform : "?")}, sdk_toolpath {toolPath}");
        if (IsTooOld(platform))
            throw new SamsungInstallException(SamsungInstallError.TooOld, $"Tizen {platform}");
        if (platform.Length > 0 && Versions.IsNewer("5.0", platform))
            Log.Write($"Tizen {platform}: 2016–2018 TV'si, destek deneysel.");

        var onTv = await ReadInstalledVersionAsync(device, cancellationToken);
        if (onTv is not null && IsForeignTitle(lastTitle))
        {
            // Bizim kimliğimizle başka adlı bir uygulama: sürümü bizimkinden yüksekse "güncel" sayılıp o açılıyordu.
            // Samsung'da imza her kullanıcının kendi Samsung sertifikasıdır, ayırt edici değildir; ada (Çevirgen) bakılır.
            Log.Write($"TV'de Çevirgen kimliğiyle başka bir uygulama var: \"{lastTitle}\" {onTv}.");
            if (!ReplaceForeign) throw new SamsungInstallException(SamsungInstallError.ForeignApp, $"\"{lastTitle}\" {onTv}");
            Step("uninstalling");
            foreach (var id in new[] { PackageId, AppId })
            {
                try { Log.Write($"Kullanıcı onayıyla kaldırılıyor {id}: {(await device.ShellCommandAsync($"0 vd_appuninstall {id}", cancellationToken)).Trim()}"); }
                catch (Exception error) when (error is not OperationCanceledException) { Log.Write($"{id} kaldırılamadı: {error.Message}"); }
            }
            onTv = null;
        }
        if (version is not null && onTv is not null && !Versions.IsNewer(version, onTv))
        {
            if (openIfCurrent) await LaunchAsync(device, cancellationToken);
            return (onTv, false);
        }
        Log.Write(onTv is null ? "TV'de Çevirgen görünmüyor; kuruluyor." : $"TV'de {onTv} var, {version ?? "yeni sürüm"} kuruluyor.");

        var duid = await ReadDuidAsync(device, cancellationToken);
        Log.Write($"DUID: {duid}");
        var directory = ProfileDirectory(duid);
        var profile = SigningProfile.In(directory);
        Log.Write($"Kayıtlı sertifika: {profile.Describe(DateTime.UtcNow, duid)}");
        if (!profile.IsValid(DateTime.UtcNow) || !profile.IsFor(duid))
        {
            if (SignIn is null) throw new SamsungInstallException(SamsungInstallError.LoginFailed, "giriş gerekiyor");
            Step("signin");
            Log.Write("Samsung hesabıyla giriş bekleniyor (tarayıcı açılıyor).");
            var account = await SignIn(cancellationToken);
            Log.Write("Samsung girişi tamam; sertifika isteniyor.");
            Step("certificate");
            profile = await certificates.CreateAsync(duid, account, directory, cancellationToken);
            Log.Write($"Yeni sertifika: {profile.Describe(DateTime.UtcNow, duid)}");
        }

        if (NeedsDeviceProfile(platform)) await PushDeviceProfileAsync(device, profile, platform, toolPath, cancellationToken);

        Step("signing");
        DeleteLeftovers(Path.GetTempPath());
        var staged = Path.Combine(Path.GetTempPath(), $"couchsubs-{Guid.NewGuid():N}.wgt");
        File.Copy(wgtPath, staged);
        // Kurulacak paket en sondaki; Tizen 2.x'te gerekirse yanına yalın paket eklenir (ayrı dosya: TizenInstaller
        // ilkini açık bırakabiliyor).
        var packages = new List<string> { staged };
        try
        {
            // Tizen 2.x (2016): #71'de çalışan paket: o config.xml ve SHA-512 imza (Apps2Samsung'un TizenSdb'si, #890);
            // TV yine yalın [118] derse SHA-256 imzalı yalın paket (#638).
            var legacy = LegacyTizenPackage.Applies(platform);
            if (legacy)
            {
                if (LegacyTizenPackage.Adapt(staged))
                    Log.Write($"Tizen {platform}: paketten network.public izni ve ultra geniş ekran ayarı çıkarıldı (#621).");
                await TizenWgtSigner.ReSignWgtWithCertsInPlace(staged, profile.AuthorP12, profile.DistributorP12, profile.Password);
                Log.Write($"Tizen {platform}: imza SHA-512 ile üretildi (#71'de çalışan paketle aynı).");
            }
            else
            {
                // 21:9 / 32:9 Samsung monitör: geniş uygulama yüzeyi (#886; TV'lerde paket değişmez).
                var (model, resolution) = await ReadScreenInfoAsync(host, cancellationToken);
                Log.Write($"Ekran: model {model ?? "?"}, çözünürlük {resolution ?? "?"}");
                if (WideScreenPackage.Applies(model, resolution) && WideScreenPackage.Adapt(staged))
                    Log.Write("Ultra geniş monitör: paket geniş uygulama yüzeyiyle (base_screen_resolution, device-width) kuruluyor (#886).");
                await TizenWgtSigner.ReSignWgtWithCertsInPlace(staged, profile.AuthorP12, profile.DistributorP12, profile.Password);
            }
            Log.Write("Paket imzalandı.");
            // Güncellemede TV'de çalışan eski uygulama önce kapatılır: kurulum açık sayfanın altından paketi değiştirmesin,
            // sonra açılan uygulama yeni sürümü temiz yüklesin.
            if (onTv is not null) await CloseRunningAppAsync(device, cancellationToken);
            Step("installing");
            try
            {
                await InstallWithRecoveryAsync(new SdbShell(device, () => packages[^1], toolPath), step => Step(step), Log, cancellationToken,
                    alternative: !legacy ? null : () =>
                    {
                        var lean = Path.Combine(Path.GetTempPath(), $"couchsubs-{Guid.NewGuid():N}.wgt");
                        File.Copy(wgtPath, lean);
                        packages.Add(lean);
                        LegacyTizenPackage.Adapt(lean, lean: true);
                        WgtSigner.SignInPlace(lean, profile.AuthorP12, profile.DistributorP12, profile.Password, WgtDigest.Sha256);
                        Log.Write($"Tizen {platform}: yalın paket hazırlandı ({LegacyTizenPackage.LeanRemoved} de çıkarıldı), imza SHA-256.");
                        return "yalın paket";
                    }, keepExisting: legacy && (onTv is not null || !lastListRead));
                installCompleted = true;
                if (legacy) Log.Write($"Tizen {platform}: {(packages.Count > 1 ? "yalın paket SHA-256" : "tam paket SHA-512")} imzayla kuruldu.");
            }
            catch (SamsungInstallException error)
            {
                if (error.Error is SamsungInstallError.CertificateRejected)
                {
                    // TV bu sertifikayı kabul etmiyor; bir sonraki denemede Samsung'dan yenisi istensin.
                    SigningProfile.Discard(directory);
                    Log.Write("Kayıtlı sertifika bırakıldı; bir sonraki denemede yenisi alınacak.");
                }
                if (!legacy || onTv is null) throw;
                // Tizen 2.x: TV'deki kopya (ör. Tizen Studio ile elle kurulmuş) yerinde; yeni paket kurulamadı diye
                // çalışan uygulama "kurulamadı" gösterilmez, o açılır (#638).
                Log.Write($"Tizen {platform}: yeni paket kurulamadı ({error.Error}); TV'deki Çevirgen {onTv} korunuyor ve açılıyor.");
                await LaunchAsync(device, cancellationToken);
                return (onTv, false);
            }
        }
        finally
        {
            foreach (var package in packages) DeleteStaged(package);
        }
        // TV "kuruldu" deyip eski sürümde kalırsa bu da sessiz geçmesin.
        var after = await ReadInstalledVersionAsync(device, cancellationToken);
        if (version is not null && after is not null && Versions.IsNewer(version, after))
            throw new SamsungInstallException(SamsungInstallError.InstallFailed,
                $"TV kurulumu bitirdi ama TV'de hâlâ {after} sürümü var ({version} kurulmalıydı)");
        await LaunchAsync(device, cancellationToken);
        return (after ?? version ?? "", true);
    }

    /// <summary>Son <c>vd_applist</c> okunabildi mi ("Çevirgen yok" ile "bilinmiyor" ayrılsın).</summary>
    private bool lastListRead;

    /// <summary>Son <c>vd_applist</c>'te Çevirgen kimliğindeki uygulamanın adı (app_title); yoksa null.</summary>
    private string? lastTitle;

    /// <summary>Samsung paketinin TV'deki adı (eski ad korunur; testdata/tizen/config.xml &lt;name&gt;).</summary>
    internal const string OurTitle = "CouchSubs";

    /// <summary>Ad biliniyor ve Çevirgen değil: başka bir uygulama bizim kimliğimizi kullanıyor. Ad yoksa yabancı sayılmaz.</summary>
    internal static bool IsForeignTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && !title.Trim().Equals(OurTitle, StringComparison.OrdinalIgnoreCase);

    /// <summary>TV'deki Çevirgen sürümü; uygulama yoksa ya da liste okunamazsa null.</summary>
    private async Task<string?> ReadInstalledVersionAsync(SdbTcpDevice device, CancellationToken cancellationToken)
    {
        lastListRead = false;
        lastTitle = null;
        try
        {
            var listing = await device.ShellCommandAsync("0 vd_applist", cancellationToken);
            lastListRead = true;
            var found = InstalledVersion(listing);
            lastTitle = InstalledTitle(listing);
            Log.Write($"vd_applist: {(found is null ? "Çevirgen yok" : $"Çevirgen {found}")} ({listing.Length} karakter)");
            return found;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Write($"vd_applist okunamadı: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>vd_applist</c> çıktısında Çevirgen'in sürümü. Çıktı uygulama bloklarından oluşur, her satır
    /// <c>---- anahtar = değer ----</c> biçimindedir (Apps2Samsung'un TizenInstalledApps'i).
    /// </summary>
    internal static string? InstalledVersion(string? listing) => InstalledField(listing, "app_version");

    /// <summary><c>vd_applist</c> çıktısında Çevirgen kimliğindeki uygulamanın adı (app_title).</summary>
    internal static string? InstalledTitle(string? listing) => InstalledField(listing, "app_title");

    private static string? InstalledField(string? listing, string key)
    {
        if (string.IsNullOrWhiteSpace(listing)) return null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? found = null;

        void Flush()
        {
            var ids = new[] { fields.GetValueOrDefault("app_tizen_id"), fields.GetValueOrDefault("app_id") };
            if (found is null && ids.Any(IsCevirgen) && fields.GetValueOrDefault(key) is { Length: > 0 } value)
                found = value;
            fields.Clear();
        }

        foreach (var raw in listing.Replace("\r", "").Split('\n'))
        {
            if (!raw.Contains('=') && raw.Contains("----"))
            {
                Flush();
                continue;
            }
            var match = FieldLine().Match(raw);
            if (match.Success) fields[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        }
        Flush();
        return found;
    }

    private static bool IsCevirgen(string? id) =>
        id is { Length: > 0 } && (id.Trim().Equals(AppId, StringComparison.OrdinalIgnoreCase) ||
                                  id.Trim().Equals(PackageId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kurar; TV başka sertifikayla kurulmuş eski kopya ya da paket kimliği yüzünden reddederse eski kopyayı kaldırıp bir
    /// kez daha dener. Sonuç yine başarısızsa TV'nin söylediği sebeple <see cref="SamsungInstallException"/> atar.
    /// </summary>
    /// <param name="alternative">
    /// İki deneme de sebepsiz (yalın <c>[118]</c>) başarısız olursa çağrılır: başka biçimde hazırlanmış paketi sıraya
    /// koyup adını döner (yoksa null); o paketle üçüncü kez denenir.
    /// </param>
    /// <param name="keepExisting">
    /// TV'deki kopyaya dokunulmaz (<c>vd_appuninstall</c> yok): Tizen 2.x'te TV'de Çevirgen varsa ya da liste
    /// okunamadıysa. 2016 TV'de Tizen Studio ile elle kurulmuş, çalışan kopya kurtarma adımında silinmesin
    /// (#638). Sebepsiz <c>[118]</c>'de <paramref name="alternative"/> yine bir kez denenir.
    /// </param>
    internal static async Task InstallWithRecoveryAsync(ISamsungTvShell tv, Action<string>? progress, InstallLog log,
        CancellationToken cancellationToken, Func<string?>? alternative = null, bool keepExisting = false)
    {
        var output = await tv.InstallAsync(cancellationToken);
        log.Write($"vd_appinstall çıktısı:\n{output.Trim()}");
        var problem = SamsungInstallDiagnostics.Classify(output);
        if (problem == InstallProblem.None) return;
        log.Write($"Kurulum başarısız: {problem}");
        if (keepExisting && SamsungInstallDiagnostics.RemovingOldCopyMayHelp(problem))
        {
            log.Write("TV'de Çevirgen var ya da liste okunamadı (elle kurulmuş olabilir); eski kopya kaldırılmıyor.");
            if (problem == InstallProblem.Other && alternative?.Invoke() is { } kept)
            {
                var second = await tv.InstallAsync(cancellationToken);
                log.Write($"İkinci deneme ({kept}) vd_appinstall çıktısı:\n{second.Trim()}");
                var secondProblem = SamsungInstallDiagnostics.Classify(second);
                if (secondProblem == InstallProblem.None) return;
                throw new SamsungInstallException(SamsungInstallDiagnostics.ToError(secondProblem), SamsungInstallDiagnostics.Summary(second));
            }
            throw new SamsungInstallException(SamsungInstallDiagnostics.ToError(problem), SamsungInstallDiagnostics.Summary(output));
        }
        if (!SamsungInstallDiagnostics.RemovingOldCopyMayHelp(problem))
            throw new SamsungInstallException(SamsungInstallDiagnostics.ToError(problem), SamsungInstallDiagnostics.Summary(output));

        // Başka sertifikayla kurulmuş eski kopya ("Author certificate not match") ya da yarım kalmış kayıt: kaldırıp bir
        // kez daha dene. TV sürümüne göre kaldırma paket kimliğini ya da uygulama kimliğini bekliyor; ikisi de denenir.
        progress?.Invoke("uninstalling");
        log.Write($"Eski kopya kaldırıldı mı:\n{await RemoveAsync()}");
        var retry = await tv.InstallAsync(cancellationToken);
        log.Write($"İkinci deneme vd_appinstall çıktısı:\n{retry.Trim()}");
        var retryProblem = SamsungInstallDiagnostics.Classify(retry);
        if (retryProblem == InstallProblem.None) return;
        // TV iki kez de sebep söylemediyse (yalın [118]) ve başka biçimde hazırlanabilen paket varsa onunla bir kez daha.
        if (problem == InstallProblem.Other && retryProblem == InstallProblem.Other && alternative?.Invoke() is { } label)
        {
            log.Write($"Eski kopya kaldırıldı mı:\n{await RemoveAsync()}");
            var last = await tv.InstallAsync(cancellationToken);
            log.Write($"Üçüncü deneme ({label}) vd_appinstall çıktısı:\n{last.Trim()}");
            var lastProblem = SamsungInstallDiagnostics.Classify(last);
            if (lastProblem == InstallProblem.None) return;
            throw new SamsungInstallException(SamsungInstallDiagnostics.ToError(lastProblem), SamsungInstallDiagnostics.Summary(last));
        }
        throw new SamsungInstallException(SamsungInstallDiagnostics.ToError(retryProblem), SamsungInstallDiagnostics.Summary(retry));

        async Task<string> RemoveAsync()
        {
            var removal = "";
            foreach (var id in new[] { PackageId, AppId })
            {
                try { removal += $"{id}: {(await tv.ShellAsync($"0 vd_appuninstall {id}", cancellationToken)).Trim()}\n"; }
                catch (Exception error) when (error is not OperationCanceledException) { removal += $"{id}: {error.Message}\n"; }
            }
            return removal.Trim();
        }
    }

    /// <summary>
    /// Uygulamayı açar; açılmasa da kurulum başarılıdır (Mac ve Windows uygulaması da ayrıca açar). TizenSdb ve
    /// Apps2Samsung <c>was_execute</c> kullanır; eski <c>execute</c> bazı TV'lerde yoktur.
    /// </summary>
    private async Task LaunchAsync(SdbTcpDevice device, CancellationToken cancellationToken)
    {
        Step("launching", report: false);
        try
        {
            var reply = await device.ShellCommandAsync($"0 was_execute {AppId}", cancellationToken);
            Log.Write($"Uygulama açılıyor: {reply.Trim()}");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Write($"Uygulama açılamadı (kurulum yine tamam): {error.Message}");
        }
    }

    /// <summary>
    /// Güncellemeden önce TV'de çalışan Çevirgen'i kapatır (<c>was_kill</c>, uygulama ve paket kimliğiyle; bekçinin
    /// kapat-aç'ıyla aynı komutlar). Kapatılamazsa (uygulama zaten kapalı, eski TV komutu tanımıyor) kurulum yine sürer.
    /// </summary>
    private async Task CloseRunningAppAsync(SdbTcpDevice device, CancellationToken cancellationToken)
    {
        Step("closing", report: false);
        await CloseWithAsync(command => device.ShellCommandAsync(command, cancellationToken), Log,
            () => Task.Delay(KillSettle, cancellationToken));
    }

    /// <summary>Kapatma ile kuruluma geçme arasındaki bekleme (TV süreci bırakabilsin).</summary>
    internal static readonly TimeSpan KillSettle = TimeSpan.FromSeconds(1.5);

    /// <summary>Çalışan uygulamayı kapatma komutları: önce uygulama kimliği, sonra paket kimliği (TV yazılımına göre biri tutar).</summary>
    internal static string[] KillCommands => [$"0 was_kill {AppId}", $"0 was_kill {PackageId}"];

    /// <summary><see cref="CloseRunningAppAsync"/>'in komutları (testlerde sahte kabukla).</summary>
    internal static async Task CloseWithAsync(Func<string, Task<string>> shell, InstallLog log, Func<Task> pause)
    {
        foreach (var kill in KillCommands)
        {
            string reply;
            try { reply = await shell(kill); }
            catch (Exception error) when (error is not OperationCanceledException) { reply = error.Message; }
            log.Write($"Güncellemeden önce eski uygulama kapatılıyor ({kill}): {reply.Trim()}");
        }
        await pause();
    }

    /// <summary>
    /// Tizen 2.4–4.0: Tizen Studio'daki "Permit to install applications" adımı. Sertifika alınırken saklanan cihaz
    /// profili TV'ye gönderilir (yol: <see cref="DeviceProfileRemotePath"/>); TV, imzayı bu profildeki DUID listesine göre
    /// kabul eder. Tizen 2.x'te <c>/home/developer</c>'a gönderilemezse <c>sdk_toolpath</c> denenir. Profil yoksa ya da
    /// gönderilemezse kurulum yine denenir: TV reddederse sebebi kurulum çıktısında görünür.
    /// </summary>
    private async Task PushDeviceProfileAsync(SdbTcpDevice device, SigningProfile profile, string platform, string toolPath,
        CancellationToken cancellationToken)
    {
        var local = Path.Combine(profile.Directory, "device-profile.xml");
        if (!File.Exists(local))
        {
            Log.Write("Cihaz profili (device-profile.xml) yok; gönderilmeden kuruluyor.");
            return;
        }
        var remote = DeviceProfileRemotePath(platform, toolPath);
        var fallback = $"{toolPath}/device-profile.xml";
        foreach (var target in remote == fallback ? [remote] : new[] { remote, fallback })
        {
            try
            {
                await using var file = File.OpenRead(local);
                await device.PushAsync(file, target, ct: cancellationToken);
                Log.Write($"Cihaz profili gönderildi: {target}");
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log.Write($"Cihaz profili gönderilemedi ({target}): {error.Message}");
            }
        }
    }

    /// <summary>
    /// TizenSdb'nin TizenInstaller'ı paketi açıp kapatmıyor; Windows açık dosyanın silinmesine izin vermiyor. Silme
    /// hatası kurulumun asıl sonucunu örtmesin diye tutamak çöp toplayıcıyla bırakılır, yine silinemezse dosya geçici
    /// klasörde kalır (bir sonraki kurulumda temizlenir).
    /// </summary>
    internal static bool DeleteStaged(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        return false;
    }

    /// <summary>Önceki kurulumlardan geçici klasörde kalmış paketler.</summary>
    internal static void DeleteLeftovers(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "couchsubs-*.wgt"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-10)) DeleteStaged(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* temizlik isteğe bağlı */ }
    }

    private string ProfileDirectory(string duid) => Path.Combine(profilesDirectory, SafeName().Replace(duid, "_"));

    private async Task<SdbTcpDevice> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(host, out var address))
            throw new SamsungInstallException(SamsungInstallError.Unreachable, $"geçersiz TV adresi: {host}");
        // TV yalnız geliştirici modunda girilen Host PC IP'den gelen sdb bağlantısını kabul eder; ikisi de günlükte
        // olsun ki "TV kabul etmedi" hatasında hangi adresin yazılması gerektiği görülsün (#343).
        var local = SourceAddressTowards(address);
        var (developerMode, developerIp) = await ReadDeveloperInfoAsync(host, cancellationToken);
        Log.Write($"Bu bilgisayarın TV'ye giden adresi (LocalEndPoint): {local ?? "?"}; TV'deki Host PC IP (developerIP): " +
                  $"{developerIp ?? "?"}; geliştirici modu: {developerMode ?? "?"}");
        if (developerIp is not null && local is not null && developerIp != local)
            Log.Write($"UYARI: TV'deki Host PC IP ({developerIp}) bu bilgisayarın adresi ({local}) değil; TV bağlantıyı kesecek.");
        var device = new SdbTcpDevice(address, SdbPort);
        try
        {
            await device.ConnectAsync(cancellationToken);
            Log.Write($"sdb bağlantısı kuruldu ({host}:{SdbPort}).");
            return device;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await device.DisposeAsync();
            throw new SamsungInstallException(SamsungInstallError.Unreachable,
                $"sdb {host}:{SdbPort}: {error.Message} (bu bilgisayar {local ?? "?"}, TV'deki Host PC IP {developerIp ?? "?"})");
        }
    }

    /// <summary>
    /// TV'ye giden bağlantının bu bilgisayardaki kaynak adresi (TV'ye açılan soketin LocalEndPoint'i). UDP soketi
    /// "bağlanır", paket gönderilmez; işletim sistemi TCP için de aynı bağdaştırıcıyı seçer. Okunamazsa null.
    /// </summary>
    internal static string? SourceAddressTowards(IPAddress target)
    {
        try
        {
            using var socket = new Socket(target.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(target, SdbPort));
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static readonly HttpClient InfoHttp = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>TV'nin 8001 yanıtındaki geliştirici modu ve Host PC IP; okunamazsa ikisi de null.</summary>
    private static async Task<(string? Mode, string? Ip)> ReadDeveloperInfoAsync(string host, CancellationToken cancellationToken)
    {
        try { return DeveloperInfo(await InfoHttp.GetStringAsync($"http://{host}:8001/api/v2/", cancellationToken)); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    /// <summary>TV'nin 8001 yanıtındaki model adı ve çözünürlük; okunamazsa ikisi de null.</summary>
    private static async Task<(string? Model, string? Resolution)> ReadScreenInfoAsync(string host, CancellationToken cancellationToken)
    {
        try { return WideScreenPackage.ScreenInfo(await InfoHttp.GetStringAsync($"http://{host}:8001/api/v2/", cancellationToken)); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    /// <summary><c>device.developerMode</c> ve <c>device.developerIP</c> ("0.0.0.0" boş sayılır).</summary>
    internal static (string? Mode, string? Ip) DeveloperInfo(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("device", out var device) || device.ValueKind != System.Text.Json.JsonValueKind.Object)
                return (null, null);
            string? Field(string name) => device.TryGetProperty(name, out var value)
                ? (value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.GetRawText())?.Trim() is { Length: > 0 } text ? text : null
                : null;
            var ip = Field("developerIP");
            return (Field("developerMode"), ip == "0.0.0.0" ? null : ip);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<string> ReadDuidAsync(SdbTcpDevice device, CancellationToken cancellationToken)
    {
        // Yavaş TV'lerde ilk yanıt boş gelebiliyor; birkaç kez dene.
        var last = "";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last = await device.ShellCommandAsync("0 getduid", cancellationToken);
            var duid = last.Split('\n')[0].Trim();
            if (IsDuid(duid)) return duid;
            await Task.Delay(500, cancellationToken);
        }
        throw new SamsungInstallException(SamsungInstallError.Unreachable,
            $"TV kimliği (DUID) okunamadı; TV'nin yanıtı: {(last.Trim() is { Length: > 0 } text ? text : "boş")}");
    }

    internal static bool IsDuid(string text) => DuidPattern().IsMatch(text);

    /// <summary>Geriye uyumluluk ve testler için: çıktı kurulumun başarısız olduğunu mu söylüyor?</summary>
    internal static bool InstallFailed(string log) => SamsungInstallDiagnostics.Failed(log);

    /// <summary>TizenInstaller sonucu konsola yazar; çıktıyı yakalayıp hatayı oradan anlarız.</summary>
    private static readonly SemaphoreSlim ConsoleGate = new(1, 1);

    private static async Task<string> CaptureAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        // Takılıp bırakılmış önceki kurulum kapıyı tutuyorsa bekleme de adımın süresiyle sınırlıdır.
        await ConsoleGate.WaitAsync(cancellationToken);
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await body();
        }
        catch (Exception error)
        {
            writer.WriteLine($"install failed: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            Console.SetOut(original);
            ConsoleGate.Release();
        }
        return writer.ToString();
    }

    /// <summary>Gerçek TV: TizenSdb'nin sdb bağlantısı ve TizenInstaller'ı.</summary>
    private sealed class SdbShell(SdbTcpDevice device, Func<string> stagedPackage, string toolPath) : ISamsungTvShell
    {
        public Task<string> InstallAsync(CancellationToken cancellationToken) =>
            CaptureAsync(() => new TizenInstaller(stagedPackage(), device, toolPath).InstallApp(), cancellationToken);

        public Task<string> ShellAsync(string command, CancellationToken cancellationToken) =>
            device.ShellCommandAsync(command, cancellationToken);
    }

    [GeneratedRegex(@"^[\s-]*(?<key>[A-Za-z_]+)\s*=\s*(?<value>.*?)[\s-]*$")]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^[A-Za-z0-9_\-+=/]{8,}$")]
    private static partial Regex DuidPattern();

    [GeneratedRegex(@"[^A-Za-z0-9_\-]")]
    private static partial Regex SafeName();
}
