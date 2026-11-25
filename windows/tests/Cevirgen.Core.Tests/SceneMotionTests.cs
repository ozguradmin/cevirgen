// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace Cevirgen.Core.Tests;

/// <summary>
/// Kamerayla kayan yazı (plaka, tabela, dükkân adı) altyazı sayılmaz; yerinde duran altyazı hiç çıkarılmaz (GitHub #1259).
/// Ortak deneme dosyası shared/fixtures/scene-motion.json (Mac SceneMotionTests.swift ve Android
/// SceneMotionTest.kt aynı dosyayı okur).
/// </summary>
public class SceneMotionTests
{
    private static TextBox Box(JsonElement element) =>
        new(element[0].GetDouble(), element[1].GetDouble(), element[2].GetDouble(), element[3].GetDouble());

    [Fact]
    public void SharedVectors()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(PackFixtures.Path("scene-motion.json")));
        var cases = fixture.RootElement.GetProperty("cases");
        Assert.True(cases.GetArrayLength() > 0);
        foreach (var vector in cases.EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString();
            var motion = new SceneMotion();
            var area = Box(vector.GetProperty("area"));
            foreach (var frame in vector.GetProperty("frames").EnumerateArray())
            {
                var time = frame.GetProperty("time").GetInt64();
                var lines = frame.GetProperty("lines").EnumerateArray().Select(line =>
                    new TextLine(line.GetProperty("text").GetString()!,
                                 line.TryGetProperty("box", out var box) ? Box(box) : null)).ToList();
                var expected = frame.GetProperty("kept").EnumerateArray().Select(text => text.GetString()!).ToList();
                var kept = motion.Filter(new Reading(lines), area, time);
                Assert.True(expected.SequenceEqual(kept.Lines.Select(line => line.Text)),
                            $"{name} @{time}: {string.Join(" / ", kept.Lines.Select(line => line.Text))}");
                Assert.Equal(lines.Count - expected.Count, motion.Removed.Count);
            }
        }
    }

    [Fact]
    public void ResetForgetsSeenText()
    {
        var motion = new SceneMotion();
        var area = new TextBox(0, 0, 1, 1);
        motion.Filter(new Reading([new TextLine("Charters", new TextBox(0.1, 0.5, 0.2, 0.55))]), area, 0);
        motion.Reset();
        var kept = motion.Filter(new Reading([new TextLine("Charters", new TextBox(0.4, 0.5, 0.5, 0.55))]), area, 100);
        Assert.Equal(["Charters"], kept.Lines.Select(line => line.Text));
    }
}
