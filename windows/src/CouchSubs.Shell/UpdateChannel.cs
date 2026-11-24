// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen'in güncelleme kanalı: GitHub'daki ozguradmin/cevirgen sürümleri (Velopack GithubSource, ön sürümler kapalı).
/// Kurulum dosyasıyla kurulmamış kopya en son sürümün etiketini API'den okur; indirme sayfası en son sürüm sayfasıdır.
/// Depo özelken ya da sürüm yokken GitHub 404 döner: bu "güncelleme yok" sayılır, kullanıcıya hata gösterilmez.
/// </summary>
public static class UpdateChannel
{
    public const string Repository = "https://github.com/ozguradmin/cevirgen";
    /// <summary>Kaynak kodu ve sürümler (Ayarlar › Uygulama).</summary>
    public static readonly Uri RepositoryPage = new(Repository);
    /// <summary>Sorun bildirme sayfası (Ayarlar › Uygulama; hata iletileri).</summary>
    public static readonly Uri IssuesPage = new(Repository + "/issues");
    public const string LatestReleaseApi = "https://api.github.com/repos/ozguradmin/cevirgen/releases/latest";
    public const string ReleaseTagApi = "https://api.github.com/repos/ozguradmin/cevirgen/releases/tags/";
    public const bool Prerelease = false;
    public static readonly Uri DownloadPage = new("https://github.com/ozguradmin/cevirgen/releases/latest");
    /// <summary>GitHub API'si User-Agent ister; kimlik değil, yalnız uygulamanın adı.</summary>
    public const string UserAgent = "Cevirgen";

    /// <summary>
    /// Kurulum dosyasıyla kurulmamış kopya için: GitHub'daki en son sürümün etiketi ("v" atılır), bu sürümden yeniyse.
    /// 404 (depo özel ya da sürüm yok), başka HTTP hatası, ağ hatası ya da bozuk yanıt null döner; hata fırlatmaz.
    /// </summary>
    public static async Task<string?> NewerTagAsync(HttpClient http, string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Core.SubtitleLog.Write($"güncelleme denetimi: GitHub HTTP {(int)response.StatusCode} (güncelleme yok sayıldı)");
                return null;
            }
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var tag = document.RootElement.GetProperty("tag_name").GetString()?.Trim().TrimStart('v');
            return !string.IsNullOrEmpty(tag) && AppVersion.IsNewer(tag, currentVersion) ? tag : null;
        }
        catch (Exception error)
        {
            Core.SubtitleLog.Write($"güncelleme denetlenemedi: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }
}
