// SPDX-License-Identifier: AGPL-3.0-only

namespace Cevirgen.Core.Tests;

/// <summary>Düşen satır günlüğü: biçim, tekrar ve dakikalık sınır (Mac DropLogTests.swift, Android DropLogTest.kt aynı).</summary>
public class DropLogTests
{
    [Fact]
    public void FormatsComponentReasonAndShortExcerpt()
    {
        long time = 0;
        var drops = new DropLog(() => time, _ => { });
        Assert.Equal("düşen satır [MissionLine] 3 sn dolmadan kalktı: \"Go to the marked location on …\"",
            drops.Line("MissionLine", "3 sn dolmadan kalktı", "Go to the marked location on the map"));
        time += 1;
        Assert.Null(drops.Line("MissionLine", "3 sn dolmadan kalktı", "Go to the marked location on the map"));
        Assert.NotNull(drops.Line("MissionLine", "başka neden", "Go to the marked location on the map"));
        time += DropLog.RepeatMs;
        Assert.NotNull(drops.Line("MissionLine", "3 sn dolmadan kalktı", "Go to the marked location on the map"));
    }

    [Fact]
    public void CapsLinesPerMinuteAndReportsSuppressed()
    {
        long time = 0;
        var lines = new List<string>();
        var drops = new DropLog(() => time, lines.Add);
        for (var index = 0; index < DropLog.PerMinute + 5; index++) drops.Note("SettledText", "neden", $"satır {index}");
        Assert.Equal(DropLog.PerMinute, lines.Count);
        time = 60_000;
        drops.Note("SettledText", "neden", "yeni satır");
        Assert.Equal("düşen satır [SettledText] neden: \"yeni satır\" (+5 satır sınır yüzünden yazılmadı)", lines[^1]);
    }
}
