// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Cevirgen.Core.LgTv;

/// <summary>Kayıtlı TV'nin taramada nasıl tanındığı (günlükte parantez içinde yazılır).</summary>
public enum TvMatch
{
    Uuid,
    Mac,
    Name,
    OnlyOne,
}

/// <summary>
/// TV'nin kalıcı kimliği: SSDP yanıtındaki USN, cihaz tanımındaki UDN, Samsung'un 8001 yanıtındaki <c>id</c>/<c>wifiMac</c>,
/// Android TV'de Google Cast cihaz tanımının UDN'si (Mac/iPhone <c>TVIdentity</c>, Android <c>TvIdentity</c> ile aynı kural).
/// Adres değişir, ad değiştirilebilir ve aynı evde aynı adlı iki TV olabilir; kimlik değişmez.
/// </summary>
public static class TvIdentity
{
    /// <summary>"uuid:ABC-123::urn:..." → "abc-123". Boşsa null.</summary>
    public static string? NormalizedUuid(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var cut = text.IndexOf("::", StringComparison.Ordinal);
        if (cut >= 0) text = text[..cut];
        if (text.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)) text = text[5..];
        text = text.Trim().ToLowerInvariant();
        return text.Length == 0 ? null : text;
    }

    /// <summary>"AA-BB-CC-DD-EE-FF" / "aabbccddeeff" → "aa:bb:cc:dd:ee:ff". 12 onaltılık hane değilse ya da sıfırsa null.</summary>
    public static string? NormalizedMac(string? raw)
    {
        if (raw is null) return null;
        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        var separators = raw.Where(c => !Uri.IsHexDigit(c) && !char.IsWhiteSpace(c));
        if (hex.Length != 12 || separators.Any(c => c is not (':' or '-' or '.')) || hex is "000000000000" or "ffffffffffff") return null;
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>SSDP yanıtındaki USN başlığından kimlik; yoksa null.</summary>
    public static string? UuidFromSsdpResponse(string response)
    {
        foreach (var line in response.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon < 0 || !line[..colon].Trim().Equals("USN", StringComparison.OrdinalIgnoreCase)) continue;
            return NormalizedUuid(line[(colon + 1)..]);
        }
        return null;
    }

    /// <summary>UPnP cihaz tanımındaki ilk <c>&lt;UDN&gt;</c> (kök cihaz); yoksa null.</summary>
    public static string? UuidFromDescription(string? xml)
    {
        if (xml is null) return null;
        var start = xml.IndexOf("<UDN>", StringComparison.OrdinalIgnoreCase);
        var end = start < 0 ? -1 : xml.IndexOf("</UDN>", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : NormalizedUuid(xml[(start + 5)..end]);
    }

    /// <summary>Kaydın kimliği (uuid ya da mac) var mı.</summary>
    public static bool HasIdentity(LgTv tv) => NormalizedUuid(tv.Uuid) is not null || NormalizedMac(tv.Mac) is not null;

    /// <summary>
    /// İki kayıt aynı cihaz mı: ikisinde de uuid varsa uuid, yoksa ikisinde de mac varsa mac karşılaştırılır. Kıyaslanacak
    /// kimlik yoksa null (bilinmiyor; eski ad ve marka kuralına düşülür).
    /// </summary>
    public static bool? SameDevice(LgTv a, LgTv b)
    {
        if (NormalizedUuid(a.Uuid) is { } x && NormalizedUuid(b.Uuid) is { } y) return x == y;
        if (NormalizedMac(a.Mac) is { } m && NormalizedMac(b.Mac) is { } n) return m == n;
        return null;
    }

    /// <summary>Bilinmeyen kimlik alanları <paramref name="other"/>'dan tamamlanır (ad, adres ve marka bu kaydınki kalır).</summary>
    public static LgTv Identified(LgTv tv, LgTv other) =>
        tv with { Uuid = NormalizedUuid(tv.Uuid) ?? NormalizedUuid(other.Uuid), Mac = NormalizedMac(tv.Mac) ?? NormalizedMac(other.Mac) };

    /// <summary>Yeniden bulmanın günlük satırı: "TV yeni adreste bulundu: eski → yeni (UUID ile)".</summary>
    public static string MovedLine(string oldHost, string newHost, TvMatch how) =>
        $"TV yeni adreste bulundu: {oldHost} → {newHost} ({Describe(how)})";

    /// <summary>Eski kayda kimlik yazıldı.</summary>
    public static string LearnedLine(LgTv tv)
    {
        var id = NormalizedUuid(tv.Uuid) is { } uuid ? $"UUID {uuid}" : NormalizedMac(tv.Mac) is { } mac ? $"MAC {mac}" : "-";
        return $"TV kimliği kayda yazıldı: {tv.Host} ({id})";
    }

    public static string Describe(TvMatch how) => how switch
    {
        TvMatch.Uuid => "UUID ile",
        TvMatch.Mac => "MAC ile",
        TvMatch.Name => "ad ile",
        _ => "ağdaki tek TV",
    };
}

/// <summary>
/// Bu bilgisayarın ev ağı adresi değişti mi (Mac/iPhone <c>SenderAddressWatcher</c>, Android <c>SenderAddressWatcher</c> ile
/// aynı kural). Modem yeniden başlayınca ya da bilgisayar başka ağa geçince yeni adres alır; TV uygulaması eski adrese
/// bağlanmaya çalışıp ağı taramaya düşüyordu. Ağ değişince (<see cref="NetworkChange.NetworkAddressChanged"/>) adresler
/// kısa bir beklemeden sonra yeniden okunur; ev ağı adresi değiştiyse <c>onChange</c> çağrılır ve uygulama TV'ye yeni adresi
/// hemen bildirir.
/// </summary>
public sealed class SenderAddressWatcher : IDisposable
{
    private readonly Func<IReadOnlyList<string>> read;
    private readonly Action<IReadOnlyList<string>, IReadOnlyList<string>> onChange;
    private readonly TimeSpan settle;
    private readonly object gate = new();
    private IReadOnlyList<string> last;
    private int generation;
    private bool started;

    public SenderAddressWatcher(Action<IReadOnlyList<string>, IReadOnlyList<string>> onChange,
                                Func<IReadOnlyList<string>>? read = null, TimeSpan? settle = null)
    {
        this.read = read ?? (() => LocalNetwork.PrivateAddresses().Select(address => address.ToString()).ToList());
        this.onChange = onChange;
        this.settle = settle ?? TimeSpan.FromSeconds(2);
        last = this.read();
    }

    public void Start()
    {
        if (started) return;
        started = true;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    public void Dispose()
    {
        if (!started) return;
        started = false;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        int current;
        lock (gate) current = ++generation;
        _ = Task.Delay(settle).ContinueWith(_ =>
        {
            lock (gate) if (generation != current) return;
            Check();
        }, TaskScheduler.Default);
    }

    /// <summary>Adresleri yeniden okur; değiştiyse bildirir. Testler doğrudan çağırır.</summary>
    public void Check()
    {
        IReadOnlyList<string> old, now;
        lock (gate)
        {
            now = read();
            old = last;
            last = now;
        }
        if (Changed(old, now))
        {
            try { onChange(old, now); }
            catch (Exception error) { Debug.WriteLine(error); }
        }
    }

    /// <summary>Bildirilecek değişiklik mi: yeni liste boş değil ve eskisinden farklı (sıra önemsiz).</summary>
    public static bool Changed(IReadOnlyList<string> old, IReadOnlyList<string> now) =>
        now.Count > 0 && !new HashSet<string>(old).SetEquals(now);

    public static string Line(IReadOnlyList<string> old, IReadOnlyList<string> now) =>
        $"Bu cihazın adresi değişti: {(old.Count == 0 ? "-" : string.Join(", ", old))} → {string.Join(", ", now)}; TV'ye yeni adres bildiriliyor.";
}
