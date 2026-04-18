using System.IO;
using StockifhsGUI;
using Xunit;

namespace StockifhsGUI.Tests;

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
            Assert.Equal(GameFingerprint.Compute(game.PgnText), storedMove.GameFingerprint);
            Assert.Equal(PlayerSide.White, storedMove.AnalyzedSide);
            Assert.Equal(16, storedMove.Depth);
            Assert.Equal(2, storedMove.MultiPv);
            Assert.Equal(3, storedMove.Ply);
            Assert.Equal("Nf3", storedMove.San);
            Assert.Equal("g1f3", storedMove.Uci);
            Assert.Equal("opening_principles", storedMove.MistakeLabel);
            Assert.Equal(145, storedMove.CentipawnLoss);
            Assert.True(storedMove.IsHighlighted);
            Assert.Equal("Short", storedMove.ShortExplanation);
            Assert.Equal("Detailed", storedMove.DetailedExplanation);
            Assert.Equal("Hint", storedMove.TrainingHint);
            Assert.Contains("late_development", storedMove.Evidence);
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

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"stockifhsgui-store-{Guid.NewGuid():N}.db");
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

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
