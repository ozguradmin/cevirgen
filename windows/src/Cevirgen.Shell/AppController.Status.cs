// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;
using Cevirgen.RemotePlay;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: cihazların ve çeviri motorunun tek renk modeli (<see cref="DeviceStates"/>). Arayüz durum noktasının rengini
/// yalnız buradan alır; değişince <see cref="PropertyChanged"/> zaten gelen özelliklerle (IsRunning, SessionState, TvClients,
/// Settings …) yeniden okunur.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Görüntü kaynağı kurulu mu (PS5 eşleşmiş ve hesap var, Xbox seçili, pencere seçili; altyazının gideceği yerden bağımsız).</summary>
    public bool SourceConfigured => ReadsCaptureWindow ? settings.CaptureWindow is { ProcessName.Length: > 0, Title.Length: > 0 }
        : IsXbox ? xboxToken is not null && xboxConsole is not null
        : settings.FrameSource == FrameSource.RemotePlayWindow || console is not null && accountId is not null;

    public DeviceState SourceState => DeviceStates.Source(demoMode, SourceConfigured, isRunning, sessionState.Phase,
        retryStopped: offersReconnect || sessionState.Reason is { Kind: DisconnectKind.Refused });

    public DeviceState TvState =>
        // Eşli TV'nin adresi sessiz ve TV yeniden aranıyor (upstream 206821be): bekleniyor.
        ShowsOnTv && HasTv && tvNotResponding && tvClients == 0 ? DeviceState.Waiting
        : DeviceStates.Tv(ShowsOnTv, HasTv, isRunning, tvClients, tvAppOutdated is not null);

    public DeviceState EngineState
    {
        get
        {
            var engine = settings.Engine;
            var failed = engine == TranslationEngine.Free && TranslatorStatus == TranslatorStatus.EngineFailed
                         || isRunning && OnlineIssue is not null;
            return DeviceStates.Engine(failed, engine == TranslationEngine.Free && !ModelInstalled, engine == TranslationEngine.Free || IsReady(engine));
        }
    }

    /// <summary>Çeviri sekmesindeki motor listesinde bir motorun durumu.</summary>
    public DeviceState EngineChoiceState(TranslationEngine engine) => DeviceStates.EngineChoice(
        engine == TranslationEngine.Free || IsReady(engine), engine == TranslationEngine.Free && !ModelInstalled);
}
