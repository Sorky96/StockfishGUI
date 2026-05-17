using MoveMentorChess.Opening;
using Xunit;

namespace MoveMentorChessServices.Tests.Opening;

public sealed class EcoConsistencyServiceTests
{
    [Fact]
    public void IsConsistentWithMoves_AcceptsAlapinMoveOrderForB22()
    {
        bool consistent = EcoConsistencyService.IsConsistentWithMoves("B22", ["e4", "c5", "c3"]);

        Assert.True(consistent);
    }

    [Fact]
    public void IsConsistentWithMoves_RejectsOpenSicilianMoveOrderForB22()
    {
        bool consistent = EcoConsistencyService.IsConsistentWithMoves("B22", ["e4", "c5", "Nf3"]);

        Assert.False(consistent);
    }

    [Fact]
    public void IsConsistentWithMoves_RejectsE4E5MoveOrderForBFamily()
    {
        bool consistent = EcoConsistencyService.IsConsistentWithMoves("B20", ["e4", "e5"]);

        Assert.False(consistent);
    }
}
