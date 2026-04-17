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

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"stockifhsgui-store-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
