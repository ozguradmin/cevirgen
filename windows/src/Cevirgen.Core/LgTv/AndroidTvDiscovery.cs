// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;

namespace Cevirgen.Core.LgTv;

/// <summary>
/// Ağdaki Android TV / Google TV'ler ve kimlikleri (Mac/iPhone <c>AndroidTVDiscovery</c> ile aynı kural). Android TV'ler
/// Google Cast'in cihaz tanımını (8008 <c>/ssdp/device-desc.xml</c>) yayınlar: adı ve kalıcı kimliği (UDN) oradadır. Cast
/// vermeyen ama ADB'si (5555) açık TV kimliksiz "Android TV" olarak listelenir.
/// </summary>
public static class AndroidTvDiscovery
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };

    /// <summary>Cihaz tanımından TV kaydı; kimlik (UDN) yoksa null.</summary>
    public static LgTv? FromDescription(string host, string? xml)
    {
        if (TvIdentity.UuidFromDescription(xml) is not { } uuid) return null;
        var start = xml!.IndexOf("<friendlyName>", StringComparison.Ordinal);
        var end = start < 0 ? -1 : xml.IndexOf("</friendlyName>", start, StringComparison.Ordinal);
        var name = end < 0 ? null : xml[(start + "<friendlyName>".Length)..end].Trim();
        return new LgTv(string.IsNullOrEmpty(name) ? "Android TV" : name, host, TvBrand.AndroidTv, uuid);
    }

    /// <summary>Adresteki Android TV'nin kaydı (Cast cihaz tanımı okunabilirse kimlikli); okunamazsa null.</summary>
    public static async Task<LgTv?> IdentifyAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(host)) return null;
        try { return FromDescription(host, await Http.GetStringAsync($"http://{host}:8008/ssdp/device-desc.xml", cancellationToken)); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Ev ağında Android TV'ler: Cast cihaz tanımını verenler kimlikli, yalnız ADB portu açık olanlar kimliksiz.</summary>
    public static async Task<IReadOnlyList<LgTv>> SearchAsync(int adbPort = 5555, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromMilliseconds(700);
        var cast = LocalNetwork.HostsWithOpenPortAsync(8008, timeout, cancellationToken);
        var adb = LocalNetwork.HostsWithOpenPortAsync(adbPort, timeout, cancellationToken);
        var tvs = new List<LgTv>();
        foreach (var host in (await cast).Distinct())
            if (await IdentifyAsync(host, cancellationToken) is { } tv) tvs.Add(tv);
        foreach (var host in (await adb).Distinct())
            if (tvs.All(tv => tv.Host != host)) tvs.Add(new LgTv("Android TV", host, TvBrand.AndroidTv));
        return tvs.OrderBy(tv => tv.Host, StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Android TV bekçisinin yeniden bulucusu (LG ve Samsung'daki <see cref="TvLocator"/>'ın Android TV karşılığı; Mac ve
/// iPhone'daki <c>AndroidTVLocator</c> ile aynı kural). Açma TV'ye ulaşamayınca ve kayıtlı adres hiç yanıt vermiyorsa ağ
/// aranır (ilk başarısızlıkta hemen, sonra en çok <c>searchInterval</c>'da bir); aynı TV (önce kimlikle, sonra ad ve marka
/// kuralıyla) bulunursa kayıt güncellenir ve bekçi yeni adresle hemen yeniden dener. Kimliği bilinmeyen eski kayıt, TV ilk
/// kez açıldığında kimliğini öğrenir.
/// </summary>
public sealed class AndroidTvLocator(
    LgTv tv,
    Action<LgTv> onMoved,
    Func<Task<IReadOnlyList<LgTv>>>? search = null,
    Func<string, Task<LgTv?>>? identify = null,
    Func<string, Task<bool>>? responds = null,
    Action<string>? log = null,
    TimeSpan? searchInterval = null)
{
    private readonly object gate = new();
    private readonly Func<Task<IReadOnlyList<LgTv>>> search = search ?? (() => AndroidTvDiscovery.SearchAsync());
    private readonly Func<string, Task<LgTv?>> identify = identify ?? (host => AndroidTvDiscovery.IdentifyAsync(host));
    private readonly Func<string, Task<bool>> responds = responds ?? (host => TvReach.RespondsAsync(host, TvBrand.AndroidTv));
    private readonly TimeSpan interval = searchInterval ?? TimeSpan.FromSeconds(30);
    private LgTv current = tv;
    private Stopwatch? sinceSearch;
    private bool identityChecked;

    /// <summary>Şu anki adres.</summary>
    public string Host { get { lock (gate) return current.Host; } }

    /// <summary>
    /// Açma TV'ye ulaşamadı: kayıtlı adres sessizse ağ aranır; TV yeni adreste bulunursa kayıt güncellenir ve yeni adres
    /// döner. Adres yanıt veriyorsa, arama zamanı gelmediyse ya da TV bulunamadıysa null.
    /// </summary>
    public async Task<string?> RelocateAsync()
    {
        LgTv saved;
        lock (gate) saved = current;
        if (saved.Host.Length == 0 || await responds(saved.Host)) return null;
        lock (gate)
        {
            if (sinceSearch is not null && sinceSearch.Elapsed < interval) return null;
            sinceSearch = Stopwatch.StartNew();
        }
        if (TvLocator.Locate(saved, await search()) is not { } hit || hit.Tv.Host == saved.Host) return null;
        lock (gate) current = hit.Tv;
        log?.Invoke(TvIdentity.MovedLine(saved.Host, hit.Tv.Host, hit.How));
        onMoved(hit.Tv);
        return hit.Tv.Host;
    }

    /// <summary>Açma başarılı: kayıtta kimlik yoksa adresteki TV'nin kimliği bir kez okunur ve kayda yazılır.</summary>
    public async Task LearnIdentityAsync()
    {
        LgTv saved;
        lock (gate)
        {
            if (identityChecked || TvIdentity.HasIdentity(current) || current.Host.Length == 0) return;
            identityChecked = true;
            saved = current;
        }
        if (await identify(saved.Host) is not { } found || !TvIdentity.HasIdentity(found)) return;
        var learned = TvIdentity.Identified(saved, found);
        lock (gate) current = learned;
        log?.Invoke(TvIdentity.LearnedLine(learned));
        onMoved(learned);
    }
}
