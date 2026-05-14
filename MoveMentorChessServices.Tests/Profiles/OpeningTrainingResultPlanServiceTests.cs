using MoveMentorChess.Training;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainingResultPlanServiceTests
{
    [Fact]
    public void BuildPlan_CleanSessionReturnsTomorrowReason()
    {
        OpeningTrainingResultPlanService service = new();
        TrainingSessionOutcomeSummary summary = CreateSummary(correct: 4);
        IReadOnlyList<TrainingNextAction> actions =
        [
            new("return-tomorrow", TrainingNextActionKind.ReturnTomorrow, "Return tomorrow", "Review later.", "Back", 80, 1440)
        ];

        TrainingResultLearningPlan plan = service.BuildPlan(summary, [], actions);

        Assert.Equal("Completed: 4/4", plan.MasteredText);
        Assert.Contains("tomorrow", plan.NextReviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean line", plan.ReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void BuildPlan_WrongAttemptCreatesReviewItem()
    {
        OpeningTrainingResultPlanService service = new();
        TrainingSessionOutcomeSummary summary = CreateSummary(correct: 2, wrong: 1);
        OpeningTrainingAttemptResult wrong = CreateAttempt("position-3", "h3", OpeningTrainingScore.Wrong, repeat: true);

        TrainingResultLearningPlan plan = service.BuildPlan(summary, [wrong], []);

        TrainingResultReviewItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal("Incorrect attempt: h3", item.MoveText);
        Assert.Equal("wrong attempt", item.ReasonText);
        Assert.Equal("h3", item.AttemptedMoveText);
        Assert.Equal("not available", item.PreparedMoveText);
        Assert.Equal("High", item.PriorityText);
        Assert.Contains("reinforcement", plan.ReasonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_HintSessionExplainsHintReason()
    {
        OpeningTrainingResultPlanService service = new();
        TrainingSessionOutcomeSummary summary = CreateSummary(correct: 3, hints: 1);

        TrainingResultLearningPlan plan = service.BuildPlan(summary, [], []);

        Assert.Contains("hint", plan.ReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not automatic", plan.ReasonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_DontKnowHasHighestReviewPriority()
    {
        OpeningTrainingResultPlanService service = new();
        TrainingSessionOutcomeSummary summary = CreateSummary(correct: 1, wrong: 2, hints: 1);
        OpeningTrainingAttemptResult wrong = CreateAttempt("position-2", "h3", OpeningTrainingScore.Wrong, repeat: true);
        OpeningTrainingAttemptResult dontKnow = CreateAttempt("position-4", "I do not know", OpeningTrainingScore.Wrong, repeat: true);

        TrainingResultLearningPlan plan = service.BuildPlan(summary, [wrong, dontKnow], []);

        Assert.Equal("Incorrect attempt: I don't know", plan.ReviewItems[0].MoveText);
        Assert.Equal("I don't know", plan.ReviewItems[0].ReasonText);
        Assert.Contains("I don't know", plan.ReasonText, StringComparison.OrdinalIgnoreCase);
    }

    private static TrainingSessionOutcomeSummary CreateSummary(
        int correct,
        int wrong = 0,
        int playable = 0,
        int hints = 0)
    {
        int completed = correct + wrong + playable;
        double accuracy = completed == 0 ? 0 : (double)(correct + playable) / completed * 100d;

        return new TrainingSessionOutcomeSummary(
            "Summary",
            completed,
            completed,
            correct,
            playable,
            wrong,
            hints,
            100,
            accuracy);
    }

    private static OpeningTrainingAttemptResult CreateAttempt(
        string positionId,
        string submittedMove,
        OpeningTrainingScore score,
        bool repeat)
    {
        return new OpeningTrainingAttemptResult(
            positionId,
            OpeningTrainingMode.LineRecall,
            OpeningTrainingSourceKind.ExampleGame,
            OpeningTrainingAttemptStatus.Normal,
            submittedMove,
            submittedMove == "I do not know" ? null : submittedMove,
            null,
            [],
            score,
            "Attempt result.",
            [],
            [],
            [],
            null,
            null,
            null,
            null,
            TrainingMistakeCategory.Unknown,
            repeat);
    }
}
