using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainingNextActionServiceTests
{
    [Fact]
    public void BuildNextActions_RepeatsAndRepairsAfterWrongAttempts()
    {
        OpeningTrainingNextActionService service = new();
        TrainingSessionOutcomeSummary summary = new(
            "Needs reinforcement",
            8,
            6,
            4,
            1,
            1,
            0,
            75,
            83.3);

        IReadOnlyList<TrainingNextAction> actions = service.BuildNextActions(summary);

        Assert.Equal(TrainingNextActionKind.RepeatNow, actions[0].Kind);
        Assert.Equal("Repeat this line now", actions[0].Title);
        Assert.Contains("Good session", actions[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(actions, action => action.Kind == TrainingNextActionKind.RepairWeakBranches);
    }

    [Fact]
    public void BuildNextActions_UsesRepairToneAfterManyWrongAttempts()
    {
        OpeningTrainingNextActionService service = new();
        TrainingSessionOutcomeSummary summary = new(
            "Needs reinforcement",
            8,
            8,
            3,
            1,
            4,
            1,
            100,
            50);

        IReadOnlyList<TrainingNextAction> actions = service.BuildNextActions(summary);

        Assert.Equal(TrainingNextActionKind.RepeatNow, actions[0].Kind);
        Assert.Equal("Repeat a smaller repair pass", actions[0].Title);
        Assert.Contains("diagnostic", actions[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNextActions_SpacesCleanSession()
    {
        OpeningTrainingNextActionService service = new();
        TrainingSessionOutcomeSummary summary = new(
            "Stable line",
            8,
            8,
            8,
            0,
            0,
            0,
            100,
            100);

        IReadOnlyList<TrainingNextAction> actions = service.BuildNextActions(summary);

        Assert.Equal(TrainingNextActionKind.ReturnTomorrow, actions[0].Kind);
        Assert.Contains("Clean session", actions[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(actions, action => action.Kind == TrainingNextActionKind.BrowseAnotherOpening);
    }

    [Fact]
    public void BuildNextActions_UsesAlmostAutomaticToneAfterHintsOrAlternatives()
    {
        OpeningTrainingNextActionService service = new();
        TrainingSessionOutcomeSummary summary = new(
            "Almost stable",
            8,
            8,
            6,
            2,
            0,
            1,
            100,
            100);

        IReadOnlyList<TrainingNextAction> actions = service.BuildNextActions(summary);

        Assert.Equal(TrainingNextActionKind.RepeatAfterBreak, actions[0].Kind);
        Assert.Contains("Good session", actions[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not automatic", actions[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNextActions_CreatesDueActionForRepeatAfterBreak()
    {
        OpeningTrainingNextActionService service = new();
        DateTime createdUtc = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        TrainingNextAction nextAction = new(
            "repeat-after-break",
            TrainingNextActionKind.RepeatAfterBreak,
            "Repeat after a short break",
            "Repeat later.",
            "Repeat after break",
            90,
            10);
        OpeningTrainingSessionResult session = CreateSessionResult(createdUtc);

        IReadOnlyList<OpeningTrainingScheduledAction> actions = service.BuildScheduledActions(
            "Alpha",
            session,
            [nextAction],
            createdUtc);

        OpeningTrainingScheduledAction action = Assert.Single(actions);
        Assert.Equal("alpha", action.PlayerKey);
        Assert.Equal(session.SessionId, action.SessionId);
        Assert.Equal(TrainingNextActionKind.RepeatAfterBreak, action.Kind);
        Assert.Equal(createdUtc.AddMinutes(10), action.DueUtc);
        Assert.Equal(OpeningTrainingScheduledActionStatus.Pending, action.Status);
        Assert.Equal("repeat-after-break", action.SourceActionId);
        Assert.Equal(new OpeningLineKey("C20:main"), action.LineKey);
    }

    private static OpeningTrainingSessionResult CreateSessionResult(DateTime completedUtc)
    {
        OpeningTrainingRecordedAttempt attempt = new(
            "position-1",
            OpeningTrainingMode.LineRecall,
            OpeningTrainingSourceKind.ExampleGame,
            OpeningTrainingAttemptStatus.Normal,
            "C20",
            "King's Pawn",
            null,
            "Nf3",
            "Nf3",
            "g1f3",
            OpeningTrainingScore.Playable,
            completedUtc,
            new OpeningBranchKey("branch-1"),
            new OpeningPositionKey("position-1"),
            new OpeningKey("C20"),
            new OpeningLineKey("C20:main"));

        return new OpeningTrainingSessionResult(
            "session-1",
            "alpha",
            "Alpha",
            completedUtc.AddMinutes(-5),
            completedUtc,
            OpeningTrainingSessionOutcome.Completed,
            OpeningTrainingStyle.Memorization,
            OpeningTrainingStrictness.BookFlexible,
            1,
            1,
            0,
            1,
            0,
            ["C20"],
            [],
            [attempt]);
    }
}
