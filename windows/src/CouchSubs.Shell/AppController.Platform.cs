// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.RemotePlay;

namespace CouchSubs.Shell;

/// <summary>
/// Çevirgen: platform seçimi ("Oyunu nerede oynuyorsun?") ve Ayarlar'dan yeniden kurulum. Karşılama adımında seçilen
/// platform (<see cref="PendingPlatform"/>) Devam'a basılınca uygulanır; sihirbaz o platformun adımlarından geçer
/// (<see cref="SetupPlatforms"/>). Ayarlar'dan açılan kurulum (<see cref="BeginPlatformChange"/>, <see cref="RunSetupStep"/>)
/// diğer ayarları korur ve yalnız eksik adımları gösterir.
/// </summary>
public sealed partial class AppController
{
    private SetupPlatform? pendingPlatform;
    /// <summary>Kurulum Ayarlar'dan açıldı: yalnız eksik adımlar (bkz. <see cref="StepNeeded"/>), sonunda ana ekran.</summary>
    private bool partialSetup;

    /// <summary>Şu anki platform (kayıtlı seçim ve ayarlardan; bkz. <see cref="SetupPlatforms.Infer"/>).</summary>
    public SetupPlatform Platform => SetupPlatforms.Infer(consoleKind, settings, store.Load<SetupPlatform?>(StoreKeys.SetupPlatform));

    /// <summary>Karşılama adımında seçili platform (Devam'a basılınca uygulanır); seçilmediyse şu anki platform.</summary>
    public SetupPlatform PendingPlatform => pendingPlatform ?? Platform;

    /// <summary>Sihirbaz Ayarlar'dan açıldı ("Platformu değiştir", "Hesabı değiştir" …): "Vazgeç" ana ekrana döner.</summary>
    public bool PartialSetup => partialSetup;

    /// <summary>Karşılamada platform seçildi (henüz uygulanmaz; Devam uygular). Xbox yardımcısı yoksa Xbox seçilemez.</summary>
    public void ChoosePlatform(SetupPlatform platform)
    {
        if (platform == SetupPlatform.Xbox && services.Xbox is null) return;
        if (pendingPlatform == platform) return;
        pendingPlatform = platform;
        Changed(nameof(PendingPlatform), nameof(CanAdvance));
    }

    /// <summary>
    /// Ayarlar › Cihazlar › "Platformu değiştir": karşılama (platform sorusu) yeniden açılır; seçimden sonra yalnız eksik
    /// adımlar gelir, öteki ayarlar (TV, görünüm, çeviri) korunur. Çeviri sürüyorsa platform uygulanırken yeniden başlar.
    /// </summary>
    public void BeginPlatformChange()
    {
        if (demoMode) return;
        partialSetup = true;
        pendingPlatform = Platform;
        Changed(nameof(PartialSetup), nameof(PendingPlatform));
        Step = OnboardingStep.Welcome;
    }

    /// <summary>
    /// Ayarlar'dan tek bir kurulum adımını açar ("Hesabı değiştir" → PSN girişi, "Başka PS5 ekle" → PS5 eşleştirme, "TV'yi
    /// değiştir" → TV). Adımdan sonra yalnız eksik adımlar gelir, sonra ana ekran.
    /// </summary>
    public void RunSetupStep(OnboardingStep start)
    {
        if (demoMode || start is OnboardingStep.Welcome or OnboardingStep.Done) return;
        partialSetup = true;
        Changed(nameof(PartialSetup));
        Step = start;
    }

    /// <summary>Ayarlar'dan açılan kurulumda "Vazgeç": ana ekrana dönülür (o ana kadar kaydedilenler kalır).</summary>
    public void CancelPartialSetup()
    {
        if (!partialSetup) return;
        partialSetup = false;
        pendingPlatform = null;
        Changed(nameof(PartialSetup), nameof(PendingPlatform));
        Step = OnboardingStep.Done;
    }

    /// <summary>
    /// Ayarlar › Cihazlar › "Eşleşmeyi kaldır": PS5 eşleşmesi silinir (PSN hesabı kalır). PS5'ten okunurken çeviri durur.
    /// Yeni PS5 "Başka PS5 ekle" ile eşleştirilir.
    /// </summary>
    public void ForgetConsole()
    {
        if (console is null) return;
        if (isRunning && !demoMode && !IsXbox && settings.FrameSource == FrameSource.Ps5) Stop();
        store.Remove(StoreKeys.Console);
        console = null;
        RemotePlayLog.Note("PS5 eşleşmesi kaldırıldı");
        Changed(nameof(Console), nameof(CanStart), nameof(CanAdvance), nameof(Ps5StatusText), nameof(NeedsPs5Link));
    }

    /// <summary>Yeniden kurulumda adım gerekli mi: yalnız eksik olan (hesap, eşleşme, TV, model) gösterilir.</summary>
    private bool StepNeeded(OnboardingStep step) => step switch
    {
        OnboardingStep.SecondAccount => accountId is null,
        OnboardingStep.PsnLogin => IsXbox ? xboxToken is null : accountId is null,
        OnboardingStep.Console => IsXbox ? xboxConsole is null : console is null,
        // Pencere platformuna geçilince okunacak pencere her zaman sorulur (kayıtlı pencere başka oyuna ait olabilir).
        OnboardingStep.Window => true,
        OnboardingStep.Tv => ShowsOnTv && !HasTv,
        // TV girişi yalnız bu kurulumda TV adımı geçildiyse (yeni TV) ve giriş seçilmemişse; sonra Ayarlar › Cihazlar'da.
        OnboardingStep.Hdmi => step == OnboardingStep.Tv && settings.Style.HdmiInput is null,
        OnboardingStep.Translation => !ModelInstalled,
        _ => false,
    };

    /// <summary>Karşılamada seçilen platformu uygular (konsol türü ve görüntü kaynağı); seçim kaydedilir.</summary>
    private void ApplyPendingPlatform()
    {
        if (pendingPlatform is not { } chosen) return;
        pendingPlatform = null;
        ApplyPlatform(chosen);
    }

    /// <summary>
    /// Platformu uygular: PS5 → PlayStation, görüntü PS5'ten; Xbox → Xbox, Xbox'a bağlanılır; Bilgisayar / Diğer → görüntü
    /// seçilen pencereden (TV yoksa altyazı bu bilgisayarda). Konsol türü kilitlenir (eski karşılamadaki gibi).
    /// </summary>
    internal void ApplyPlatform(SetupPlatform platform)
    {
        if (platform == SetupPlatform.Xbox && services.Xbox is null) platform = SetupPlatform.Ps5;
        var kind = platform == SetupPlatform.Xbox ? GameConsoleKind.Xbox : GameConsoleKind.PlayStation;
        if (kind != consoleKind)
        {
            if (step == OnboardingStep.Welcome && !consoleKindLocked) SelectConsoleKind(kind);
            else ChangeConsoleKind(kind);
        }
        var next = platform switch
        {
            SetupPlatform.Ps5 => settings with { FrameSource = FrameSource.Ps5 },
            SetupPlatform.Xbox => settings with { XboxFrameSource = XboxFrameSource.Xbox },
            _ => settings with { FrameSource = FrameSource.CaptureWindow, Output = HasTv ? settings.Output : SubtitleOutput.Computer },
        };
        if (next != settings) Settings = next;
        store.Save(StoreKeys.SetupPlatform, platform);
        consoleKindLocked = true;
        store.Save(StoreKeys.ConsoleKindLocked, true);
        RemotePlayLog.Note($"Platform: {platform}");
        Changed(nameof(Platform), nameof(PendingPlatform), nameof(ConsoleKindLocked), nameof(CanStart), nameof(CanAdvance));
    }
}
