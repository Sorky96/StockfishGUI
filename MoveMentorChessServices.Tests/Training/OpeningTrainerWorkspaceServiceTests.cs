using MoveMentorChess.Persistence;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainerWorkspaceServiceTests
{
    [Fact]
    public void SaveScheduledActions_UsesInjectedClock()
    {
        DateTime nowUtc = new(2026, 5, 18, 12, 30, 0, DateTimeKind.Utc);
        CapturingHistoryStore store = new();
        OpeningTrainerWorkspaceService workspace = new(store, new FixedClock(nowUtc));
        OpeningTrainingSessionResult result = new()
        {
            SessionId = "session-1",
            PlayerKey = "player-1"
        };
        TrainingNextAction nextAction = new(
            "repeat-after-break",
            TrainingNextActionKind.RepeatAfterBreak,
            "Repeat",
            "Repeat after a break.",
            "Repeat",
            90,
            DelayMinutes: 15);

        IReadOnlyList<OpeningTrainingScheduledAction> actions = workspace.SaveScheduledActions(result, [nextAction]);

        OpeningTrainingScheduledAction action = Assert.Single(actions);
        Assert.Equal(nowUtc, action.CreatedUtc);
        Assert.Equal(nowUtc.AddMinutes(15), action.DueUtc);
        OpeningTrainingScheduledAction savedAction = Assert.Single(store.SavedActions);
        Assert.Equal(action, savedAction);
    }

    [Fact]
    public void TrackTelemetry_UsesInjectedClock()
    {
        DateTime nowUtc = new(2026, 5, 18, 14, 45, 0, DateTimeKind.Utc);
        CapturingHistoryStore store = new();
        OpeningTrainerWorkspaceService workspace = new(store, new FixedClock(nowUtc));

        workspace.TrackTelemetry(OpeningTrainingTelemetryEvents.OpeningTrainerOpened, "Player One");

        OpeningTrainingTelemetryEvent telemetryEvent = Assert.Single(workspace.GetTelemetrySnapshot());
        Assert.Equal(nowUtc, telemetryEvent.CreatedUtc);
        Assert.Equal("player one", telemetryEvent.PlayerKey);
        OpeningTrainingTelemetryEvent savedEvent = Assert.Single(store.SavedTelemetryEvents);
        Assert.Equal(telemetryEvent, savedEvent);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class CapturingHistoryStore : IAnalysisStore, IOpeningTrainingHistoryStore, IOpeningTrainingTelemetryStore
    {
        public List<OpeningTrainingScheduledAction> SavedActions { get; } = [];

        public List<OpeningTrainingTelemetryEvent> SavedTelemetryEvents { get; } = [];

        public void SaveImportedGame(ImportedGame game) => throw new NotSupportedException();

        public void SaveImportedGames(IReadOnlyList<ImportedGame> games) => throw new NotSupportedException();

        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
        {
            game = null;
            throw new NotSupportedException();
        }

        public bool DeleteImportedGame(string gameFingerprint) => throw new NotSupportedException();

        public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200) => [];

        public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500) => [];

        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result)
        {
            result = null;
            throw new NotSupportedException();
        }

        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result) => throw new NotSupportedException();

        public IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000) => [];

        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state)
        {
            state = null;
            throw new NotSupportedException();
        }

        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state) => throw new NotSupportedException();

        public void SaveOpeningTrainingSessionResult(OpeningTrainingSessionResult result)
        {
        }

        public IReadOnlyList<OpeningTrainingSessionResult> ListOpeningTrainingSessionResults(string? playerKey = null, int limit = 200) => [];

        public void SaveOpeningReviewItems(string playerKey, IReadOnlyList<OpeningReviewItem> items)
        {
        }

        public IReadOnlyList<OpeningReviewItem> ListOpeningReviewItems(string? playerKey = null, int limit = 1000) => [];

        public void SaveOpeningTrainingScheduledActions(string playerKey, IReadOnlyList<OpeningTrainingScheduledAction> actions)
        {
            SavedActions.AddRange(actions);
        }

        public IReadOnlyList<OpeningTrainingScheduledAction> ListDueOpeningTrainingScheduledActions(string? playerKey, DateTime nowUtc, int limit = 50) => [];

        public void MarkOpeningTrainingScheduledActionCompleted(string playerKey, string actionId, DateTime completedUtc)
        {
        }

        public void SaveOpeningTrainingTelemetryEvent(OpeningTrainingTelemetryEvent telemetryEvent)
        {
            SavedTelemetryEvents.Add(telemetryEvent);
        }

        public IReadOnlyList<OpeningTrainingTelemetryEvent> ListOpeningTrainingTelemetryEvents(
            string? playerKey = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int limit = 500)
            => SavedTelemetryEvents;
    }
}
