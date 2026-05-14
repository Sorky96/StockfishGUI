using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningUnderstandingServiceTests
{
    [Fact]
    public void BuildCards_ReturnsOneCardPerKind()
    {
        OpeningLineCatalogItem line = CreateLine();
        OpeningTrainerOverview overview = CreateOverview(line);
        OpeningUnderstandingService service = new();

        IReadOnlyList<OpeningUnderstandingCard> cards = service.BuildCards(overview, line);

        Assert.Equal(3, cards.Count);
        Assert.Equal(cards.Count, cards.Select(card => card.Kind).Distinct().Count());
        Assert.Contains(cards, card => card.Kind == OpeningUnderstandingCardKind.OpeningPlan);
        Assert.Contains(cards, card => card.Kind == OpeningUnderstandingCardKind.PieceSetup);
        Assert.Contains(cards, card => card.Kind == OpeningUnderstandingCardKind.CommonTrap);
    }

    [Fact]
    public void BuildCards_FallbackIsNotEmptyWithMinimalOverview()
    {
        OpeningLineCatalogItem line = CreateLine();
        OpeningTrainerOverview overview = CreateOverview(line, mainLine: [], branches: [], ideas: [], priorities: []);
        OpeningUnderstandingService service = new();

        IReadOnlyList<OpeningUnderstandingCard> cards = service.BuildCards(overview, line);

        Assert.All(cards, card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Title));
            Assert.False(string.IsNullOrWhiteSpace(card.Body));
        });
    }

    [Fact]
    public void BuildCards_MainRiskUsesCommonBranchWhenAvailable()
    {
        OpeningLineCatalogItem line = CreateLine();
        OpeningTrainingBranch branch = CreateBranch("d5", "d7d5", "c4");
        OpeningTrainerOverview overview = CreateOverview(line, branches: [branch]);
        OpeningUnderstandingService service = new();

        OpeningUnderstandingCard risk = service.BuildCards(overview, line)
            .Single(card => card.Kind == OpeningUnderstandingCardKind.CommonTrap);

        Assert.Contains("d5", risk.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCards_CommonTrapDoesNotInventSpecificTrapWithoutData()
    {
        OpeningLineCatalogItem line = CreateLine();
        OpeningTrainerOverview overview = CreateOverview(line, branches: [], priorities: []);
        OpeningUnderstandingService service = new();

        OpeningUnderstandingCard trap = service.BuildCards(overview, line)
            .Single(card => card.Kind == OpeningUnderstandingCardKind.CommonTrap);

        Assert.Contains("No specific risk", trap.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static OpeningTrainerOverview CreateOverview(
        OpeningLineCatalogItem line,
        IReadOnlyList<OpeningLineMove>? mainLine = null,
        IReadOnlyList<OpeningTrainingBranch>? branches = null,
        IReadOnlyList<OpeningMoveIdea>? ideas = null,
        IReadOnlyList<TrainingPriorityItem>? priorities = null)
    {
        IReadOnlyList<OpeningTrainingBranch> resolvedBranches = branches ?? [CreateBranch("Nc6", "b8c6", "Bb5")];
        return new OpeningTrainerOverview(
            line.OpeningKey,
            line.LineKey,
            line.RepertoireSide,
            line.Eco,
            line.OpeningName,
            line.VariationName,
            mainLine ?? CreateMainLine(),
            resolvedBranches,
            new OpponentReplyProfile(line.LineKey, line.RepertoireSide, [], "Common replies are available."),
            new OpeningCoverageSummary(3, 1, 2, 2, 33.3, 1, 1, 4),
            priorities ?? [],
            [],
            ideas ?? [new OpeningMoveIdea("Nf3", [OpeningMoveIdeaTag.DevelopPiece], "Develop quickly and fight for the center.")]);
    }

    private static IReadOnlyList<OpeningLineMove> CreateMainLine()
    {
        return
        [
            new(1, 1, PlayerSide.White, "e4", "e2e4", new OpeningPositionKey("root"), new OpeningPositionKey("after-e4"), true),
            new(2, 1, PlayerSide.Black, "e5", "e7e5", new OpeningPositionKey("after-e4"), new OpeningPositionKey("after-e5"), true),
            new(3, 2, PlayerSide.White, "Nf3", "g1f3", new OpeningPositionKey("after-e5"), new OpeningPositionKey("after-nf3"), true,
                new OpeningMoveIdea("Nf3", [OpeningMoveIdeaTag.DevelopPiece], "Nf3 develops a piece and attacks e5."))
        ];
    }

    private static OpeningTrainingBranch CreateBranch(string opponentMove, string opponentMoveUci, string response)
    {
        OpeningTrainingMoveOption recommendedResponse = new(
            response,
            "c2c4",
            OpeningTrainingMoveRole.Expected,
            true,
            "Prepared response.");

        return new OpeningTrainingBranch(
            new OpeningBranchKey($"branch-{opponentMoveUci}"),
            opponentMove,
            opponentMoveUci,
            5,
            "Book branch",
            recommendedResponse,
            [],
            [],
            new OpeningPositionKey($"after-{opponentMoveUci}"));
    }

    private static OpeningLineCatalogItem CreateLine()
    {
        return new OpeningLineCatalogItem(
            new OpeningKey("C20"),
            new OpeningLineKey("C20:main"),
            RepertoireSide.White,
            "C20",
            "King's Pawn",
            "Main line",
            "King's Pawn (C20)",
            new OpeningPositionKey("root"),
            new ChessGame().GetFen(),
            20,
            3);
    }
}
