using Xunit;
using TriviUpBackend.Game.Services;

namespace TriviUpTest.Services;

public class FairTurnPlannerTests
{
    [Theory]
    [InlineData(10, 3, 9)]
    [InlineData(12, 4, 12)]
    [InlineData(7, 2, 6)]
    [InlineData(25, 10, 20)]
    [InlineData(2, 10, 2)]
    [InlineData(5, 1, 5)]
    [InlineData(0, 4, 0)]
    public void FairTurnQuestionCount_ReturnsLargestMultipleOfPlayers(int questions, int players, int expected)
    {
        Assert.Equal(expected, FairTurnPlanner.FairTurnQuestionCount(questions, players));
    }

    [Fact]
    public void TrimToFair_AllCombinations_EveryPlayerGetsSameTurns()
    {
        for (var players = 2; players <= 10; players++)
        {
            for (var total = players; total <= 40; total++)
            {
                var questions = Enumerable.Range(0, total).ToList();
                var trimmed = FairTurnPlanner.TrimToFair(questions, players, _ => false);

                Assert.Equal(0, trimmed.Count % players);
                Assert.Equal(questions.Take(trimmed.Count), trimmed);
            }
        }
    }

    [Fact]
    public void TrimToFair_KeepsBuzzerQuestions_AndRemovesOnlyTurnOnes()
    {
        // 0..6 por turno (7), 100 y 101 pulsador; 3 jugadores -> sobra 1 por turno
        var questions = new List<int> { 0, 1, 100, 2, 3, 4, 101, 5, 6 };

        var trimmed = FairTurnPlanner.TrimToFair(questions, 3, q => q >= 100);

        Assert.Equal(new List<int> { 0, 1, 100, 2, 3, 4, 101, 5 }, trimmed);
    }

    [Fact]
    public void TrimToFair_AlreadyFair_ReturnsSameList()
    {
        var questions = new List<int> { 1, 2, 3, 4 };

        Assert.Same(questions, FairTurnPlanner.TrimToFair(questions, 2, _ => false));
    }

    [Fact]
    public void TrimToFair_MorePlayersThanQuestions_KeepsAll()
    {
        var questions = new List<int> { 1, 2 };

        Assert.Equal(questions, FairTurnPlanner.TrimToFair(questions, 5, _ => false));
    }
}
