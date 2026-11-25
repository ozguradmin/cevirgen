// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>
/// Telefonun kararlı çıktısının kuralları (X, 22 Kas): aynı mesaj bir kez, gösterilen Türkçe aynı mesajda değişmez, gruptaki
/// balonlar tek boyda, kart kısalmaz, yinelenen metin bir kez, panel yazı boyunu hatırlar ve telefonu aynı boyda izler.
/// Mac (PhoneSteadyOutputTests.swift) ve Android (PhoneSteadyOutputTest.kt) aynı denemeyi yapar. Metinler uydurmadır.
/// </summary>
public class PhoneSteadyOutputTests
{
    private const string First = "Are you coming to the barbecue tonight?";
    private const string Second = "Bring the folding chairs from the garage.";

    private static ScreenTranslation.Item Item(string source, string translation, double top = .3, double line = .02,
                                               ScreenPhoneCard.Role? role = ScreenPhoneCard.Role.Incoming) =>
        new(source, translation, new TextBox(.6, top, .8, top + .05), line)
        {
            Phone = role is { } value ? new ScreenPhoneCard.Placement(value, new TextBox(.1, top, .9, top + .05)) : null,
        };

    [Fact]
    public void SameMessageIsWrittenOnce()
    {
        var items = new[] { Item(First, "A"), Item("Are you cominq to the barbecue tonight?", "B", .4), Item(Second, "C", .5) };
        Assert.Equal(["A", "C"], PhoneSteady.WithoutRepeats(items).Select(item => item.Translation));
        var shortOnes = new[] { Item("Ok", "Tamam"), Item(Second, "C", .4), Item("Ok", "Tamam", .5) };
        Assert.Equal(3, PhoneSteady.WithoutRepeats(shortOnes).Count);
        Assert.Single(PhoneSteady.WithoutRepeats([Item("Ok", "Tamam"), Item("Ok", "Tamam", .4)]));
        var far = new[] { Item(First, "A"), Item(Second, "C", .4), Item(First, "A", .5) };
        Assert.Equal(["A", "C"], PhoneSteady.WithoutRepeats(far).Select(item => item.Translation));
        Assert.Equal(2, PhoneSteady.WithoutRepeats([Item("Ok", "x", role: null), Item("Ok", "x", role: null)]).Count);
    }

    [Fact]
    public void ShownTranslationIsKeptForTheSameMessage()
    {
        var previous = new[] { Item(First, "Bu akşam mangala geliyor musun?") };
        var fresh = new[] { Item("Are you cominq to the barbecue tonight?", "Bu gece mangala gelir misin?"),
                            Item(Second, "Katlanır sandalyeleri getir.", .5) };
        var kept = PhoneSteady.KeptTranslations(fresh, previous);
        Assert.Equal(["Bu akşam mangala geliyor musun?", "Katlanır sandalyeleri getir."], kept.Select(item => item.Translation));
        Assert.Equal(First, kept[0].Source);
        Assert.Equal(["6'da"], PhoneSteady.KeptTranslations([Item("Meet me at the station at 6", "6'da")],
            [Item("Meet me at the station at 5", "5'te")]).Select(item => item.Translation));
        // Aynı yerdeki balonun daha bozuk okunuşu ("fo1ding" atıldı) da aynı balondur.
        Assert.Equal("Sandalyeleri getir.", PhoneSteady.ShownTranslation("Bring the chairs from the garage.",
            new TextBox(.1, .5, .9, .55), [Item(Second, "Sandalyeleri getir.", .5)]));
        Assert.Null(PhoneSteady.ShownTranslation("Bring the chairs from the garage.", new TextBox(.1, .2, .9, .25),
            [Item(Second, "Sandalyeleri getir.", .5)]));
    }

    [Fact]
    public void LongMessageWithoutItsQuestionMarkIsTheSameMessage()
    {
        Assert.True(PhoneSteady.SameReading(First, "Are you coming to the barbecue tonight"));
        Assert.False(PhoneSteady.SameReading("Ok?", "Ok."));
    }

    [Fact]
    public void MessageBubblesShareTheLockedLine()
    {
        var items = new[] { Item("Rose", "Rose", line: .03, role: ScreenPhoneCard.Role.Header), Item(First, "A", line: .018),
                            Item(Second, "B", line: .023), Item("x", "x", line: .05, role: null) };
        Assert.Equal([.03, .02, .02, .05], PhoneSteady.UniformLine(items, .02).Select(item => item.LineHeight));
    }

    [Fact]
    public void CardDoesNotShrinkWhileThePhoneStays()
    {
        static ScreenTranslation.Item Card(double bottom, double left = .7) =>
            new("", ScreenPhoneCard.BackdropText, new TextBox(left, .2, left + .2, bottom), .02) { Panel = ScreenPhoneCard.PanelPart.Card };
        Assert.Equal(.8, PhoneSteady.SteadyCard([Card(.7)], [Card(.8)])[0].Box.Bottom);
        Assert.Equal(.9, PhoneSteady.SteadyCard([Card(.9)], [Card(.8)])[0].Box.Bottom);
        Assert.Equal(.7, PhoneSteady.SteadyCard([Card(.7, .6)], [Card(.8)])[0].Box.Bottom);
    }

    [Fact]
    public void RepeatedTextIsCollapsed()
    {
        Assert.Equal("Grab some ice on the way.", GamePhoneText.WithoutRepeat("Grab some ice on the way. Grab some ice on the way."));
        Assert.Equal("Yolda buz al.", GamePhoneText.WithoutRepeat("Yolda buz al. Yolda buz al."));
        Assert.Equal("no no", GamePhoneText.WithoutRepeat("no no"));
        Assert.Equal("Call me at 5. Call me at 6.", GamePhoneText.WithoutRepeat("Call me at 5. Call me at 6."));
        var lines = new[] { new TextLine("Grab some ice on the way", new TextBox(.05, .3, .55, .34)),
                            new TextLine("Grab some ice on the way", new TextBox(.05, .34, .55, .38)),
                            new TextLine("and some plates", new TextBox(.05, .38, .55, .42)) };
        Assert.Equal(["Grab some ice on the way", "and some plates"], GamePhoneText.WithoutRepeatedLines(lines).Select(line => line.Text));
    }

    [Fact]
    public void PanelRemembersItsTextSizeAndFollowsAMoveWithTheSameSize()
    {
        var still = new GamePhoneGeometry.Pose(.5, .19, .5, .87);
        var steady = new PhonePanelSteady();
        steady.SteadyPose(still, 0);
        Assert.Equal(.02, steady.SteadyLine(.02, 0));
        steady.Reset();
        var back = steady.SteadyPose(new GamePhoneGeometry.Pose(.505, .2, .505, .885), 450);
        Assert.False(back.Resized);
        Assert.Equal(.02, steady.SteadyLine(.024, 450));
        var moved = new GamePhoneGeometry.Pose(.30, .21, .31, .91);
        steady.SteadyPose(moved, 900);
        var placed = steady.SteadyPose(moved, 1950);
        Assert.False(placed.Resized);
        Assert.Equal(.885 - .2, placed.Pose.BottomY - placed.Pose.TopY, 9);
        Assert.Equal((moved.Bounds.Left + moved.Bounds.Right) / 2, (placed.Pose.Bounds.Left + placed.Pose.Bounds.Right) / 2, 9);
        steady.Forget();
        Assert.True(steady.SteadyPose(still, 2400).Resized);
    }

    [Fact]
    public void MemoryKeepsTranslationsAndForgetsTheOldest()
    {
        var memory = new PhoneMemory();
        memory.Remember(First, "Bu akşam mangala geliyor musun?");
        Assert.Equal("Bu akşam mangala geliyor musun?", memory.Translation("Are you cominq to the barbecue tonight?"));
        for (var i = 0; i < PhoneMemory.Capacity; i++) memory.Remember($"Message number {i} about the party", $"{i}");
        Assert.False(memory.Known.ContainsKey(First));
        Assert.Equal(PhoneMemory.Capacity, memory.Known.Count);
        memory.Clear();
        Assert.Empty(memory.Known);
    }
}
