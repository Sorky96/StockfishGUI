using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class PlayerOpeningPlanServiceTests
{
    [Fact]
    public void BuildPlan_UsesRecommendationForToday()
    {
        OpeningLineCatalogItem line = CreateLine("C20", "King's Pawn", 4, 20);
        TrainingRecommendationCard recommendation = new(
            line,
            10,
            TrainingRecommendationDifficulty.Medium,
            TrainingRecommendationReasonCode.HighValueTheory,
            TrainingRecommendationType.General,
            "High-value theory branch.",
            "Start guided study",
            "Browse all openings",
            80);
        PlayerOpeningPlanService service = new();

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", recommendation, [line], [], []);

        Assert.Equal("alpha", plan.PlayerKey);
        Assert.Single(plan.Today);
        Assert.Equal("Main line review", plan.Today[0].Title);
        Assert.Equal(0, plan.Progress.SessionCount);
    }

    [Fact]
    public void BuildPlan_PrioritizesWeeklyRepairsFromWrongHistory()
    {
        OpeningLineCatalogItem quietLine = CreateLine("A00", "Polish", 2, 20);
        OpeningLineCatalogItem weakLine = CreateLine("B12", "Caro-Kann Advance", 2, 5);
        OpeningTrainingSessionResult result = CreateResult("B12", OpeningTrainingScore.Wrong);
        PlayerOpeningPlanService service = new();

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", null, [quietLine, weakLine], [], [result]);

        Assert.NotEmpty(plan.ThisWeek);
        Assert.Equal("B12", plan.ThisWeek[0].Eco);
        Assert.Contains("moves are ready for review", plan.ThisWeek[0].Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plan.Progress.SessionCount);
    }

    [Fact]
    public void BuildPlan_DoesNotReduceGapForUnrelatedLine()
    {
        OpeningLineCatalogItem reviewedLine = CreateLine("B12", "A Reviewed Caro-Kann", 3, 1);
        OpeningLineCatalogItem unrelatedLine = CreateLine("C20", "Z Unreviewed King's Pawn", 2, 1);
        PlayerOpeningPlanService service = new();
        IReadOnlyList<OpeningReviewItem> reviewItems =
        [
            CreateReviewItem(reviewedLine, "branch-b12-1"),
            CreateReviewItem(reviewedLine, "branch-b12-2"),
            CreateReviewItem(reviewedLine, "branch-b12-3")
        ];

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", null, [reviewedLine, unrelatedLine], reviewItems, []);

        Assert.Equal(unrelatedLine.Eco, plan.ThisWeek[0].Eco);
        Assert.Contains("2 common branch gap", plan.ThisWeek[0].Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(unrelatedLine.Eco, plan.LongTermGaps[0].Eco);
    }

    [Fact]
    public void BuildPlan_UsesReviewedBranchesForMatchingLine()
    {
        OpeningLineCatalogItem line = CreateLine("C20", "King's Pawn", 3, 1);
        PlayerOpeningPlanService service = new();
        IReadOnlyList<OpeningReviewItem> reviewItems =
        [
            CreateReviewItem(line, "branch-c20-1"),
            CreateReviewItem(line, "branch-c20-2")
        ];
        OpeningTrainingSessionResult result = CreateResult("C20", OpeningTrainingScore.Correct);

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", null, [line], reviewItems, [result]);

        PlayerOpeningPlanItem longTermGap = Assert.Single(plan.LongTermGaps);
        Assert.Contains("1 branch gap", longTermGap.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_IncludesDueScheduledActionsToday()
    {
        OpeningLineCatalogItem line = CreateLine("C20", "King's Pawn", 3, 10);
        OpeningTrainingScheduledAction dueAction = new(
            "due-action",
            "alpha",
            "session-1",
            TrainingNextActionKind.RepeatAfterBreak,
            line.LineKey,
            null,
            null,
            DateTime.UtcNow.AddMinutes(-20),
            DateTime.UtcNow.AddMinutes(-10),
            OpeningTrainingScheduledActionStatus.Pending,
            null,
            90,
            "repeat-after-break");
        PlayerOpeningPlanService service = new();

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", null, [line], [], [], [dueAction]);

        Assert.NotEmpty(plan.Today);
        Assert.Contains("Main line review", plan.Today[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", plan.Today[0].Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dueAction.DueUtc.ToLocalTime().ToString("HH:mm"), plan.Today[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_GroupsDuplicateDueScheduledActionsToday()
    {
        OpeningLineCatalogItem line = CreateLine("C20", "King's Pawn", 3, 10);
        DateTime firstDueUtc = DateTime.UtcNow.AddMinutes(-40);
        DateTime secondDueUtc = DateTime.UtcNow.AddMinutes(-10);
        OpeningTrainingScheduledAction firstDueAction = new(
            "due-action-1",
            "alpha",
            "session-1",
            TrainingNextActionKind.RepeatAfterBreak,
            line.LineKey,
            null,
            null,
            firstDueUtc.AddMinutes(-10),
            firstDueUtc,
            OpeningTrainingScheduledActionStatus.Pending,
            null,
            90,
            "repeat-after-break");
        OpeningTrainingScheduledAction secondDueAction = firstDueAction with
        {
            Id = "due-action-2",
            DueUtc = secondDueUtc,
            CreatedUtc = secondDueUtc.AddMinutes(-10)
        };
        PlayerOpeningPlanService service = new();

        PlayerOpeningPlan plan = service.BuildPlan("Alpha", null, [line], [], [], [firstDueAction, secondDueAction]);

        PlayerOpeningPlanItem item = Assert.Single(plan.Today);
        Assert.Equal("Main line review", item.Title);
        Assert.Contains("2 due items", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstDueUtc.ToLocalTime().ToString("HH:mm"), item.Detail, StringComparison.Ordinal);
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
            "alpha",
            "Alpha",
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
