// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;

namespace Cevirgen.Core.LgTv;

/// <summary>Kayıtlı TV'ye ulaşılamazsa ağda yeniden arar: modem TV'ye yeni adres verdiğinde eşleşme bozulmaz.</summary>
/// <param name="onMoved">TV yeni adreste bulundu ya da eski kayda kimliği (uuid/mac) yazıldı; kayıt güncellenmelidir.</param>
public sealed class TvLocator(
    LgTv tv,
    Func<LgTv, Task<ITvControl>> connect,
    Func<Task<IReadOnlyList<LgTv>>> search,
    Action<LgTv> onMoved,
    TimeSpan? searchInterval = null,
    Action<string>? log = null)
{
    private readonly TimeSpan interval = searchInterval ?? TimeSpan.FromSeconds(30);
    private LgTv current = tv;
    private Stopwatch? sinceSearch;

    /// <summary>Şu an kullanılan kayıt (yeni adreste bulununca değişir).</summary>
    public LgTv Current => current;

    public async Task<ITvControl> ConnectAsync()
    {
        try
        {
            return await connect(current);
        }
        catch (Exception) when (SearchDue())
        {
            // TV kapalıyken de buraya gelinir; ağ her denemede taranmaz.
            var found = await search();
            if (Learned(current, found) is { } learned)
            {
                current = learned;
                log?.Invoke(TvIdentity.LearnedLine(learned));
                onMoved(learned);
            }
            if (Locate(current, found) is not { } hit || hit.Tv.Host == current.Host) throw;
            var control = await connect(hit.Tv);
            log?.Invoke(TvIdentity.MovedLine(current.Host, hit.Tv.Host, hit.How));
            current = hit.Tv;
            onMoved(hit.Tv);
            return control;
        }
    }

    private bool SearchDue()
    {
        if (sinceSearch is not null && sinceSearch.Elapsed < interval) return false;
        sinceSearch = Stopwatch.StartNew();
        return true;
    }

    /// <summary>Bulunanlar içinde kayıtlı TV'nin yeni adresi; TV kayıtlı adresteyse ya da emin olunamazsa null.</summary>
    public static LgTv? Moved(LgTv saved, IReadOnlyList<LgTv> all) =>
        Locate(saved, all) is { } hit && hit.Tv.Host != saved.Host ? hit.Tv : null;

    /// <summary>
    /// Bulunanlar içinde kayıtlı TV (kayıtlı adreste de olabilir) ve nasıl tanındığı (Mac <c>TVLocator.locate</c> ile aynı).
    /// Kimlik önce gelir: kayıtta uuid ya da mac varsa aynı kimlikli TV seçilir; kimliği farklı TV (adı aynı olsa da,
    /// kayıtlı adreste olsa da) asla seçilmez. Kimlikle bulunamazsa kimliği bilinmeyenler arasında eski kural: kayıtlı adres,
    /// aynı marka ve aynı ad tekse o, değilse markada tek TV varsa o. Dönen kayıtta kimlik kayıttakinden tamamlanır.
    /// </summary>
    public static (LgTv Tv, TvMatch How)? Locate(LgTv saved, IReadOnlyList<LgTv> all)
    {
        var found = all.Where(tv => tv.Brand == saved.Brand).ToList();
        if (TvIdentity.HasIdentity(saved))
        {
            var same = found.Where(tv => TvIdentity.SameDevice(saved, tv) == true).ToList();
            if ((same.FirstOrDefault(tv => tv.Host == saved.Host) ?? same.FirstOrDefault()) is { } hit)
            {
                var byUuid = TvIdentity.NormalizedUuid(saved.Uuid) is not null && TvIdentity.NormalizedUuid(hit.Uuid) is not null;
                return (TvIdentity.Identified(hit, saved), byUuid ? TvMatch.Uuid : TvMatch.Mac);
            }
        }
        var unknown = found.Where(tv => TvIdentity.SameDevice(saved, tv) is null).ToList();
        if (unknown.FirstOrDefault(tv => tv.Host == saved.Host) is { } here) return (TvIdentity.Identified(here, saved), TvMatch.Name);
        var sameName = unknown.Where(tv => tv.Name == saved.Name).ToList();
        if (sameName.Count == 1) return (TvIdentity.Identified(sameName[0], saved), TvMatch.Name);
        if (found.Count == 1 && unknown.Count == 1) return (TvIdentity.Identified(unknown[0], saved), TvMatch.OnlyOne);
        return null;
    }

    /// <summary>Kimliği bilinmeyen eski kayıt: TV kayıtlı adreste kimliğiyle bulunduysa kimlik eklenmiş kayıt; değilse null.</summary>
    public static LgTv? Learned(LgTv saved, IReadOnlyList<LgTv> all)
    {
        if (TvIdentity.HasIdentity(saved) || Locate(saved, all) is not { } hit || hit.Tv.Host != saved.Host
            || !TvIdentity.HasIdentity(hit.Tv)) return null;
        return TvIdentity.Identified(saved, hit.Tv);
    }
}
