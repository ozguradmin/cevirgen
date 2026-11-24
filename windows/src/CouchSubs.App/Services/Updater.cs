// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Net.Http;
using CouchSubs.Core;
using CouchSubs.Shell;
using Velopack;
using Velopack.Sources;

namespace CouchSubs.App.Services;

/// <summary>
/// Çevirgen'in GitHub sürümlerinden güncelleme (Velopack; <see cref="UpdateChannel"/>). Yalnız kurulum dosyasıyla
/// (CevirgenApp-win-Setup.exe) kurulan uygulama kendini günceller; zip'ten açılan kopyada en son sürüm sayfası açılır.
/// Denetim hiçbir koşulda hata göstermez: depo özelken ya da sürüm yokken GitHub 404 döner, ağ yoksa istek düşer; ikisi
/// de "güncelleme yok" (null) sayılır ve yalnız günlüğe yazılır. Kurulum yalnız kullanıcı şeritteki düğmeye basınca yapılır.
/// </summary>
internal sealed class Updater : IUpdater
{
    private readonly UpdateManager manager = new(new GithubSource(UpdateChannel.Repository, null, UpdateChannel.Prerelease));
    private UpdateInfo? pending;
    private readonly GitHubReleaseNotes notes = new(new HttpClient
        { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 });

    public bool CanUpdateItself => manager.IsInstalled;
    /// <summary>Çevirgen güncelleme kanalı açık (github.com/ozguradmin/cevirgen).</summary>
    public const bool UpdatesEnabled = true;
    public Uri DownloadPage { get; } = UpdateChannel.DownloadPage;
    public UpdateAttemptJournal? Attempts { get; } =
        new(UpdateAttemptJournal.DefaultPath, AppVersion.Current(typeof(Updater).Assembly));

    /// <summary>
    /// Velopack kurulumu: %LOCALAPPDATA%\CevirgenApp\current\Cevirgen.exe; Update.exe günlüğünü bir üst klasöre
    /// (velopack.log) yazar.
    /// </summary>
    public string? UpdateLogPath =>
        manager.IsInstalled ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "velopack.log")) : null;

    public Task<IReadOnlyList<ReleaseNoteRun>> ReleaseNotesAsync(string version) => notes.LoadAsync(version);

    public async Task<string?> CheckAsync()
    {
        if (!manager.IsInstalled) return await LatestTagAsync();
        try
        {
            pending = await manager.CheckForUpdatesAsync();
            return pending?.TargetFullRelease.Version.ToString();
        }
        catch (Exception error)
        {
            // Depo özel/sürüm yok (404), ağ yok, GitHub hız sınırı: hepsi "güncelleme yok"; şerit hata göstermez.
            pending = null;
            SubtitleLog.Write($"güncelleme denetlenemedi: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    public async Task InstallAsync(IProgress<double> progress, Action? beforeRestart = null)
    {
        if (pending is null) return;
        await manager.DownloadUpdatesAsync(pending, percent => progress.Report(percent / 100.0));
        beforeRestart?.Invoke();
        // Update.exe kurulum klasörünü değiştirir; açık kalan yardımcı süreç (CTranslate2 python.exe, Xbox köprüsü
        // node.exe, seslendirme motoru) klasörü kilitlerse güncelleme uygulanamaz ve eski sürüm yeniden açılır.
        var running = ChildProcessGuard.StopAllForUpdate(TimeSpan.FromSeconds(5));
        if (running > 0)
        {
            var message = Localizer.Current["The update was postponed because CouchSubs couldn't confirm that all helper processes have exited. Restart CouchSubs and try again."];
            SubtitleLog.Write($"Güncelleme ertelendi: {running} yardımcı süreç kapanışı doğrulanamadı; " +
                              $"{pending.TargetFullRelease.Version} sürümü uygulanmadı.");
            SubtitleLog.Flush();
            throw new InvalidOperationException(message);
        }
        SubtitleLog.Write($"Güncelleme uygulanıyor: {pending.TargetFullRelease.Version}");
        SubtitleLog.Flush();
        manager.ApplyUpdatesAndRestart(pending);
    }

    /// <summary>Zip kullanıcısı için: GitHub'daki en son sürümün etiketi, bu sürümden yeniyse (404 ve ağ hatası: null).</summary>
    private static async Task<string?> LatestTagAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await UpdateChannel.NewerTagAsync(http, AppVersion.Current(typeof(Updater).Assembly));
    }
}
