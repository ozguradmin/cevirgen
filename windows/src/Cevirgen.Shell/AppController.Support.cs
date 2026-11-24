// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Canlı ekrandaki sakin destek alanı: tek satır yazı ve çerçeveli bir düğme (sarı değil; sarı düğme başlat/durdur'undur).
/// Kapatılamaz ama yalnız çeviri en az <see cref="LinesBeforeShowing"/> satır çevirdikten sonra (kalıcı) ve hata bildirimi
/// yokken görünür. Metin sunucudan (<c>support</c>) değiştirilebilir; yoksa ya da geçersizse <see cref="Default"/>. Yıldız
/// karşılığında hiçbir şey önerilmez (GitHub teşvikli yıldızı yasaklar).
/// </summary>
public sealed record SupportSlot(string Id, string Text, string Button, Uri Link, string? IconFile)
{
    /// <summary>Bu kadar satır çevrilince (deneme modu sayılmaz) alan açılır ve sonraki açılışlarda da görünür.</summary>
    public const int LinesBeforeShowing = 20;

    public static readonly SupportSlot Default = new("github-star",
        "Çevirgen işine yaradıysa GitHub'da yıldız vererek destek olabilirsin.", "Yıldız ver", UpdateChannel.RepositoryPage, null);

    /// <summary>Sunucudaki alan (geçerliyse ayrıştırmada denetlendi), yoksa uygulamanın kendi metni.</summary>
    public static SupportSlot From(ResolvedSupport? remote) => remote is null ? Default
        : new SupportSlot(remote.Support.Id, remote.Support.Text, remote.Support.Button, remote.Support.Link, remote.IconFile);

    /// <summary>Görünür mü: yeterince satır çevrildi ve şeritte hata (çeviriyi durduran) bildirimi yok.</summary>
    public static bool Visible(int translatedLines, bool blockingNotice) => translatedLines >= LinesBeforeShowing && !blockingNotice;
}

public sealed partial class AppController
{
    private int? supportLines;

    /// <summary>Destek alanının metni, düğmesi, bağlantısı ve simgesi (sunucudan ya da varsayılan). Değişince bildirilir.</summary>
    public SupportSlot Support => SupportSlot.From(remoteContent.Support);

    /// <summary>Şimdiye kadar çevrilen satır (en çok <see cref="SupportSlot.LinesBeforeShowing"/>'e kadar sayılır, kalıcı).</summary>
    public int SupportLines => supportLines ??= Math.Max(0, store.Load<int?>(StoreKeys.SupportLines) ?? 0);

    /// <summary>Destek alanı açıldı (yeterince satır çevrildi). Hata bildirimi varken arayüz yine gizler.</summary>
    public bool SupportUnlocked => SupportLines >= SupportSlot.LinesBeforeShowing;

    /// <summary>Düğme: bağlantı tarayıcıda açılır.</summary>
    public void OpenSupport() => OpenInBrowser(Support.Link);

    /// <summary>Gerçek bir çeviri satırı gösterildi (deneme modu değil): destek alanının sayacı ilerler.</summary>
    private void CountSupportLine()
    {
        if (demoMode || SupportUnlocked) return;
        supportLines = SupportLines + 1;
        store.Save<int?>(StoreKeys.SupportLines, supportLines);
        if (SupportUnlocked) Changed(nameof(SupportUnlocked));
    }
}
