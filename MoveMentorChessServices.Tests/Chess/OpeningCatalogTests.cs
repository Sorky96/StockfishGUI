using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningCatalogTests
{
    [Theory]
    [InlineData("D00", "Queen's Pawn Game (D00)")]
    [InlineData("C23", "Bishop's Opening (C23)")]
    [InlineData("B01", "Scandinavian Defense (B01)")]
    [InlineData("A00", "Uncommon Opening (A00)")]
    public void OpeningCatalog_DescribesKnownCodes(string eco, string expected)
    {
        Assert.Equal(expected, OpeningCatalog.Describe(eco));
    }

    [Theory]
    [InlineData("E12", "Indian Defense (E12)")]
    [InlineData("C99", "Open Game (C99)")]
    [InlineData("D45", "Closed Game (D45)")]
    public void OpeningCatalog_FallsBackToFamilyNameForUnknownSpecificCode(string eco, string expected)
    {
        Assert.Equal(expected, OpeningCatalog.Describe(eco));
    }
}
