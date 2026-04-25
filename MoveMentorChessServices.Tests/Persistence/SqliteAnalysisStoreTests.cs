using System.IO;
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
