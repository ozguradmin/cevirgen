// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// Uygulamanın yerel veri klasörleri. <see cref="Root"/> eski ad (%LOCALAPPDATA%\CouchSubs) bilerek korunur: günlükler
/// (altyazi.log, remoteplay.log, tvinstall.log), geçmiş, sözlük, paketler, kapaklar mevcut kurulumlarda orada.
/// <see cref="CevirgenRoot"/> (%LOCALAPPDATA%\Cevirgen): Çevirgen'in kendi dosyaları (güncelleme denemesi vb.). Testler bunu süreç başına geçici bir klasöre yönlendirir (her test derlemesinin
/// TestDataRedirect modül başlatıcısı): önceden Mac'te testlerin "paket 0.6.1", "Kayıtlı anahtar reddedildi" gibi sahte
/// satırları kullanıcının gerçek tvinstall.log'una karışıyordu; Windows'ta da aynısı olmasın.
/// </summary>
public static class AppDataPaths
{
    /// <summary>Kullanıcının gerçek klasörü.</summary>
    public static string RealRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchSubs");

    private static string? redirected;

    /// <summary>Yazılan klasör: testlerde geçici klasör, uygulamada gerçek klasör.</summary>
    public static string Root => redirected ?? RealRoot;

    /// <summary>Çevirgen'in kendi klasörü; testlerde geçici klasörün altında.</summary>
    public static string RealCevirgenRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cevirgen");

    public static string CevirgenRoot => redirected is { } root ? Path.Combine(root, "Cevirgen") : RealCevirgenRoot;

    public static bool IsRedirected => redirected is not null;

    /// <summary>Testler için: bundan sonra bütün yerel dosyalar <paramref name="root"/> altına yazılır.</summary>
    public static void RedirectForTests(string root)
    {
        Directory.CreateDirectory(root);
        redirected = root;
    }

    /// <summary>Test başına (süreç başına) yeni bir geçici klasör.</summary>
    public static string NewTestRoot() =>
        Path.Combine(Path.GetTempPath(), "CevirgenTests", $"{Environment.ProcessId}-{Guid.NewGuid():N}");
}
