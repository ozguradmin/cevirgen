// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core;

/// <summary>
/// Telefonun açılışları arasında kalan hafıza: mesajların çevirisi ve panelin yazı boyu. Önceden ikisi de telefon alanının
/// durumundaydı; telefon izleyicisi telefonu bir an kaybedince alan kalkıyor, yeniden açılışta aynı İngilizce mesajlar
/// motora yeniden gidiyor (yapay zekâ her seferinde biraz başka Türkçe veriyordu) ve yazı boyu baştan ölçülüyordu (X, 22 Kas:
/// "her saniye şekli de hareketi de değişiyor"). Çeviri durunca (<see cref="RegionWatcher.Reset"/>) unutulur. Mac/iPhone
/// (PhoneMemory.swift) ve Android (PhoneMemory.kt) aynı kural, aynı sınır.
/// </summary>
internal sealed class PhoneMemory
{
    /// <summary>En çok bu kadar mesajın çevirisi tutulur; dolunca en eskisi düşer.</summary>
    internal const int Capacity = 256;

    private readonly object gate = new();
    private readonly Dictionary<string, string> translations = new(StringComparer.Ordinal);
    private readonly Queue<string> keys = new();

    /// <summary>Yanda Panel'in kilitli telefon yeri ve yazı boyu (<see cref="PhonePanelSteady"/>).</summary>
    internal PhonePanelSteady Steady { get; } = new();

    /// <summary>Bilinen çevirilerin anlık kopyası.</summary>
    internal IReadOnlyDictionary<string, string> Known
    {
        get { lock (gate) return new Dictionary<string, string>(translations, StringComparer.Ordinal); }
    }

    /// <summary>Mesajın bilinen çevirisi: birebir aynı okunuşunki, yoksa aynı mesajın biraz farklı okunuşununki.</summary>
    internal string? Translation(string source)
    {
        lock (gate) return PhoneSteady.Translation(source, translations);
    }

    internal void Remember(string source, string translation)
    {
        lock (gate)
        {
            if (!translations.ContainsKey(source))
            {
                if (keys.Count >= Capacity) translations.Remove(keys.Dequeue());
                keys.Enqueue(source);
            }
            translations[source] = translation;
        }
    }

    /// <summary>Çeviri durdu ya da hat yeniden kuruldu.</summary>
    internal void Clear()
    {
        lock (gate)
        {
            translations.Clear();
            keys.Clear();
        }
        Steady.Forget();
    }
}
