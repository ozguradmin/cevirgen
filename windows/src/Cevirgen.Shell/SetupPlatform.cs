// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: ilk açılıştaki "Oyunu nerede oynuyorsun?" sorusunun cevabı. Kurulum bu seçime göre yalnız gereken adımlardan
/// geçer; Ayarlar › Cihazlar › "Platformu değiştir" soruyu yeniden sorar.
/// </summary>
public enum SetupPlatform
{
    /// <summary>Çevirgen Uzaktan Oynatma ile PS5'e ikinci PSN hesabıyla kendisi bağlanır.</summary>
    Ps5,
    /// <summary>Çevirgen Xbox'a Microsoft hesabıyla bağlanır (ya da Xbox uygulamasının penceresini okur).</summary>
    Xbox,
    /// <summary>Oyun bu bilgisayarda: oyun penceresi okunur (<see cref="FrameSource.CaptureWindow"/>).</summary>
    Pc,
    /// <summary>Yakalama kartı / OBS penceresi (Switch, eski konsollar …); yine <see cref="FrameSource.CaptureWindow"/>.</summary>
    Capture,
}

/// <summary>Platform seçeneğinin ekrandaki bilgileri (taslak: design/cevirgen/arayuz-taslagi/Setup.dc.html).</summary>
/// <param name="How">Nasıl çalıştığı (bir iki cümle).</param>
/// <param name="Needs">"Gerekenler" satırı.</param>
/// <param name="Minutes">Yaklaşık süre (dakika).</param>
public sealed record PlatformOption(SetupPlatform Id, string Name, string How, string Needs, int Minutes)
{
    /// <summary>Kurulumun kullanıcıya görünen adım sayısı (akıştan; TV girişi TV adımının parçasıdır).</summary>
    public int Steps => SetupPlatforms.Steps(Id, hdmi: false).Count;

    /// <summary>"4 adım · 3 dk".</summary>
    public string StepsText => $"{Steps} adım · {Minutes} dk";
}

/// <summary>
/// Platforma göre kurulum akışı: hangi sihirbaz adımları, hangi sırayla. Arayüzden bağımsız; denetleyici (Advance/Back)
/// ve "yalnız gereken adımlar" süzgeci (Ayarlar'dan yeniden kurulum) bunu kullanır.
/// </summary>
public static class SetupPlatforms
{
    public static readonly IReadOnlyList<PlatformOption> All =
    [
        new(SetupPlatform.Ps5, "PS5",
            "Çevirgen, Uzaktan Oynatma ile PS5'in görüntüsünü izler. Sen kendi hesabınla oynamaya devam edersin.",
            "Ücretsiz ikinci bir PSN hesabı, aynı ev ağı", 3),
        new(SetupPlatform.Xbox, "Xbox",
            "Çevirgen Xbox'a bağlanıp görüntüyü izler; ya da bilgisayardaki Xbox uygulamasının penceresini okur.",
            "Microsoft hesabı, aynı ev ağı", 2),
        new(SetupPlatform.Pc, "Bilgisayar",
            "Oyun bu bilgisayarda. Çevirgen oyun penceresini okur; Türkçe oyunun üstünde ya da TV'de çıkar.",
            "Hesap gerekmez; oyunu pencereli ya da kenarlıksız aç", 1),
        new(SetupPlatform.Capture, "Diğer",
            "Switch, eski konsollar ya da başka bir cihaz: görüntü yakalama kartıyla bilgisayara gelir, Çevirgen o pencereyi okur.",
            "Yakalama kartı ve OBS (ya da kartın kendi uygulaması)", 2),
    ];

    public static PlatformOption Option(SetupPlatform platform) => All.First(option => option.Id == platform);

    /// <summary>Görüntüyü seçilen pencereden okuyan platform mu (Bilgisayar, Diğer).</summary>
    public static bool ReadsWindow(SetupPlatform platform) => platform is SetupPlatform.Pc or SetupPlatform.Capture;

    /// <summary>
    /// Mevcut ayarlardan platform. Kayıtlı seçim (<paramref name="stored"/>) ayarlarla çelişmiyorsa o; değilse ayarlardan:
    /// Xbox (pencereden okuyorsa Diğer), seçilen pencere OBS/yakalama yazılımıysa Diğer, başka pencere ya da Remote Play
    /// penceresi Bilgisayar, yoksa PS5.
    /// </summary>
    public static SetupPlatform Infer(GameConsoleKind kind, UserSettings settings, SetupPlatform? stored = null)
    {
        if (kind == GameConsoleKind.Xbox)
            return settings.XboxFrameSource == XboxFrameSource.CaptureWindow ? stored is SetupPlatform.Pc ? SetupPlatform.Pc : SetupPlatform.Capture : SetupPlatform.Xbox;
        return settings.FrameSource switch
        {
            FrameSource.CaptureWindow => stored is SetupPlatform.Pc or SetupPlatform.Capture ? stored.Value
                : UsagePlatform.IsCaptureSource(settings.CaptureWindow) ? SetupPlatform.Capture : SetupPlatform.Pc,
            FrameSource.RemotePlayWindow => SetupPlatform.Pc,
            _ => SetupPlatform.Ps5,
        };
    }

    /// <summary>
    /// Karşılamadan sonraki sihirbaz adımları, sırayla (TV Girişi yalnız <paramref name="hdmi"/> ise). Çevirgen 1.3'te kısaldı:
    /// ikinci hesabın nasıl açılacağı giriş adımının içinde (ayrı adım değil); ücretsiz model karşılamadan sonra arka planda
    /// iner (deneme adımı ilerlemesini gösterir); daha iyi çeviri (Gemini anahtarı) kurulumda sorulmaz, Çeviri sekmesindedir.
    /// Bilgisayar platformunda TV adımı yoktur (altyazı oyunun üstünde; TV sonra Ayarlar › Cihazlar'dan eşlenir).
    /// </summary>
    public static IReadOnlyList<OnboardingStep> Steps(SetupPlatform platform, bool hdmi)
    {
        var steps = new List<OnboardingStep>();
        switch (platform)
        {
            case SetupPlatform.Ps5:
            case SetupPlatform.Xbox:
                steps.AddRange([OnboardingStep.PsnLogin, OnboardingStep.Console]);
                break;
            default:
                steps.Add(OnboardingStep.Window);
                break;
        }
        if (platform != SetupPlatform.Pc)
        {
            steps.Add(OnboardingStep.Tv);
            if (hdmi) steps.Add(OnboardingStep.Hdmi);
        }
        steps.Add(OnboardingStep.Test);
        return steps;
    }

    /// <summary>
    /// Kullanıcıya gösterilen ilerleme ("Adım 2 / 4"): TV girişi TV adımıyla bir sayılır; karşılamada 0. Listede olmayan adım
    /// (Ayarlar'dan açılan eski adımlar) kendinden önceki adımın numarasını alır.
    /// </summary>
    public static (int Number, int Count) Progress(SetupPlatform platform, OnboardingStep step)
    {
        var steps = Steps(platform, hdmi: false);
        if (step == OnboardingStep.Welcome) return (0, steps.Count);
        if (step == OnboardingStep.Hdmi) step = OnboardingStep.Tv;
        var index = steps.ToList().IndexOf(step);
        if (index < 0) index = Math.Max(0, IndexOrBefore(steps, step));
        return (index + 1, steps.Count);
    }

    /// <summary>
    /// <paramref name="current"/>'ten sonraki adım; <paramref name="needed"/> false dönen adımlar atlanır (Ayarlar'dan yeniden
    /// kurulumda yalnız eksik olanlar). Liste bitince <see cref="OnboardingStep.Done"/>. Adım listede yoksa (ör. PS5 listesinde
    /// pencere adımı) listedeki sırasına göre bir sonrakine geçilir.
    /// </summary>
    public static OnboardingStep Next(SetupPlatform platform, OnboardingStep current, bool hdmi, Func<OnboardingStep, bool>? needed = null)
    {
        var steps = Steps(platform, hdmi);
        var index = current == OnboardingStep.Welcome ? -1 : IndexOrBefore(steps, current);
        for (var i = index + 1; i < steps.Count; i++)
            if (needed?.Invoke(steps[i]) != false) return steps[i];
        return OnboardingStep.Done;
    }

    /// <summary>Bir önceki adım (atlananlar dahil değil); ilk adımdan önce karşılama.</summary>
    public static OnboardingStep Previous(SetupPlatform platform, OnboardingStep current, bool hdmi, Func<OnboardingStep, bool>? needed = null)
    {
        var steps = Steps(platform, hdmi);
        var index = steps.ToList().IndexOf(current);
        if (index < 0) index = IndexOrBefore(steps, current) + 1;
        for (var i = index - 1; i >= 0; i--)
            if (needed?.Invoke(steps[i]) != false) return steps[i];
        return OnboardingStep.Welcome;
    }

    /// <summary>Adımın listedeki yeri; yoksa sıra numarası kendisinden küçük son adımın yeri (-1: hepsinden önce).</summary>
    private static int IndexOrBefore(IReadOnlyList<OnboardingStep> steps, OnboardingStep step)
    {
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] == step) return i;
        var last = -1;
        for (var i = 0; i < steps.Count; i++)
            if (Order(steps[i]) < Order(step)) last = i;
        return last;
    }

    /// <summary>Akıştaki sıra (pencere adımı hesap adımlarının yerinde, TV'den önce).</summary>
    private static int Order(OnboardingStep step) => step == OnboardingStep.Window ? (int)OnboardingStep.Console : (int)step;
}
