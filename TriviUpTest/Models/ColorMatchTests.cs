using Xunit;
using TriviUpBackend.Game.Models;

namespace TriviUpTest.Models;

public class ColorMatchTests
{
    [Fact]
    public void Similarity_SameColor_Is100()
    {
        var color = new ColorHsb(200, 70, 80);

        Assert.Equal(100, ColorMatch.Similarity(color, color));
    }

    [Fact]
    public void Similarity_BlackVsWhite_Is0()
    {
        Assert.Equal(0, ColorMatch.Similarity(new ColorHsb(0, 0, 0), new ColorHsb(0, 0, 100)));
    }

    [Fact]
    public void Similarity_CloserColor_ScoresHigher()
    {
        var target = new ColorHsb(120, 80, 80);

        var cerca = ColorMatch.Similarity(target, new ColorHsb(125, 80, 80));
        var lejos = ColorMatch.Similarity(target, new ColorHsb(200, 80, 80));

        Assert.True(cerca > lejos);
        Assert.InRange(cerca, 90, 100);
    }

    [Fact]
    public void Similarity_HueWrapsAround()
    {
        // 359º y 1º son casi el mismo rojo
        Assert.InRange(ColorMatch.Similarity(new ColorHsb(359, 90, 90), new ColorHsb(1, 90, 90)), 95, 100);
    }

    [Fact]
    public void Similarity_IsSymmetric()
    {
        var a = new ColorHsb(40, 60, 70);
        var b = new ColorHsb(300, 30, 90);

        Assert.Equal(ColorMatch.Similarity(a, b), ColorMatch.Similarity(b, a));
    }

    [Theory]
    [InlineData(0, 100, 100, 1, 0, 0)]
    [InlineData(120, 100, 100, 0, 1, 0)]
    [InlineData(240, 100, 100, 0, 0, 1)]
    [InlineData(0, 0, 100, 1, 1, 1)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void ToRgb_PrimaryColors(int h, int s, int v, double r, double g, double b)
    {
        var rgb = ColorMatch.ToRgb(new ColorHsb(h, s, v));

        Assert.Equal(r, rgb.R, 3);
        Assert.Equal(g, rgb.G, 3);
        Assert.Equal(b, rgb.B, 3);
    }

    [Fact]
    public void RandomTarget_AlwaysValidAndNotTooDull()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var color = ColorMatch.RandomTarget(random);
            Assert.True(color.IsValid());
            Assert.InRange(color.Saturation, 35, 100);
            Assert.InRange(color.Brightness, 35, 100);
        }
    }
}
