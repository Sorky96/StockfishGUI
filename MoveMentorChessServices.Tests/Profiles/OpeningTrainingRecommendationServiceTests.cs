using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainingRecommendationServiceTests
{
    [Fact]
    public void Recommend_PrioritizesRecentWeakHistory()
    {
        OpeningLineCatalogItem quietLine = CreateLine("C20", "King's Pawn", 4, 30);
        OpeningLineCatalogItem weakLine = CreateLine("B12", "Caro-Kann Advance", 2, 10);
        OpeningTrainingSessionResult result = CreateResult("B12", OpeningTrainingScore.Wrong);
        OpeningTrainingRecommendationService service = new();

        TrainingRecommendationCard? recommendation = service.Recommend(
            "player",
            [quietLine, weakLine],
            [],
            [result]);

        Assert.NotNull(recommendation);
        Assert.Equal(weakLine, recommendation!.OpeningLine);
        Assert.Equal(TrainingRecommendationReasonCode.WeakRecentHistory, recommendation.ReasonCode);
        Assert.Equal(TrainingRecommendationType.Recovery, recommendation.RecommendationType);
    }

    [Fact]
    public void Recommend_FallsBackToHighValueTheoryWithoutHistory()
    {
        OpeningLineCatalogItem smallLine = CreateLine("A00", "Polish", 1, 2);
        OpeningLineCatalogItem commonLine = CreateLine("C65", "Ruy Lopez", 6, 60);
        OpeningTrainingRecommendationService service = new();

        TrainingRecommendationCard? recommendation = service.Recommend(
            null,
            [smallLine, commonLine],
            [],
            []);

        Assert.NotNull(recommendation);
        Assert.Equal(commonLine, recommendation!.OpeningLine);
        Assert.Equal(TrainingRecommendationReasonCode.StartHere, recommendation.ReasonCode);
        Assert.Equal(TrainingRecommendationType.General, recommendation.RecommendationType);
        Assert.InRange(recommendation.EstimatedDurationMinutes, 5, 15);
    }

    [Fact]
    public void Recommend_DoesNotApplyReviewedBranchesGloballyAcrossLines()
    {
        OpeningLineCatalogItem reviewedLine = CreateLine("B12", "A Reviewed Caro-Kann", 3, 1);
        OpeningLineCatalogItem unrelatedLine = CreateLine("C20", "Z Unreviewed King's Pawn", 2, 1);
        OpeningTrainingRecommendationService service = new();

        IReadOnlyList<OpeningReviewItem> reviewItems =
        [
            CreateReviewItem(reviewedLine, "branch-b12-1"),
            CreateReviewItem(reviewedLine, "branch-b12-2"),
            CreateReviewItem(reviewedLine, "branch-b12-3")
        ];

        TrainingRecommendationCard? recommendation = service.Recommend(
            "player",
            [reviewedLine, unrelatedLine],
            reviewItems,
            []);

        Assert.NotNull(recommendation);
        Assert.Equal(unrelatedLine, recommendation!.OpeningLine);
    }

    [Fact]
    public void Recommendation_PrioritizesDueScheduledAction()
    {
        OpeningLineCatalogItem commonLine = CreateLine("C65", "Ruy Lopez", 6, 60);
        OpeningLineCatalogItem dueLine = CreateLine("B12", "Caro-Kann Advance", 1, 1);
        OpeningTrainingScheduledAction dueAction = new(
            "due-action",
            "player",
            "session-1",
            TrainingNextActionKind.ReturnTomorrow,
            dueLine.LineKey,
            null,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddMinutes(-1),
            OpeningTrainingScheduledActionStatus.Pending,
            null,
            80,
            "return-tomorrow");
        OpeningTrainingRecommendationService service = new();

        TrainingRecommendationCard? recommendation = service.Recommend(
            "player",
            [commonLine, dueLine],
            [],
            [],
            [dueAction]);

        Assert.NotNull(recommendation);
        Assert.Equal(dueLine, recommendation!.OpeningLine);
        Assert.Equal(TrainingRecommendationReasonCode.RevisitDue, recommendation.ReasonCode);
        Assert.Contains("due", recommendation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommendation_IgnoresImmediateChoiceActions()
    {
        OpeningLineCatalogItem commonLine = CreateLine("C65", "Ruy Lopez", 6, 60);
        OpeningLineCatalogItem immediateLine = CreateLine("B22", "Sicilian Defense: Alapin", 1, 1);
        DateTime createdUtc = DateTime.UtcNow.AddMinutes(-5);
        OpeningTrainingScheduledAction immediateAction = new(
            "immediate-action",
            "player",
            "session-1",
            TrainingNextActionKind.RepeatNow,
            immediateLine.LineKey,
            null,
            null,
            createdUtc,
            createdUtc,
            OpeningTrainingScheduledActionStatus.Pending,
            null,
            100,
            "repeat-now");
        OpeningTrainingRecommendationService service = new();

        TrainingRecommendationCard? recommendation = service.Recommend(
            "player",
            [commonLine, immediateLine],
            [],
            [],
            [immediateAction]);

        Assert.NotNull(recommendation);
        Assert.Equal(commonLine, recommendation!.OpeningLine);
        Assert.NotEqual(TrainingRecommendationReasonCode.RevisitDue, recommendation.ReasonCode);
    }

    private static OpeningLineCatalogItem CreateLine(string eco, string displayName, int branchCount, int gameCount)
    {
        return new OpeningLineCatalogItem(
            new OpeningKey(eco),
            new OpeningLineKey($"{eco}:main"),
            RepertoireSide.White,
            eco,
            displayName,
            "Main line",
            displayName,
            new OpeningPositionKey($"{eco}:root"),
            new ChessGame().GetFen(),
            gameCount,
            branchCount);
    }

    private static OpeningTrainingSessionResult CreateResult(string eco, OpeningTrainingScore score)
    {
        OpeningTrainingRecordedAttempt attempt = new(
            "position-1",
            OpeningTrainingMode.LineRecall,
            OpeningTrainingSourceKind.ExampleGame,
            eco,
            "Opening",
            null,
            "h4",
            "h4",
            "h2h4",
            score,
            DateTime.UtcNow);

        return new OpeningTrainingSessionResult(
            "session-1",
            "player",
            "Player",
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            OpeningTrainingSessionOutcome.Completed,
            1,
            1,
            score == OpeningTrainingScore.Correct ? 1 : 0,
            0,
            score == OpeningTrainingScore.Wrong ? 1 : 0,
            [eco],
            [],
            [attempt]);
    }

    private static OpeningReviewItem CreateReviewItem(OpeningLineCatalogItem line, string branchKey)
    {
        DateTime reviewedUtc = DateTime.UtcNow;
        return new OpeningReviewItem(
            new OpeningBranchKey(branchKey),
            new OpeningPositionKey($"{branchKey}:position"),
            reviewedUtc,
            reviewedUtc.AddDays(4),
            2.2,
            1,
            0,
            1,
            line.OpeningKey,
            line.LineKey);
    }
}
