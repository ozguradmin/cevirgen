// SPDX-License-Identifier: AGPL-3.0-only

using Cevirgen.Core;

namespace Cevirgen.Core.Tests;

/// <summary>"Örtsün"de yazı boyu sabit: hiç küçülmez, büyümez, zıplamaz (Mac: OverlayCoverTests).</summary>
public class CoverFontScaleTests
{
    private const string Short = "Çaldın mı?";
    private static readonly string Long = string.Concat(Enumerable.Repeat("Annenin arabası demiştin hani, unuttun mu yoksa? ", 4));

    [Fact]
    public void Font_stays_at_the_chosen_size()
    {
        var font = new CoverFontScale();
        Assert.Equal(1, font.Scale(Short, 46));
        Assert.Equal(1, font.Scale("Bu cümle iki satıra bölünecek kadar uzun ama üç satıra rahat sığıyor.", 46));
        // 3 satıra sığmayan satır da, sonraki satırlar da seçilen boyda (X, beta.89).
        Assert.Equal(1, font.Scale(Long, 46));
        Assert.Equal(1, font.Scale(Short, 46));
        Assert.Equal(1, font.Scale(Long + Long, 46));
        Assert.Equal(1, font.Scale(Short, 52));
    }

    [Fact]
    public void Wrap_at_a_fixed_scale_never_shrinks()
    {
        var wrapped = SubtitleWrap.Fit(string.Concat(Enumerable.Repeat(Long, 2)), 1);
        Assert.Equal(1, wrapped.Scale);
        Assert.Equal(3, wrapped.Text.Split('\n').Length);
        Assert.Equal(new SubtitleWrap.Result(Short, 1), SubtitleWrap.Fit(Short, 1));
    }
}
