using System.IO;
using MoveMentorChess.Analysis;
using MoveMentorChess.Opening;
using MoveMentorChess.Diagnostics;
using MoveMentorChess.Persistence;
using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class SqliteAnalysisStoreTests
{
    private const string GameOnePgn = """
[Event "Mini"]
[Site "Chess.com"]
[Date "2026.04.17"]
[White "Alpha"]
[Black "Beta"]
[Result "1-0"]
[ECO "C20"]
[WhiteElo "812"]
[BlackElo "799"]
[TimeControl "600"]
[Termination "Alpha won by checkmate"]
[Link "https://www.chess.com/game/live/1"]

1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0
""";

    private const string GameTwoPgn = """
[Event "Mini"]
[Site "Lichess"]
[Date "2026.03.01"]
[White "Gamma"]
[Black "Delta"]
[Result "0-1"]
[ECO "B01"]

1. e4 d5 2. exd5 Qxd5 3. Nc3 Qa5 0-1
""";

    private const string OpeningImportGameOnePgn = """
[Event "Shared E4"]
[Site "Test"]
[Date "2026.04.23"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "C20"]
[Opening "King's Pawn Game"]
[Variation "Main Line"]

1. e4 e5 2. Nf3 Nc6 *
""";

    private const string OpeningImportGameTwoPgn = """
[Event "Shared E4 Alternative"]
[Site "Test"]
[Date "2026.04.23"]
[White "Gamma"]
[Black "Delta"]
[Result "*"]
[ECO "C20"]
[Opening "King's Pawn Game"]
[Variation "Bishop Line"]

1. e4 e5 2. Bc4 Nc6 *
""";

    private const string OpeningImportGameThreePgn = """
[Event "Shared D4"]
[Site "Test"]
[Date "2026.04.23"]
[White "Eta"]
[Black "Theta"]
[Result "*"]
[ECO "D00"]
[Opening "Queen's Pawn Game"]
[Variation "Main Line"]

1. d4 d5 2. Nf3 Nf6 *
""";

    private const string MismatchedB22Pgn = """
[Event "Mismatched ECO"]
[Site "Test"]
[Date "2026.04.23"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "B22"]
[Opening "Semi-Open Game"]

1. e4 e5 2. Nf3 Nc6 *
""";

    private const string AlapinB22Pgn = """
[Event "Alapin"]
[Site "Test"]
[Date "2026.04.23"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "B22"]
[Opening "Sicilian Defense"]
[Variation "Alapin"]

1. e4 c5 2. c3 Nf6 *
""";

    private const string OpenSicilianB22Pgn = """
[Event "Wrong B22 Open Sicilian"]
[Site "Test"]
[Date "2026.04.23"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "B22"]
[Opening "Sicilian Defense"]

1. e4 c5 2. Nf3 Nc6 *
""";

    [Fact]
    public void SqliteAnalysisStore_SavesAndLoadsImportedGame()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame savedGame = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(savedGame.PgnText);

            store.SaveImportedGame(savedGame);

            bool found = store.TryLoadImportedGame(fingerprint, out ImportedGame? loadedGame);

            Assert.True(found);
            Assert.NotNull(loadedGame);
            Assert.Equal(savedGame.PgnText, loadedGame!.PgnText);
            Assert.Equal(savedGame.WhitePlayer, loadedGame.WhitePlayer);
            Assert.Equal(savedGame.BlackPlayer, loadedGame.BlackPlayer);
            Assert.Equal(812, loadedGame.WhiteElo);
            Assert.Equal(799, loadedGame.BlackElo);
            Assert.Equal("600", loadedGame.Metadata?.TimeControl);
            Assert.Equal(GameTimeControlCategory.Rapid, loadedGame.Metadata?.TimeControlCategory);
            Assert.Equal("Alpha won by checkmate", loadedGame.Metadata?.Termination);
            Assert.Equal("https://www.chess.com/game/live/1", loadedGame.Metadata?.Link);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_ListsImportedGamesWithFiltering()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            store.SaveImportedGame(PgnGameParser.Parse(GameOnePgn));
            store.SaveImportedGame(PgnGameParser.Parse(GameTwoPgn));

            IReadOnlyList<SavedImportedGameSummary> allGames = store.ListImportedGames();
            IReadOnlyList<SavedImportedGameSummary> chessDotComGames = store.ListImportedGames("chess.com");
            IReadOnlyList<SavedImportedGameSummary> ecoGames = store.ListImportedGames("b01");
            IReadOnlyList<SavedImportedGameSummary> missingGames = store.ListImportedGames("no-such-game");

            Assert.Equal(2, allGames.Count);
            Assert.Single(chessDotComGames);
            Assert.Equal("Alpha", chessDotComGames[0].WhitePlayer);
            Assert.Single(ecoGames);
            Assert.Equal("Gamma", ecoGames[0].WhitePlayer);
            Assert.Empty(missingGames);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_SaveImportedGameUsesInjectedClock()
    {
        string databasePath = CreateTempDatabasePath();
        DateTime nowUtc = new(2026, 5, 18, 18, 20, 0, DateTimeKind.Utc);

        try
        {
            SqliteAnalysisStore store = new(databasePath, clock: new FixedClock(nowUtc));

            store.SaveImportedGame(PgnGameParser.Parse(GameOnePgn));

            SavedImportedGameSummary summary = Assert.Single(store.ListImportedGames());
            Assert.Equal(nowUtc, summary.UpdatedUtc.ToUniversalTime());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_SavesAndLoadsAnalysisWindowState()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(game.PgnText);
            AnalysisWindowState state = new(PlayerSide.Black, 2, 0);

            store.SaveWindowState(fingerprint, state);

            bool found = store.TryLoadWindowState(fingerprint, out AnalysisWindowState? restoredState);

            Assert.True(found);
            Assert.Equal(state, restoredState);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_ListsSavedAnalysisResults()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            GameAnalysisResult result = new(
                game,
                PlayerSide.White,
                [],
                [],
                [
                    new SelectedMistake(
                        [],
                        MoveQualityBucket.Mistake,
                        new MistakeTag("opening_principles", 0.8, ["evidence"]),
                        new MoveExplanation("Short", "Hint", "Detailed"))
                ]);

            store.SaveResult(key, result);

            IReadOnlyList<GameAnalysisResult> listed = store.ListResults("Alpha");

            Assert.Single(listed);
            Assert.Equal(PlayerSide.White, listed[0].AnalyzedSide);
            Assert.Equal("Alpha", listed[0].Game.WhitePlayer);
            Assert.Single(listed[0].HighlightedMistakes);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_SavesStructuredMoveAnalyses()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions(Depth: 16, MultiPv: 2));
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "opening_principles");
            GameAnalysisResult result = new(
                game,
                PlayerSide.White,
                [move.Replay],
                [move],
                [
                    new SelectedMistake(
                        [move],
                        move.Quality,
                        move.MistakeTag,
                        move.Explanation ?? new MoveExplanation("Short", "Hint", "Detailed"))
                ]);

            store.SaveResult(key, result);

            IReadOnlyList<StoredMoveAnalysis> storedMoves = store.ListMoveAnalyses("Alpha");

            StoredMoveAnalysis storedMove = Assert.Single(storedMoves);
            Assert.Equal(GameFingerprint.Compute(game.PgnText), storedMove.Game.GameFingerprint);
            Assert.Equal(PlayerSide.White, storedMove.Analysis.AnalyzedSide);
            Assert.Equal(16, storedMove.Analysis.Depth);
            Assert.Equal(2, storedMove.Analysis.MultiPv);
            Assert.Equal(3, storedMove.Move.Ply);
            Assert.Equal("Nf3", storedMove.Move.San);
            Assert.Equal("g1f3", storedMove.Move.Uci);
            Assert.Equal("opening_principles", storedMove.Advice.MistakeLabel);
            Assert.Equal(145, storedMove.Move.CentipawnLoss);
            Assert.True(storedMove.Advice.IsHighlighted);
            Assert.Equal("Short", storedMove.Advice.ShortExplanation);
            Assert.Equal("Detailed", storedMove.Advice.DetailedExplanation);
            Assert.Equal("Hint", storedMove.Advice.TrainingHint);
            Assert.Contains("late_development", storedMove.Advice.Evidence);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_AppliesLatestManualLabelCorrectionToStoredMoves()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions(Depth: 16, MultiPv: 2));
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "missed_tactic");
            GameAnalysisResult result = new(game, PlayerSide.White, [move.Replay], [move], []);

            store.SaveResult(key, result);
            store.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "hanging_piece", "first"));
            store.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "material_loss", "latest"));

            StoredMoveAnalysis storedMove = Assert.Single(store.ListMoveAnalyses("Alpha"));
            MoveAdviceFeedback latest = Assert.Single(store.ListMoveAdviceFeedback("latest"));

            Assert.Equal("material_loss", storedMove.Advice.MistakeLabel);
            Assert.Equal("missed_tactic", storedMove.Advice.OriginalMistakeLabel);
            Assert.NotNull(storedMove.ManualFeedback);
            Assert.Equal(AdviceFeedbackKind.WrongLabel, storedMove.ManualFeedback.ManualFeedbackKind);
            Assert.Equal("material_loss", storedMove.ManualFeedback.ManualCorrectedLabel);
            Assert.Equal("latest", storedMove.ManualFeedback.ManualComment);
            Assert.NotNull(storedMove.ManualFeedback.ManualCorrectedUtc);
            Assert.Equal("material_loss", latest.CorrectedLabel);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_DeletesManualFeedbackWithImportedGame()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(game.PgnText);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "missed_tactic");

            store.SaveImportedGame(game);
            store.SaveResult(key, new GameAnalysisResult(game, PlayerSide.White, [move.Replay], [move], []));
            store.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "hanging_piece", "delete me"));

            bool deleted = store.DeleteImportedGame(fingerprint);

            Assert.True(deleted);
            Assert.Empty(store.ListMoveAdviceFeedback());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void DatasetExporter_IncludesManualCorrectionFields()
    {
        string databasePath = CreateTempDatabasePath();
        string outputPath = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-dataset-{Guid.NewGuid():N}.jsonl");

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "missed_tactic");
            store.SaveResult(key, new GameAnalysisResult(game, PlayerSide.White, [move.Replay], [move], []));
            store.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "hanging_piece", "custom fix"));

            int count = DatasetExporter.ExportJsonl(store, outputPath);
            string jsonl = File.ReadAllText(outputPath);

            Assert.Equal(1, count);
            Assert.Contains("\"OriginalMistakeLabel\":\"missed_tactic\"", jsonl);
            Assert.Contains("\"EffectiveMistakeLabel\":\"hanging_piece\"", jsonl);
            Assert.Contains("\"ManualFeedbackKind\":\"WrongLabel\"", jsonl);
            Assert.Contains("\"ManualCorrectedLabel\":\"hanging_piece\"", jsonl);
            Assert.Contains("\"ManualComment\":\"custom fix\"", jsonl);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void SqliteAnalysisStore_RestoresLegacyHighlightedLabelsFromMoveTags()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "opening_principles");
            GameAnalysisResult result = new(
                game,
                PlayerSide.White,
                [move.Replay],
                [move],
                [
                    new SelectedMistake(
                        [move],
                        move.Quality,
                        null,
                        move.Explanation ?? new MoveExplanation("Short", "Hint", "Detailed"))
                ]);

            store.SaveResult(key, result);

            bool found = store.TryLoadResult(key, out GameAnalysisResult? restored);
            GameAnalysisResult listed = Assert.Single(store.ListResults("Alpha"));

            Assert.True(found);
            Assert.NotNull(restored);
            Assert.Equal("opening_principles", restored!.HighlightedMistakes[0].Tag?.Label);
            Assert.Equal("opening_principles", listed.HighlightedMistakes[0].Tag?.Label);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_SavesAndListsOpeningTrainingSessionResults()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime completedUtc = DateTime.Parse("2026-04-20T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingSessionResult result = new(
                "opening-trainer:alpha:1",
                "alpha",
                "Alpha",
                completedUtc.AddMinutes(-15),
                completedUtc,
                OpeningTrainingSessionOutcome.Completed,
                2,
                2,
                1,
                0,
                1,
                ["C20"],
                ["opening_principles"],
                [
                    new OpeningTrainingRecordedAttempt(
                        "position-1",
                        OpeningTrainingMode.LineRecall,
                        OpeningTrainingSourceKind.OpeningWeakness,
                        "C20",
                        "King's Pawn Game",
                        "opening_principles",
                        "Nf3",
                        "Nf3",
                        "g1f3",
                        OpeningTrainingScore.Correct,
                        completedUtc),
                    new OpeningTrainingRecordedAttempt(
                        "position-2",
                        OpeningTrainingMode.MistakeRepair,
                        OpeningTrainingSourceKind.FirstOpeningMistake,
                        "C20",
                        "King's Pawn Game",
                        "opening_principles",
                        "h3",
                        "h3",
                        "h2h3",
                        OpeningTrainingScore.Wrong,
                        completedUtc)
                ]);

            store.SaveOpeningTrainingSessionResult(result);

            IReadOnlyList<OpeningTrainingSessionResult> listed = store.ListOpeningTrainingSessionResults("Alpha");

            OpeningTrainingSessionResult restored = Assert.Single(listed);
            Assert.Equal(result.SessionId, restored.SessionId);
            Assert.Equal(2, restored.AttemptCount);
            Assert.Equal(1, restored.CorrectCount);
            Assert.Equal(1, restored.WrongCount);
            Assert.Contains("C20", restored.RelatedOpenings);
            Assert.Contains(restored.Attempts, attempt => attempt.Score == OpeningTrainingScore.Wrong);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SaveOpeningReviewItems_UpsertsWithoutDeletingExistingItems()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime now = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningReviewItem existing = new(
                new OpeningBranchKey("branch-existing"),
                new OpeningPositionKey("position-existing"),
                now.AddDays(-1),
                now.AddDays(1),
                2.2,
                1,
                0,
                1,
                new OpeningKey("B12"),
                new OpeningLineKey("B12:main"));
            OpeningReviewItem next = new(
                new OpeningBranchKey("branch-new"),
                new OpeningPositionKey("position-new"),
                now,
                now.AddDays(2),
                1.8,
                0,
                1,
                1,
                new OpeningKey("C20"),
                new OpeningLineKey("C20:main"));

            store.SaveOpeningReviewItems("alpha", [existing]);
            store.SaveOpeningReviewItems("alpha", [next]);

            IReadOnlyList<OpeningReviewItem> items = store.ListOpeningReviewItems("alpha");

            Assert.Equal(2, items.Count);
            Assert.Contains(items, item => item.BranchKey.Value == "branch-existing");
            Assert.Contains(items, item => item.BranchKey.Value == "branch-new");
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SaveOpeningReviewItems_MergesAttemptsForSamePosition()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime now = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningReviewItem first = new(
                new OpeningBranchKey("branch-merge"),
                new OpeningPositionKey("position-merge"),
                now.AddMinutes(-10),
                now.AddDays(4),
                2.2,
                1,
                0,
                1,
                new OpeningKey("C20"),
                new OpeningLineKey("C20:main"));
            OpeningReviewItem second = new(
                new OpeningBranchKey("branch-merge"),
                new OpeningPositionKey("position-merge"),
                now,
                now,
                1.3,
                0,
                1,
                1,
                new OpeningKey("C20"),
                new OpeningLineKey("C20:main"));

            store.SaveOpeningReviewItems("alpha", [first]);
            store.SaveOpeningReviewItems("alpha", [second]);

            OpeningReviewItem merged = Assert.Single(store.ListOpeningReviewItems("alpha"));

            Assert.Equal(2, merged.TotalAttempts);
            Assert.Equal(0, merged.CorrectStreak);
            Assert.Equal(1, merged.WrongStreak);
            Assert.Equal(now, merged.LastReviewedUtc);
            Assert.Equal(now, merged.NextReviewUtc);
            Assert.Equal(1.3, merged.Ease);
            Assert.Equal(new OpeningLineKey("C20:main"), merged.OpeningLineKey);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void ListDueOpeningTrainingScheduledActions_ReturnsOnlyPendingDueItems()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime now = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingScheduledAction due = CreateScheduledAction("due", "alpha", now.AddMinutes(-1), OpeningTrainingScheduledActionStatus.Pending);
            OpeningTrainingScheduledAction future = CreateScheduledAction("future", "alpha", now.AddMinutes(10), OpeningTrainingScheduledActionStatus.Pending);
            OpeningTrainingScheduledAction otherPlayer = CreateScheduledAction("other", "beta", now.AddMinutes(-1), OpeningTrainingScheduledActionStatus.Pending);
            OpeningTrainingScheduledAction completed = CreateScheduledAction("completed", "alpha", now.AddMinutes(-1), OpeningTrainingScheduledActionStatus.Completed);

            store.SaveOpeningTrainingScheduledActions("alpha", [due, future, completed]);
            store.SaveOpeningTrainingScheduledActions("beta", [otherPlayer]);

            IReadOnlyList<OpeningTrainingScheduledAction> actions = store.ListDueOpeningTrainingScheduledActions("alpha", now);

            OpeningTrainingScheduledAction action = Assert.Single(actions);
            Assert.Equal("due", action.Id);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void ExecuteNextAction_MarksActionCompleted()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime now = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingScheduledAction due = CreateScheduledAction("due", "alpha", now.AddMinutes(-1), OpeningTrainingScheduledActionStatus.Pending);

            store.SaveOpeningTrainingScheduledActions("alpha", [due]);
            store.MarkOpeningTrainingScheduledActionCompleted("alpha", "due", now);

            Assert.Empty(store.ListDueOpeningTrainingScheduledActions("alpha", now.AddMinutes(1)));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void ListTelemetryEvents_FiltersByPlayerAndDate()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            DateTime now = DateTime.Parse("2026-05-01T10:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingTelemetryEvent older = CreateTelemetryEvent("alpha", now.AddDays(-2), "old");
            OpeningTrainingTelemetryEvent matching = CreateTelemetryEvent("alpha", now, "match");
            OpeningTrainingTelemetryEvent otherPlayer = CreateTelemetryEvent("beta", now, "other");

            store.SaveOpeningTrainingTelemetryEvent(older);
            store.SaveOpeningTrainingTelemetryEvent(matching);
            store.SaveOpeningTrainingTelemetryEvent(otherPlayer);

            IReadOnlyList<OpeningTrainingTelemetryEvent> events = store.ListOpeningTrainingTelemetryEvents(
                "Alpha",
                now.AddHours(-1),
                now.AddHours(1));

            OpeningTrainingTelemetryEvent telemetryEvent = Assert.Single(events);
            Assert.Equal("alpha", telemetryEvent.PlayerKey);
            Assert.Equal("match", telemetryEvent.Properties!["marker"]);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void Track_PersistsTelemetryEventInSqlite()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            OpeningTrainingTelemetryEvent telemetryEvent = CreateTelemetryEvent("alpha", DateTime.UtcNow, "persisted");

            store.SaveOpeningTrainingTelemetryEvent(telemetryEvent);

            SqliteAnalysisStore reopened = new(databasePath);
            OpeningTrainingTelemetryEvent restored = Assert.Single(reopened.ListOpeningTrainingTelemetryEvents("alpha"));
            Assert.Equal(telemetryEvent.EventName, restored.EventName);
            Assert.Equal(telemetryEvent.LineKey, restored.LineKey);
            Assert.Equal("persisted", restored.Properties!["marker"]);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_DeletesImportedGameTogetherWithAnalysisData()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(game.PgnText);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            GameAnalysisResult result = new(
                game,
                PlayerSide.White,
                [],
                [],
                [
                    new SelectedMistake(
                        [],
                        MoveQualityBucket.Mistake,
                        new MistakeTag("opening_principles", 0.8, ["evidence"]),
                        new MoveExplanation("Short", "Hint", "Detailed"))
                ]);

            store.SaveImportedGame(game);
            store.SaveResult(key, result);
            store.SaveWindowState(fingerprint, new AnalysisWindowState(PlayerSide.White, 1, 2));

            bool deleted = store.DeleteImportedGame(fingerprint);

            Assert.True(deleted);
            Assert.False(store.TryLoadImportedGame(fingerprint, out _));
            Assert.False(store.TryLoadResult(key, out _));
            Assert.False(store.TryLoadWindowState(fingerprint, out _));
            Assert.Empty(store.ListMoveAnalyses());
            Assert.Empty(store.ListImportedGames());
            Assert.Empty(store.ListResults());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_ClearsImportedAnalysisDataWithoutOpeningOrTrainingData()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(game.PgnText);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "missed_tactic");
            DateTime completedUtc = DateTime.Parse("2026-04-20T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingSessionResult trainingResult = new(
                "opening-trainer:alpha:clear",
                "alpha",
                "Alpha",
                completedUtc.AddMinutes(-15),
                completedUtc,
                OpeningTrainingSessionOutcome.Completed,
                1,
                1,
                1,
                1,
                0,
                ["C20"],
                ["opening_principles"],
                []);

            store.SaveImportedGame(game);
            store.SaveResult(key, new GameAnalysisResult(game, PlayerSide.White, [move.Replay], [move], []));
            store.SaveWindowState(fingerprint, new AnalysisWindowState(PlayerSide.White, 1, 2));
            store.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "hanging_piece", "clear me"));
            store.SaveOpeningTree(BuildOpeningTree(OpeningImportGameOnePgn, OpeningImportGameTwoPgn));
            store.SaveOpeningTrainingSessionResult(trainingResult);

            OpeningTreeStoreSummary openingSummaryBefore = store.GetOpeningTreeSummary();

            store.ClearImportedAnalysisData();

            Assert.Empty(store.ListImportedGames());
            Assert.Empty(store.ListResults());
            Assert.Empty(store.ListMoveAnalyses());
            Assert.Empty(store.ListMoveAdviceFeedback());
            Assert.False(store.TryLoadImportedGame(fingerprint, out _));
            Assert.False(store.TryLoadResult(key, out _));
            Assert.False(store.TryLoadWindowState(fingerprint, out _));
            Assert.Equal(openingSummaryBefore, store.GetOpeningTreeSummary());
            OpeningTrainingSessionResult restoredTraining = Assert.Single(store.ListOpeningTrainingSessionResults("alpha"));
            Assert.Equal(trainingResult.SessionId, restoredTraining.SessionId);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_ClearsOnlyDerivedAnalysisDataWhenVersionChanges()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            const string legacyDerivedVersion = "legacy-derived-analysis-v0";
            SqliteAnalysisStore legacyStore = new(databasePath, legacyDerivedVersion);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            string fingerprint = GameFingerprint.Compute(game.PgnText);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Mistake,
                centipawnLoss: 145,
                label: "missed_tactic");
            DateTime completedUtc = DateTime.Parse("2026-04-20T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            OpeningTrainingSessionResult trainingResult = new(
                "opening-trainer:alpha:version-reset",
                "alpha",
                "Alpha",
                completedUtc.AddMinutes(-15),
                completedUtc,
                OpeningTrainingSessionOutcome.Completed,
                1,
                1,
                1,
                1,
                0,
                ["C20"],
                ["opening_principles"],
                []);

            legacyStore.SaveImportedGame(game);
            legacyStore.SaveResult(key, new GameAnalysisResult(game, PlayerSide.White, [move.Replay], [move], []));
            legacyStore.SaveWindowState(fingerprint, new AnalysisWindowState(PlayerSide.White, 1, 2));
            legacyStore.SaveMoveAdviceFeedback(CreateFeedback(key, move, AdviceFeedbackKind.WrongLabel, "hanging_piece", "keep me"));
            legacyStore.SaveOpeningTree(BuildOpeningTree(OpeningImportGameOnePgn, OpeningImportGameTwoPgn));
            legacyStore.SaveOpeningTrainingSessionResult(trainingResult);
            OpeningTreeStoreSummary openingSummaryBefore = legacyStore.GetOpeningTreeSummary();

            SqliteAnalysisStore currentStore = new(databasePath);

            Assert.Equal(SqliteAnalysisStore.CurrentDerivedAnalysisDataVersion, currentStore.GetDerivedAnalysisDataVersion());
            Assert.True(currentStore.TryLoadImportedGame(fingerprint, out ImportedGame? restoredGame));
            Assert.Equal(game.PgnText, restoredGame!.PgnText);
            Assert.False(currentStore.TryLoadResult(key, out _));
            Assert.False(currentStore.TryLoadWindowState(fingerprint, out _));
            Assert.Empty(currentStore.ListResults());
            Assert.Empty(currentStore.ListMoveAnalyses());
            Assert.Equal(openingSummaryBefore, currentStore.GetOpeningTreeSummary());
            MoveAdviceFeedback preservedFeedback = Assert.Single(currentStore.ListMoveAdviceFeedback("keep me"));
            Assert.Equal("hanging_piece", preservedFeedback.CorrectedLabel);
            OpeningTrainingSessionResult restoredTraining = Assert.Single(currentStore.ListOpeningTrainingSessionResults("alpha"));
            Assert.Equal(trainingResult.SessionId, restoredTraining.SessionId);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void SqliteAnalysisStore_RoundTripsPositiveMoveQualityValues()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            ImportedGame game = PgnGameParser.Parse(GameOnePgn);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions());
            MoveAnalysisResult move = CreateMoveAnalysis(
                ply: 3,
                moveNumber: 2,
                phase: GamePhase.Opening,
                quality: MoveQualityBucket.Great,
                centipawnLoss: 8,
                label: "opening_principles");
            GameAnalysisResult result = new(game, PlayerSide.White, [move.Replay], [move], []);

            store.SaveResult(key, result);

            StoredMoveAnalysis storedMove = Assert.Single(store.ListMoveAnalyses("Alpha"));
            bool found = store.TryLoadResult(key, out GameAnalysisResult? restored);

            Assert.True(found);
            Assert.NotNull(restored);
            Assert.Equal(MoveQualityBucket.Great, storedMove.Move.Quality);
            Assert.Equal(MoveQualityBucket.Great, restored!.MoveAnalyses[0].Quality);
            Assert.Equal(2, (int)MoveQualityBucket.Great);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void MoveQualityBucketExtensions_ClassifyProblemAndPositiveRanges()
    {
        Assert.False(MoveQualityBucket.Book.IsProblem());
        Assert.False(MoveQualityBucket.Brilliant.IsProblem());
        Assert.False(MoveQualityBucket.Great.IsProblem());
        Assert.False(MoveQualityBucket.Best.IsProblem());
        Assert.False(MoveQualityBucket.Excellent.IsProblem());
        Assert.False(MoveQualityBucket.Good.IsProblem());
        Assert.True(MoveQualityBucket.Inaccuracy.IsProblem());
        Assert.True(MoveQualityBucket.Mistake.IsProblem());
        Assert.True(MoveQualityBucket.Blunder.IsProblem());

        Assert.True(MoveQualityBucket.Book.IsPositiveOrNeutral());
        Assert.True(MoveQualityBucket.Brilliant.IsPositiveOrNeutral());
        Assert.True(MoveQualityBucket.Great.IsPositiveOrNeutral());
        Assert.True(MoveQualityBucket.Best.IsPositiveOrNeutral());
        Assert.True(MoveQualityBucket.Excellent.IsPositiveOrNeutral());
        Assert.True(MoveQualityBucket.Good.IsPositiveOrNeutral());
        Assert.False(MoveQualityBucket.Inaccuracy.IsPositiveOrNeutral());
        Assert.False(MoveQualityBucket.Mistake.IsPositiveOrNeutral());
        Assert.False(MoveQualityBucket.Blunder.IsPositiveOrNeutral());
    }

    [Fact]
    public void SqliteAnalysisStore_SavesOpeningTreeWithUpserts()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            OpeningTreeBuildResult tree = BuildOpeningTree(OpeningImportGameOnePgn, OpeningImportGameTwoPgn);

            store.SaveOpeningTree(tree);
            OpeningTreeStoreSummary firstSummary = store.GetOpeningTreeSummary();
            store.SaveOpeningTree(tree);
            OpeningTreeStoreSummary secondSummary = store.GetOpeningTreeSummary();

            Assert.Equal(tree.Nodes.Count, firstSummary.NodeCount);
            Assert.Equal(tree.Edges.Count, firstSummary.EdgeCount);
            Assert.Equal(tree.Tags.Count, firstSummary.TagCount);
            Assert.Equal(firstSummary, secondSummary);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void OpeningPgnImportService_SavesImportedGamesAndOpeningTreeToSameSqliteDatabase()
    {
        string databasePath = CreateTempDatabasePath();
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "openings.pgn"),
                OpeningImportGameOnePgn + Environment.NewLine + OpeningImportGameTwoPgn);
            SqliteAnalysisStore store = new(databasePath);
            OpeningPgnImportService importService = new(store, treeStore: store);

            OpeningPgnImportResult result = importService.ImportFolder(folder);
            OpeningTreeStoreSummary summary = store.GetOpeningTreeSummary();

            Assert.Equal(2, store.ListImportedGames().Count);
            Assert.Equal(result.Tree.Nodes.Count, summary.NodeCount);
            Assert.Equal(result.Tree.Edges.Count, summary.EdgeCount);
            Assert.Equal(result.Tree.Tags.Count, summary.TagCount);
            Assert.True(summary.NodeCount > 0);
            Assert.True(summary.EdgeCount > 0);
            Assert.True(summary.TagCount > 0);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void OpeningTheoryQueryService_ReturnsMainPlayableMovesAndTags()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            OpeningTreeBuildResult tree = BuildOpeningTree(
                OpeningImportGameOnePgn,
                OpeningImportGameTwoPgn,
                OpeningImportGameThreePgn);
            store.SaveOpeningTree(tree);
            OpeningTheoryQueryService queryService = new(store);
            const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

            bool found = queryService.TryGetPositionByFen(startFen, out OpeningTheoryPosition? position);
            IReadOnlyList<OpeningTheoryMove> topMoves = queryService.GetTopMovesForFen(startFen);
            IReadOnlyList<OpeningTheoryMove> playableMoves = queryService.GetPlayableMovesForFen(startFen);
            OpeningTheoryMove? mainMove = queryService.GetMainMoveForFen(startFen);

            Assert.True(found);
            Assert.NotNull(position);
            Assert.Equal("C20", position!.Metadata.Eco);
            Assert.Equal("King's Pawn Game", position.Metadata.OpeningName);
            Assert.Equal(2, topMoves.Count);
            Assert.NotNull(mainMove);
            Assert.Equal("e2e4", mainMove!.MoveUci);
            Assert.Equal("e4", mainMove.MoveSan);
            Assert.Equal("opening_book", mainMove.SourceKind);
            Assert.True(mainMove.IsMainMove);
            Assert.True(mainMove.IsPlayableMove);
            OpeningTheoryMove d4 = Assert.Single(topMoves, move => move.MoveUci == "d2d4");
            Assert.False(d4.IsMainMove);
            Assert.False(d4.IsPlayableMove);
            OpeningTheoryMove playable = Assert.Single(playableMoves);
            Assert.Equal("e2e4", playable.MoveUci);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void OpeningSeedBootstrapper_ImportsBundledSeedIntoLocalDatabaseOnce()
    {
        string localDatabasePath = CreateTempDatabasePath();
        string bundledSeedPath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore seedStore = new(bundledSeedPath);
            OpeningTreeBuildResult tree = BuildOpeningTree(
                OpeningImportGameOnePgn,
                OpeningImportGameTwoPgn,
                OpeningImportGameThreePgn);
            seedStore.ReplaceOpeningTree(tree);
            seedStore.SetOpeningSeedVersion("seed-v1");

            OpeningSeedBootstrapper bootstrapper = new(localDatabasePath, bundledSeedPath);

            OpeningSeedBootstrapResult firstRun = bootstrapper.EnsureSeedImported();
            SqliteAnalysisStore localStore = new(localDatabasePath);
            OpeningSeedBootstrapResult secondRun = bootstrapper.EnsureSeedImported();

            Assert.True(firstRun.SeedFileFound);
            Assert.True(firstRun.Imported);
            Assert.Equal("seed-v1", firstRun.SeedVersion);
            Assert.Equal(tree.Nodes.Count, localStore.GetOpeningTreeSummary().NodeCount);
            Assert.Equal("seed-v1", localStore.GetOpeningSeedVersion());
            Assert.True(secondRun.SeedFileFound);
            Assert.False(secondRun.Imported);
            Assert.Equal("seed-v1", secondRun.SeedVersion);
        }
        finally
        {
            DeleteTempDatabase(localDatabasePath);
            DeleteTempDatabase(bundledSeedPath);
        }
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-store-{Guid.NewGuid():N}.db");
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static MoveAnalysisResult CreateMoveAnalysis(
        int ply,
        int moveNumber,
        GamePhase phase,
        MoveQualityBucket quality,
        int centipawnLoss,
        string label)
    {
        ReplayPly replay = new(
            ply,
            moveNumber,
            PlayerSide.White,
            "Nf3",
            "Nf3",
            "g1f3",
            "fen-before",
            "fen-after",
            "placement-before",
            "placement-after",
            phase,
            "N",
            null,
            "g1",
            "f3",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            new EngineAnalysis("fen-before", [new EngineLine("e2e4", 35, null, ["e2e4", "e7e5"])], "e2e4"),
            new EngineAnalysis("fen-after", [new EngineLine("g1f3", -110, null, ["g1f3", "d7d5"])], "g1f3"),
            35,
            -110,
            null,
            null,
            centipawnLoss,
            quality,
            -20,
            new MistakeTag(label, 0.82, ["late_development", "king_uncastled"]),
            new MoveExplanation("Short", "Hint", "Detailed"));
    }

    [Fact]
    public void OpeningTheoryQueryService_HidesEcoTagsThatContradictTheActualMoveOrder()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            store.SaveOpeningTree(BuildOpeningTree(MismatchedB22Pgn));
            OpeningTheoryQueryService queryService = new(store);

            IReadOnlyList<OpeningLineCatalogItem> lines = queryService.ListOpeningLines(limit: 20);

            Assert.DoesNotContain(lines, line => line.Eco == "B22");
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void OpeningTheoryQueryService_KeepsB22WhenTheMoveOrderIsSicilian()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            store.SaveOpeningTree(BuildOpeningTree(AlapinB22Pgn));
            OpeningTheoryQueryService queryService = new(store);

            OpeningLineCatalogItem line = Assert.Single(queryService.ListOpeningLines(repertoireSide: RepertoireSide.White, limit: 20), item => item.Eco == "B22");
            bool loaded = queryService.TryGetOpeningOverview(line.LineKey, line.RepertoireSide, 4, out OpeningTrainerOverview? overview);

            Assert.True(loaded);
            Assert.NotNull(overview);
            Assert.Equal(["e4", "c5", "c3", "Nf6"], overview!.MainLine.Select(move => move.San).ToArray());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void OpeningTheoryQueryService_HidesB22WhenTheMoveOrderIsOpenSicilian()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            SqliteAnalysisStore store = new(databasePath);
            store.SaveOpeningTree(BuildOpeningTree(OpenSicilianB22Pgn));
            OpeningTheoryQueryService queryService = new(store);

            IReadOnlyList<OpeningLineCatalogItem> lines = queryService.ListOpeningLines(limit: 20);

            Assert.DoesNotContain(lines, line => line.Eco == "B22");
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static OpeningTrainingScheduledAction CreateScheduledAction(
        string id,
        string playerKey,
        DateTime dueUtc,
        OpeningTrainingScheduledActionStatus status)
    {
        return new OpeningTrainingScheduledAction(
            id,
            playerKey,
            "session-1",
            TrainingNextActionKind.RepeatAfterBreak,
            new OpeningLineKey("C20:main"),
            new OpeningBranchKey("branch-1"),
            new OpeningPositionKey("position-1"),
            dueUtc.AddMinutes(-10),
            dueUtc,
            status,
            status == OpeningTrainingScheduledActionStatus.Completed ? dueUtc : null,
            90,
            "repeat-after-break");
    }

    private static OpeningTrainingTelemetryEvent CreateTelemetryEvent(string playerKey, DateTime createdUtc, string marker)
    {
        return new OpeningTrainingTelemetryEvent(
            OpeningTrainingTelemetryEvents.OpeningTrainingStarted,
            createdUtc,
            playerKey,
            new OpeningLineKey("C20:main"),
            new OpeningKey("C20"),
            "session-1",
            "recommendation-1",
            SpecialTrainingModeKind.QuickBlackReview,
            new Dictionary<string, string>
            {
                ["marker"] = marker
            });
    }

    private static MoveAdviceFeedback CreateFeedback(
        GameAnalysisCacheKey key,
        MoveAnalysisResult move,
        AdviceFeedbackKind kind,
        string? correctedLabel,
        string? comment)
    {
        return new MoveAdviceFeedback(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            key.GameFingerprint,
            key.Side,
            key.Depth,
            key.MultiPv,
            key.MoveTimeMs,
            move.Replay.Ply,
            move.Replay.MoveNumber,
            move.Replay.San,
            move.Replay.Uci,
            move.Replay.FenBefore,
            move.Replay.FenAfter,
            move.EvalBeforeCp,
            move.EvalAfterCp,
            move.BeforeAnalysis.BestMoveUci,
            move.MistakeTag?.Label,
            move.MistakeTag?.Confidence,
            move.MistakeTag?.Evidence ?? [],
            move.Quality,
            move.CentipawnLoss,
            kind,
            correctedLabel,
            comment,
            "test");
    }

    private static OpeningTreeBuildResult BuildOpeningTree(params string[] pgns)
    {
        OpeningGameParser parser = new();
        OpeningTreeBuilder builder = new();
        List<OpeningParsedGame> games = new();

        foreach (string pgn in pgns)
        {
            ImportedGame game = PgnGameParser.Parse(pgn);
            games.Add(new OpeningParsedGame(game, parser.Parse(game))
            {
                Metadata = OpeningPgnMetadataParser.Parse(pgn)
            });
        }

        return new OpeningTreePostProcessor().Process(builder.Build(games));
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
