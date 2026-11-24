// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Altyazı hattının göndericisi: ekranda menü varken (<see cref="ScreenTextDetector.InMenu"/>) altyazı satırları
/// gönderilmez, temizleme geçer. Menüde altyazı alanı da otomatik ekran yazısı olarak okunuyor; menü satırları ayrıca
/// altyazı kutusunda görünmesin. Menü yüzünden gönderilmeyen ilk satırda ve menü kalkınca altyazı günlüğüne yazılır
/// (<paramref name="log"/>): menü durumu takılı kalınca altyazının neden hiç görünmediği günlükten anlaşılsın (Mac ve
/// iPhone da menü durumunu yazar).
/// </summary>
public sealed class MenuPausedSender(ISubtitleSender inner, Func<bool> inMenu, Action<string>? log = null) : ISubtitleSender
{
    private bool paused;

    public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false)
    {
        if (Paused()) return;
        inner.SendSubtitle(text, durationMs, box, paragraphs, replaces);
    }

    public void SendWithAbove(string text, int durationMs, TextBox? box, bool replaces, IReadOnlyList<string> above)
    {
        if (Paused()) return;
        inner.SendWithAbove(text, durationMs, box, replaces, above);
    }

    public void SendAbove(IReadOnlyList<string> above)
    {
        if (!inMenu()) inner.SendAbove(above);
    }

    private bool Paused()
    {
        var menu = inMenu();
        if (menu != paused)
        {
            paused = menu;
            log?.Invoke(menu ? "ekran menüsü açık: altyazı gösterilmiyor" : "ekran menüsü kapandı: altyazı gösteriliyor");
        }
        return menu;
    }

    public void Clear() => inner.Clear();
}
