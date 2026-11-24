// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Çevirgen: tanılama kaydı (Ayarlar › Gelişmiş › "Tanılama kaydı"; bkz. <see cref="DiagnosticRecorder"/>). Kapalı başlar,
/// diske yazılmaz: uygulama yeniden açılınca ve 30 dakika sonra kendiliğinden kapanır. Hiçbir yere gönderilmez.
/// </summary>
public sealed partial class AppController
{
    private DiagnosticRecorder? diagnostics;

    /// <summary>Tanılama klasörü (%LOCALAPPDATA%\Cevirgen\tanilama); oturumlar bunun altında.</summary>
    public string DiagnosticsFolder => Diagnostics?.Root
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cevirgen", DiagnosticRecorder.FolderName);

    /// <summary>Açıksa bu kaydın oturum klasörü; kapalıyken null.</summary>
    public string? DiagnosticsSessionFolder => Diagnostics?.SessionFolder;

    /// <summary>Bu derlemede kayıt yazılabilir mi (testlerde ve ekran görüntüsü aracında anahtar yine görünür, yazılmaz).</summary>
    private DiagnosticRecorder? Diagnostics
    {
        get
        {
            if (diagnostics is not null || services.DataFolder is not { } folder) return diagnostics;
            return diagnostics = new DiagnosticRecorder(Path.Combine(folder, DiagnosticRecorder.FolderName), services.EncodeJpeg);
        }
    }

    private bool diagnosticsOn;

    /// <summary>"Tanılama kaydı" açık mı. Açılınca yeni oturum klasörü, kapanınca kayıt durur (yazılanlar kalır, üç gün sonra silinir).</summary>
    public bool DiagnosticsOn
    {
        get => diagnosticsOn;
        set
        {
            if (!Set(ref diagnosticsOn, value, [nameof(DiagnosticsSessionFolder)])) return;
            if (value)
            {
                Diagnostics?.Start();
                SubtitleLog.Write($"tanılama kaydı açıldı: {DiagnosticsSessionFolder ?? DiagnosticsFolder}");
            }
            else
            {
                Diagnostics?.Stop();
                SubtitleLog.Write("tanılama kaydı kapandı");
            }
            Changed(nameof(DiagnosticsSessionFolder));
        }
    }

    /// <summary>Kare iş parçacığından: sırası geldiyse kare ve o anki alanlar arka planda yazılır; 30 dakika dolduysa kapanır.</summary>
    private void RecordDiagnostics(Frame frame)
    {
        if (!diagnosticsOn || diagnostics is not { } recorder || pipeline is not { } current || demoMode) return;
        if (recorder.Expired)
        {
            post(() =>
            {
                if (!diagnosticsOn) return;
                SubtitleLog.Write("tanılama kaydı 30 dakika doldu");
                DiagnosticsOn = false;
            });
            return;
        }
        if (!recorder.Due()) return;
        IReadOnlyList<WatchedArea> extra;
        lock (regionGate) extra = watchedAreas;
        var snapshot = new DiagnosticSnapshot(DateTimeOffset.Now, settings.Region, extra, current.LastRead, LastSource, LastTranslation,
            gameSense.Current?.Name);
        _ = Task.Run(() => recorder.Record(frame, snapshot));
    }
}
