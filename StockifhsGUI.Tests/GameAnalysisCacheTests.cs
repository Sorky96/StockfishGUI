using System.IO;
using System.Text.Json;
using StockifhsGUI;
using Xunit;

namespace StockifhsGUI.Tests;

public sealed class GameAnalysisCacheTests
{
    private const string MiniPgn = """
[Event "Mini"]
[Site "Local"]
[Date "2026.04.17"]
[White "TesterWhite"]
[Black "TesterBlack"]
[Result "0-1"]

1. f3 e5 2. g4 Qh4#
""";

    [Fact]
    public void GameAnalysisCache_StoresAndRetrievesGameResultByKey()
    {
        GameAnalysisCache.Clear();
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        EngineAnalysisOptions options = new();
        GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, options);
        GameAnalysisResult result = new(game, PlayerSide.White, [], [], []);

        GameAnalysisCache.StoreResult(key, result);

        bool found = GameAnalysisCache.TryGetResult(key, out GameAnalysisResult? cached);

        Assert.True(found);
        Assert.Same(result, cached);
    }

    [Fact]
    public void GameAnalysisCache_DistinguishesSideAndOptions()
    {
        GameAnalysisCache.Clear();
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        GameAnalysisResult whiteResult = new(game, PlayerSide.White, [], [], []);
        GameAnalysisResult blackResult = new(game, PlayerSide.Black, [], [], []);

        GameAnalysisCache.StoreResult(GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions()), whiteResult);
        GameAnalysisCache.StoreResult(GameAnalysisCache.CreateKey(game, PlayerSide.Black, new EngineAnalysisOptions(Depth: 16)), blackResult);

        Assert.True(GameAnalysisCache.TryGetResult(GameAnalysisCache.CreateKey(game, PlayerSide.White, new EngineAnalysisOptions()), out GameAnalysisResult? cachedWhite));
        Assert.True(GameAnalysisCache.TryGetResult(GameAnalysisCache.CreateKey(game, PlayerSide.Black, new EngineAnalysisOptions(Depth: 16)), out GameAnalysisResult? cachedBlack));
        Assert.Same(whiteResult, cachedWhite);
        Assert.Same(blackResult, cachedBlack);
        Assert.False(GameAnalysisCache.TryGetResult(GameAnalysisCache.CreateKey(game, PlayerSide.Black, new EngineAnalysisOptions()), out _));
    }

    [Fact]
    public void GameAnalysisCache_StoresAndRestoresWindowStatePerGame()
    {
        GameAnalysisCache.Clear();
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        AnalysisWindowState state = new(PlayerSide.Black, 2, 0);

        GameAnalysisCache.StoreWindowState(game, state);

        bool found = GameAnalysisCache.TryGetWindowState(game, out AnalysisWindowState? restored);

        Assert.True(found);
        Assert.Equal(state, restored);
    }

    [Fact]
    public void GameAnalysisCache_LoadsPersistedResultAfterMemoryClear()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            GameAnalysisCache.OverridePersistentStore(new SqliteAnalysisStore(databasePath));
            GameAnalysisCache.Clear();

            ImportedGame game = PgnGameParser.Parse(MiniPgn);
            EngineAnalysisOptions options = new(Depth: 16, MultiPv: 2);
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, PlayerSide.White, options);
            GameAnalysisResult result = new(
                game,
                PlayerSide.White,
                [],
                [],
                [
                    new SelectedMistake(
                        [],
                        MoveQualityBucket.Blunder,
                        new MistakeTag("opening_principles", 0.71, ["wing_pawn_before_development"]),
                        new MoveExplanation("Short text", "Training hint", "Detailed text"))
                ]);

            GameAnalysisCache.StoreResult(key, result);
            GameAnalysisCache.Clear();

            bool found = GameAnalysisCache.TryGetResult(key, out GameAnalysisResult? restored);

            Assert.True(found);
            Assert.NotNull(restored);
            Assert.Equal(PlayerSide.White, restored!.AnalyzedSide);
            Assert.Equal(game.PgnText, restored.Game.PgnText);
            Assert.Single(restored.HighlightedMistakes);
            Assert.Equal("opening_principles", restored.HighlightedMistakes[0].Tag?.Label);
            Assert.Equal("Detailed text", restored.HighlightedMistakes[0].Explanation.DetailedText);
        }
        finally
        {
            GameAnalysisCache.ResetPersistentStoreOverride();
            GameAnalysisCache.Clear();
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void GameAnalysisCache_LoadsPersistedWindowStateAfterMemoryClear()
    {
        string databasePath = CreateTempDatabasePath();

        try
        {
            GameAnalysisCache.OverridePersistentStore(new SqliteAnalysisStore(databasePath));
            GameAnalysisCache.Clear();

            ImportedGame game = PgnGameParser.Parse(MiniPgn);
            AnalysisWindowState state = new(PlayerSide.Black, 1, 2);

            GameAnalysisCache.StoreWindowState(game, state);
            GameAnalysisCache.Clear();

            bool found = GameAnalysisCache.TryGetWindowState(game, out AnalysisWindowState? restored);

            Assert.True(found);
            Assert.Equal(state, restored);
        }
        finally
        {
            GameAnalysisCache.ResetPersistentStoreOverride();
            GameAnalysisCache.Clear();
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void MoveExplanation_DeserializesOlderPayloadWithoutDetailedText()
    {
        MoveExplanation? explanation = JsonSerializer.Deserialize<MoveExplanation>(
            """{"ShortText":"Short text","TrainingHint":"Training hint"}""");

        Assert.NotNull(explanation);
        Assert.Equal("Short text", explanation!.ShortText);
        Assert.Equal("Training hint", explanation.TrainingHint);
        Assert.Equal(string.Empty, explanation.DetailedText);
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"stockifhsgui-analysis-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
