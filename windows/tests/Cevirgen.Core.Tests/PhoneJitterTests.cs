// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>
/// X (22 Kas, GTA 6 telefonu): "yazılar sürekli oynuyor okumak çok zor, balonların içinde de aynı hata var". Aynı balon küçük
/// oynamada yerinde kalır (<see cref="PhoneSteady.SteadyPositions"/>), panel el titremesiyle kaymaz
/// (<see cref="PhoneSteady.SteadyPose"/>), aynı görünen gönderi yinelenmez (<see cref="PhoneSteady.SamePicture"/>); gerçek
/// kaydırmada hepsi birlikte bir kerede geçer (Mac PhoneJitterTests.swift, Android PhoneJitterTest.kt ile aynı vektörler).
/// </summary>
public class PhoneJitterTests
{
    private static ScreenTranslation.Item Item(string text, TextBox box, double line = 0.03) =>
        new(text, "Çevrildi: " + text, box, line);

    [Fact]
    public void SmallJitterKeepsEachBubbleWhereItWasDrawn()
    {
        ScreenTranslation.Item[] before = [Item("Thx amigo! U find", new(0.62, 0.65, 0.75, 0.69)),
                                           Item("New message", new(0.64, 0.59, 0.75, 0.62))];
        ScreenTranslation.Item[] jiggled = [before[0] with { Source = "Thx amigo! U flnd", Box = new(0.623, 0.655, 0.753, 0.695) },
                                            before[1] with { Box = new(0.638, 0.586, 0.748, 0.616) }];
        var still = PhoneSteady.SteadyPositions(jiggled, before, 16.0 / 9);
        Assert.Equal(0.62, still[0].Box.Left, 9);
        Assert.Equal(0.65, still[0].Box.Top, 9);
        Assert.Equal(0.59, still[1].Box.Top, 9);
        Assert.Equal(0.03, still[1].Box.Bottom - still[1].Box.Top, 9);

        var scrolled = before.Select(item => item with { Box = item.Box with { Top = item.Box.Top - 0.02, Bottom = item.Box.Bottom - 0.02 } }).ToList();
        Assert.Equal(scrolled, PhoneSteady.SteadyPositions(scrolled, before, 16.0 / 9));

        ScreenTranslation.Item[] one = [jiggled[0] with { Box = jiggled[0].Box with { Top = 0.665, Bottom = 0.705 } }, jiggled[1]];
        var mixed = PhoneSteady.SteadyPositions(one, before, 16.0 / 9);
        Assert.Equal(0.665, mixed[0].Box.Top, 9);
        Assert.Equal(0.59, mixed[1].Box.Top, 9);

        ScreenTranslation.Item[] other = [jiggled[0], jiggled[1] with { Translation = "Başka" }];
        Assert.Equal(other[1].Box, PhoneSteady.SteadyPositions(other, before, 16.0 / 9)[1].Box);
    }

    [Fact]
    public void PanelAnchorIgnoresHandShakeAndFollowsARealMove()
    {
        var pose = new GamePhoneGeometry.Pose(0.70, 0.19, 0.70, 0.87);
        var shaken = new GamePhoneGeometry.Pose(0.704, 0.186, 0.703, 0.874);
        Assert.Equal(pose, PhoneSteady.SteadyPose(shaken, pose));
        var moved = new GamePhoneGeometry.Pose(0.66, 0.19, 0.66, 0.87);
        Assert.Equal(moved, PhoneSteady.SteadyPose(moved, pose));
        Assert.Equal(shaken, PhoneSteady.SteadyPose(shaken, null));
    }

    [Fact]
    public void PanelMessagesAreComparedByOrderAndTextNotByTheirFlowBoxes()
    {
        var card = Item("", new(0.2, 0.2, 0.5, 0.8)) with { Translation = ScreenPhoneCard.BackdropText, Panel = ScreenPhoneCard.PanelPart.Card };
        var message = Item("Hi", new(0.22, 0.3, 0.4, 0.34)) with { Panel = ScreenPhoneCard.PanelPart.Theirs };
        var nudged = message with { Box = message.Box with { Top = 0.304 } };
        Assert.True(PhoneSteady.SamePicture([card, message], [card, nudged]));
        Assert.False(PhoneSteady.SamePicture([card, message], [card, message with { Translation = "Selam" }]));
        Assert.False(PhoneSteady.SamePicture([card, message], [card with { Box = card.Box with { Left = 0.21 } }, message]));
        Assert.False(PhoneSteady.SamePicture([Item("Hi", message.Box)], [Item("Hi", nudged.Box)]));
    }

    [Fact]
    public void InkOnTheSameSideOfTheBubbleKeepsItsColor()
    {
        Assert.True(PhoneSteady.SameInk("#000000", "#584048", "#C8B0B8"));
        Assert.False(PhoneSteady.SameInk("#000000", "#FFFFFF", "#C8B0B8"));
        var before = Item("Casa De Donnie", new(0.65, 0.36, 0.77, 0.38)) with { Style = new("#C8B0B8", "#000000") };
        var grey = before with { Style = new("#C8B0B8", "#584048") };
        Assert.Equal(before.Style, Assert.Single(RegionWatcher.StablePhoneBlocks([grey], [before])).Style);
    }

    [Fact]
    public void SideOfABubbleStaysWithItsIdentity()
    {
        var spot = new TextBox(0.2, 0.2, 0.8, 0.3);
        var before = Item("Casa De Donnie", new(0.65, 0.36, 0.77, 0.38)) with { Phone = new(ScreenPhoneCard.Role.Incoming, spot) };
        var flipped = before with { Phone = new(ScreenPhoneCard.Role.Outgoing, spot) };
        Assert.Equal(ScreenPhoneCard.Role.Incoming, Assert.Single(RegionWatcher.StablePhoneBlocks([flipped], [before])).Phone?.Role);
    }

    [Fact]
    public void ShortMessageWithPunctuationNoiseKeepsItsTranslation()
    {
        var known = new Dictionary<string, string> { ["Ok."] = "Tamam.", ["Comprendo?"] = "Anladın mı?" };
        Assert.Equal("Tamam.", PhoneSteady.Translation("ok", known));
        Assert.Equal("Anladın mı?", PhoneSteady.Translation("Comprendo ?", known));
        Assert.Null(PhoneSteady.Translation("Ok?", known));
        Assert.Null(PhoneSteady.Translation("On", known));
        Assert.Equal("Yeni", PhoneSteady.Translation("New rnessage", new Dictionary<string, string> { ["New message"] = "Yeni" }));
    }

    // Yanda Panel'in yazı boyu ve yeri (#1152, GTA 6: "telefon titreyince panelin yazıları büyüyüp küçülüyor").

    private static readonly GamePhoneGeometry.Pose Still = new(0.70, 0.19, 0.70, 0.87);

    private static GamePhoneGeometry.Pose Shaken(double scale = 1, double dx = 0, double dy = 0)
    {
        double cy = (Still.TopY + Still.BottomY) / 2 + dy, half = (Still.BottomY - Still.TopY) / 2 * scale;
        return new(Still.TopX + dx, cy - half, Still.BottomX + dx, cy + half);
    }

    /// <summary>Yakın kamerada el titremesi: boy ±%8, ortası boyun ±%5'i; okunan satır yüksekliği ±%20.</summary>
    private static readonly (double Scale, double Dx, double Dy, double Line)[] Shakes =
    [
        (1.08, 0.004, -0.03, 1.2), (0.93, -0.006, 0.02, 0.82), (1.05, 0.003, 0.034, 1.15), (0.95, -0.002, -0.025, 0.85),
        (1.07, 0.005, 0.01, 1.18), (0.92, -0.004, -0.034, 0.8), (1.03, 0.006, 0.028, 1.1), (0.97, -0.006, -0.01, 0.9),
    ];

    private static List<ScreenTranslation.Item> ChatItems(double line) =>
    [
        new("Where are you man", "Neredesin adamım bu saatte, herkes seni bekliyor", new(0.75, 0.2, 0.9, 0.2 + line), line)
        {
            Phone = new ScreenPhoneCard.Placement(ScreenPhoneCard.Role.Incoming, new(0.1, 0.2, 0.8, 0.23)),
        },
        new("On my way", "Yoldayım, beş dakikaya oradayım", new(0.75, 0.4, 0.9, 0.4 + line), line)
        {
            Phone = new ScreenPhoneCard.Placement(ScreenPhoneCard.Role.Outgoing, new(0.1, 0.4, 0.8, 0.43)),
        },
    ];

    [Fact]
    public void PanelKeepsItsSizeAndPlaceWhileThePhoneShakes()
    {
        var steady = new PhonePanelSteady();
        var first = steady.SteadyPose(Still, 0);
        Assert.True(first.Resized);
        Assert.Equal(0.02, steady.SteadyLine(0.02, 0));
        var drawn = ScreenPhoneCard.Layout(ChatItems(0.02), new ScreenPhoneCard.Handset(first.Pose, 16.0 / 9), lockedLine: 0.02);
        for (int turn = 0; turn < Shakes.Length; turn++)
        {
            var shake = Shakes[turn];
            long now = (turn + 1) * 450;
            var placed = steady.SteadyPose(Shaken(shake.Scale, shake.Dx, shake.Dy), now);
            Assert.Equal(Still, placed.Pose);
            Assert.False(placed.Resized);
            var measured = ScreenPhoneCard.MeasuredLine(ChatItems(0.02 * shake.Line));
            Assert.Equal(0.02 * shake.Line, measured);
            var line = steady.SteadyLine(measured, now);
            Assert.Equal(0.02, line);
            var laid = ScreenPhoneCard.Layout(ChatItems(0.02 * shake.Line), new ScreenPhoneCard.Handset(placed.Pose, 16.0 / 9),
                                              lockedLine: line);
            Assert.Equal(drawn, laid);
        }
    }

    [Fact]
    public void PanelFollowsARealZoomAfterItLastsASecond()
    {
        var steady = new PhonePanelSteady();
        steady.SteadyPose(Still, 0);
        steady.SteadyLine(0.02, 0);
        var near = Shaken(scale: 1.4);
        Assert.Equal(Still, steady.SteadyPose(near, 450).Pose);
        Assert.Equal(Still, steady.SteadyPose(near, 900).Pose);
        Assert.Equal(Still, steady.SteadyPose(Still, 1350).Pose);
        Assert.Equal(Still, steady.SteadyPose(near, 1800).Pose);
        Assert.Equal(Still, steady.SteadyPose(near, 2250).Pose);
        var zoomed = steady.SteadyPose(near, 2800);
        Assert.Equal(near, zoomed.Pose);
        Assert.True(zoomed.Resized);
        Assert.Equal(0.028, steady.SteadyLine(0.028, 2800));
        Assert.Equal(0.028, steady.SteadyLine(0.025, 3250));
    }

    [Fact]
    public void PanelFollowsARealMoveWithoutChangingItsTextSize()
    {
        var steady = new PhonePanelSteady();
        steady.SteadyPose(Still, 0);
        steady.SteadyLine(0.02, 0);
        var moved = Shaken(dx: -0.2);
        Assert.Equal(Still, steady.SteadyPose(moved, 450).Pose);
        var placed = steady.SteadyPose(moved, 1450);
        Assert.Equal(moved, placed.Pose);
        Assert.False(placed.Resized);
        Assert.Equal(0.02, steady.SteadyLine(0.023, 1450));
        Assert.Equal(moved, steady.SteadyPose(Shaken(1.06, -0.196, 0.02), 1900).Pose);
    }

    [Fact]
    public void WrongFirstTextSizeIsCorrectedOnlyWhenTheNewSizeLasts()
    {
        var steady = new PhonePanelSteady();
        Assert.Equal(0.014, steady.SteadyLine(0.014, 0));
        Assert.Equal(0.014, steady.SteadyLine(null, 200));
        Assert.Equal(0.014, steady.SteadyLine(0.021, 450));
        Assert.Equal(0.014, steady.SteadyLine(0.015, 900));
        Assert.Equal(0.014, steady.SteadyLine(0.021, 1350));
        Assert.Equal(0.014, steady.SteadyLine(0.022, 2000));
        Assert.Equal(0.021, steady.SteadyLine(0.021, 2400));
        steady.Reset();
        Assert.Null(steady.Line);
        Assert.True(steady.SteadyPose(Shaken(dx: 0.1), 2500).Resized);
    }
}
