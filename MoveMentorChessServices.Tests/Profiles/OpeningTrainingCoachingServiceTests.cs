using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainingCoachingServiceTests
{
    [Fact]
    public void BuildHints_ReturnsLayeredHints()
    {
        OpeningTrainingPosition position = CreatePosition();
        OpeningTrainingCoachingService service = new();

        IReadOnlyList<TrainingCoachHint> hints = service.BuildHints(position);

        Assert.Equal(5, hints.Count);
        Assert.Equal(TrainingCoachHintLevel.Light, hints[0].Level);
        Assert.Equal(TrainingCoachHintLevel.Plan, hints[1].Level);
        Assert.Equal(TrainingCoachHintLevel.Structure, hints[2].Level);
        Assert.Equal(TrainingCoachHintLevel.OpponentIdea, hints[3].Level);
        Assert.Equal(TrainingCoachHintLevel.Full, hints[4].Level);
        Assert.Contains("Nf3", hints[4].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildHints_LightHintDoesNotRevealMove()
    {
        OpeningTrainingPosition position = CreatePosition();
        OpeningTrainingCoachingService service = new();

        IReadOnlyList<TrainingCoachHint> hints = service.BuildHints(position);

        Assert.DoesNotContain(hints.Where(hint => hint.Level != TrainingCoachHintLevel.Full), hint =>
            hint.Text.Contains("Nf3", StringComparison.OrdinalIgnoreCase)
            || hint.Text.Contains("g1f3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildHints_FullHintIncludesMoveAndIdea()
    {
        OpeningTrainingPosition position = CreatePosition();
        OpeningTrainingCoachingService service = new();

        TrainingCoachHint full = service.BuildHints(position).Single(hint => hint.Level == TrainingCoachHintLevel.Full);

        Assert.Contains("Nf3", full.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("develops", full.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildHints_BranchAwarenessMentionsOpponentPlan()
    {
        OpeningTrainingPosition position = CreateBranchAwarenessPosition();
        OpeningTrainingCoachingService service = new();

        TrainingCoachHint opponentHint = service.BuildHints(position).Single(hint => hint.Level == TrainingCoachHintLevel.OpponentIdea);

        Assert.Contains("opponent", opponentHint.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply", opponentHint.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddCoaching_AddsRecoveryToWrongResult()
    {
        OpeningTrainingPosition position = CreatePosition();
        OpeningTrainingAttemptResult result = new(
            position.PositionId,
            position.Mode,
            position.SourceKind,
            "h4",
            "h4",
            "h2h4",
            position.CandidateMoves,
            OpeningTrainingScore.Wrong,
            "Wrong move.",
            [],
            position.CandidateMoves,
            []);
        OpeningTrainingCoachingService service = new();

        OpeningTrainingAttemptResult coached = service.AddCoaching(position, result);

        Assert.True(coached.ShouldRepeatImmediately);
        Assert.Equal(TrainingCoachHintLevel.Plan, coached.NextHintLevel);
        Assert.Equal(TrainingMistakeCategory.MissedBookMove, coached.MistakeCategory);
        Assert.Contains("prepared book continuation", coached.RecoverySuggestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("central control", coached.RecoverySuggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nf3", coached.RecoverySuggestion, StringComparison.OrdinalIgnoreCase);
    }

    private static OpeningTrainingPosition CreatePosition()
    {
        OpeningTrainingMoveOption preferred = new(
            "Nf3",
            "g1f3",
            OpeningTrainingMoveRole.Expected,
            true,
            Idea: new OpeningMoveIdea(
                "Nf3",
                [OpeningMoveIdeaTag.DevelopPiece, OpeningMoveIdeaTag.ControlCenter],
                "Nf3 develops a kingside piece while keeping e5 under control."));

        return new OpeningTrainingPosition(
            "position-1",
            new OpeningKey("C20"),
            new OpeningLineKey("C20:main"),
            null,
            new OpeningPositionKey("root"),
            OpeningTrainingMode.LineRecall,
            OpeningTrainingSourceKind.ExampleGame,
            "C20",
            "King's Pawn",
            new ChessGame().GetFen(),
            1,
            1,
            PlayerSide.White,
            "Play the book move.",
            "Find the main move.",
            1,
            RepertoireSide.White,
            OpeningTrainingStrictness.BookFlexible,
            null,
            null,
            "Nf3",
            "Develop the knight before side moves.",
            ["guided-study"],
            [preferred],
            [],
            new OpeningTrainingReference(string.Empty, PlayerSide.White, "Theory", null, null, "Guided study", 1, null),
            "C20:main");
    }

    private static OpeningTrainingPosition CreateBranchAwarenessPosition()
    {
        OpeningTrainingMoveOption response = new(
            "c4",
            "c2c4",
            OpeningTrainingMoveRole.Expected,
            true,
            Idea: new OpeningMoveIdea(
                "c4",
                [OpeningMoveIdeaTag.ControlCenter],
                "c4 challenges the center."));
        OpeningTrainingBranch branch = new(
            new OpeningBranchKey("d7d5"),
            "d5",
            "d7d5",
            4,
            "Book branch",
            response,
            [],
            [],
            new OpeningPositionKey("after-d5"));

        return CreatePosition() with
        {
            Mode = OpeningTrainingMode.BranchAwareness,
            Branches = [branch],
            CandidateMoves =
            [
                new OpeningTrainingMoveOption(
                    "d5",
                    "d7d5",
                    OpeningTrainingMoveRole.Alternative,
                    true,
                    "Book branch")
            ]
        };
    }
}
