using Xunit;
using TriviUpBackend.Game.Models;

namespace TriviUpTest.Models;

public class OcarinaMelodyTests
{
    [Fact]
    public void Random_HasSixValidNotes_EndsLong_AndNeverThreeEqualInARow()
    {
        var random = new Random(11);
        for (var i = 0; i < 500; i++)
        {
            var melody = OcarinaMelody.Random(random);

            Assert.Equal(OcarinaMelody.Length, melody.Count);
            Assert.All(melody, n => Assert.InRange(n.Pitch, 0, OcarinaMelody.PitchCount - 1));
            Assert.Equal(NoteFigure.Blanca, melody[^1].Figure);
            for (var j = 2; j < melody.Count; j++)
            {
                Assert.False(melody[j].Pitch == melody[j - 1].Pitch && melody[j].Pitch == melody[j - 2].Pitch);
            }
        }
    }

    [Fact]
    public void Random_VariesBetweenCalls()
    {
        var random = new Random(3);
        var melodies = Enumerable.Range(0, 20)
            .Select(_ => string.Join(",", OcarinaMelody.Random(random).Select(n => n.Pitch)))
            .ToHashSet();

        Assert.True(melodies.Count > 15);
    }

    [Fact]
    public void Matches_OnlyComparesPitchesInOrder()
    {
        var melody = new List<OcarinaNote>
        {
            new(0, NoteFigure.Negra), new(2, NoteFigure.Corchea), new(4, NoteFigure.Blanca)
        };

        Assert.True(OcarinaMelody.Matches(melody, [0, 2, 4]));
        Assert.False(OcarinaMelody.Matches(melody, [0, 4, 2]));
        Assert.False(OcarinaMelody.Matches(melody, [0, 2]));
        Assert.False(OcarinaMelody.Matches(melody, [0, 2, 4, 1]));
    }

    [Fact]
    public void PlaybackMs_IsLeadInPlusFigureDurations()
    {
        var melody = new List<OcarinaNote>
        {
            new(0, NoteFigure.Semicorchea), new(1, NoteFigure.Corchea), new(2, NoteFigure.Negra), new(3, NoteFigure.Blanca)
        };

        var q = OcarinaMelody.QuarterMs;
        Assert.Equal(OcarinaMelody.LeadInMs + q / 4 + q / 2 + q + q * 2, OcarinaMelody.PlaybackMs(melody));
    }
}
