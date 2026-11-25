// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Shell;

/// <summary>
/// Altyazı hattının göndericisi: ekranda menü varken (<see cref="ScreenTextDetector.InMenu"/>) altyazı satırları hemen
/// gönderilmez, bekletilir; temizleme geçer. Menüde altyazı alanı da otomatik ekran yazısı olarak okunuyor; menü satırları
/// ayrıca altyazı kutusunda görünmesin. Menü kapanınca (<see cref="Poll"/>) bekletilen satır sessizce düşmez (#1240):
/// menü <see cref="ShortMenuMs"/>'den kısa sürdüyse bekletilen bütün satırlar, yoksa son <see cref="HoldMs"/> içinde gelen
/// en son konuşma satırı (<see cref="SilenceWatch.IsSpeech"/>; menü etiketi değil) gönderilir. Eskiden menü sırasında
/// gelen satır hiç gönderilmiyordu: GTA 6'nın karanlık balık tutma sahnesinde tuş ipucu ve görev yazısı birlikte belirip
/// menü sanılınca her seferinde bir iki konuşma satırı kayboluyordu. Geçişler ve bekletilen/gönderilen satırlar altyazı
/// günlüğüne yazılır (<paramref name="log"/>, <see cref="DropLog"/>). Mac/iPhone (MenuHeldSender.swift) ve Android
/// (MenuHeldSender.kt) aynı kurallar. Çevirgen: üstte yığılı eski replikler (SendWithAbove/SendAbove) de aynı biçimde
/// bekletilir; menü kapanınca yalnız bekletilen satır gider. Upstream DropLog yerine altyazı günlüğü (<paramref name="log"/>).
/// </summary>
public sealed class MenuPausedSender(ISubtitleSender inner, Func<bool> inMenu, Action<string>? log = null, Func<long>? clock = null) : ISubtitleSender
{
    /// <summary>Menü kapanınca bundan eski bekletilen satır gönderilmez (ekrandan çoktan kalkmıştır).</summary>
    public const long HoldMs = 5000;
    /// <summary>Bundan kısa süren menü durumu yanlış algıdır: bekletilen bütün satırlar hemen gönderilir.</summary>
    public const long ShortMenuMs = 300;

    private sealed record Held(string Text, int DurationMs, TextBox? Box, bool Paragraphs, bool Replaces, long At,
                               IReadOnlyList<string>? Above = null);

    private readonly object gate = new();
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private readonly List<Held> held = [];
    private bool paused;
    private long pausedAt;

    /// <summary>
    /// Menü durumunu yeniden okur; menü kapandıysa bekletilen satırları gönderir. Okuma hattı her karede çağırır (menü
    /// kapandıktan sonra yeni satır gelmese de bekletilen satır gitsin). Şu an menü var mı döner.
    /// </summary>
    public bool Poll()
    {
        List<Held> release;
        bool menu;
        lock (gate) (menu, release) = Observe();
        Release(release);
        return menu;
    }

    public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false) =>
        Send(new Held(text, durationMs, box, paragraphs, replaces, 0));

    public void SendWithAbove(string text, int durationMs, TextBox? box, bool replaces, IReadOnlyList<string> above) =>
        Send(new Held(text, durationMs, box, false, replaces, 0, above));

    public void SendAbove(IReadOnlyList<string> above)
    {
        if (!Poll()) inner.SendAbove(above);
    }

    private void Send(Held line)
    {
        List<Held> release;
        bool menu;
        lock (gate)
        {
            (menu, release) = Observe();
            if (menu)
            {
                held.Add(line with { At = now() });
                log?.Invoke($"menü: menü sırasında bekletildi: {SubtitleLog.Excerpt(line.Text)}");
            }
        }
        Release(release);
        if (!menu) Deliver(line);
    }

    private void Release(List<Held> release)
    {
        foreach (var line in release) Deliver(line);
    }

    private void Deliver(Held line)
    {
        if (line.Above is { } above) inner.SendWithAbove(line.Text, line.DurationMs, line.Box, line.Replaces, above);
        else inner.SendSubtitle(line.Text, line.DurationMs, line.Box, line.Paragraphs, line.Replaces);
    }

    public void Clear() => inner.Clear();

    private (bool Menu, List<Held> Release) Observe()
    {
        var menu = inMenu();
        if (menu == paused) return (menu, []);
        paused = menu;
        var time = now();
        if (menu)
        {
            pausedAt = time;
            log?.Invoke("ekran menüsü açık: altyazı bekletiliyor");
            return (true, []);
        }
        var lasted = time - pausedAt;
        List<Held> release = lasted < ShortMenuMs
            ? [.. held]
            : [.. held.Where(line => time - line.At <= HoldMs && SilenceWatch.IsSpeech(line.Text)).TakeLast(1)];
        foreach (var line in held)
            log?.Invoke($"menü: {(release.Contains(line) ? "menü kapanınca gönderildi" : "menü sırasında bekletildi, gönderilmedi")}: {SubtitleLog.Excerpt(line.Text)}");
        held.Clear();
        log?.Invoke($"ekran menüsü kapandı: altyazı gösteriliyor ({lasted} ms)");
        return (false, release);
    }
}
