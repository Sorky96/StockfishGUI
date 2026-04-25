using System.IO;
using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningImportServiceTests
{
    private const string LongGamePgn = """
[Event "Long"]
[Site "Test"]
[Date "2026.04.23"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "D02"]
[Opening "Queen's Pawn Game"]

1. d4 d5 2. Nf3 Nf6 3. Bf4 e6 4. e3 Be7 5. Bd3 O-O 6. O-O c5 7. c3 Nc6 8. Nbd2 b6 9. Ne5 Bb7 10. Qf3 Rc8 11. Qh3 *
""";

    private const string ShortGamePgn = """
[Event "Short"]
[Site "Test"]
[Date "2026.04.23"]
[White "Gamma"]
[Black "Delta"]
[Result "1-0"]
[ECO "C20"]

1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0
""";

    private const string ItalianSetupPgn = """
[Event "Shared E4"]
[Site "Test"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]

1. e4 e5 2. Nf3 Nc6 *
""";

    private const string BishopSetupPgn = """
[Event "Shared E4 Alternative"]
[Site "Test"]
[White "Gamma"]
[Black "Delta"]
[Result "*"]

1. e4 e5 2. Bc4 Nc6 *
""";

    private const string QueenPawnSetupPgn = """
[Event "Queen Pawn"]
[Site "Test"]
[White "Eta"]
[Black "Theta"]
[Result "*"]

1. d4 d5 2. Nf3 Nf6 *
""";

    private const string TaggedKingPawnPgn = """
[Event "Tagged E4"]
[Site "Test"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]
[ECO "C20"]
[Opening "King's Pawn Game"]
[Variation "Main Line"]

1. e4 e5 2. Nf3 Nc6 *
""";

    private const string TaggedKingPawnSecondPgn = """
[Event "Tagged E4 Again"]
[Site "Test"]
[White "Gamma"]
[Black "Delta"]
[Result "*"]
[ECO "C20"]
[Opening "King's Pawn Game"]
[Variation "Main Line"]

1. e4 e5 2. Bc4 Nc6 *
""";

    private const string TaggedQueenPawnPgn = """
[Event "Tagged D4"]
[Site "Test"]
[White "Eta"]
[Black "Theta"]
[Result "*"]
[ECO "D00"]
[Opening "Queen's Pawn Game"]
[Variation "Main Line"]

1. d4 d5 2. Nf3 Nf6 *
""";

    private const string InvalidSanPgn = """
[Event "Broken"]
[Site "Test"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]

1. e4 e5 2. Be6+ *
""";

    private const string PlusInsteadOfMatePgn = """
[Event "New Orleans"]
[Site "New Orleans"]
[Date "1855.??.??"]
[Round "?"]
[White "Meek, Alexander Beaufort"]
[Black "NN"]
[Result "1-0"]
[ECO "C36"]

1.e4 e5 2.f4 exf4 3.Nf3 d5 4.Nc3 dxe4 5.Nxe4 Bg4 6.Qe2 Bxf3 7.Nf6+ 1-0
""";

    private const string HunChampionshipKe7Pgn = """
[Event "HUN-chT1"]
[Site "HUN"]
[Date "1995.??.??"]
[Round "?"]
[White "Kovacs, Gabor"]
[Black "Nemeth, Zoltan"]
[Result "1-0"]
[WhiteElo ""]
[BlackElo "2380"]
[ECO "C23"]

1.e4 e5 2.Bc4 Nc6 3.Nc3 Bc5 4.d3 d6 5.Na4 Na5 6.Nxc5 Nxc4 7.dxc4 dxc5 8.Qxd8+ Kxd8
9.Be3 b6 10.O-O-O+ Ke7 11.Ne2 Be6 12.b3 Nf6 13.f3 Rhd8 14.Nc3 c6 15.a4 a5
16.Rd2 Rxd2 17.Kxd2 Nd7 18.Nd1 h5 19.Nf2 g6 20.Re1 f6 21.h4 Rg8 22.Nd3 g5
23.hxg5 fxg5 24.Rh1 Bf7 25.Ke2 Kf6 26.Bd2 Bg6 27.Bc3 Ke6 28.Ke3 Re8 29.g3 Kd6
30.Rd1 Kc7 31.Nf2 Nf6 32.Nd3 Nd7 33.Nf2 Nf6 34.Rg1 g4 35.fxg4 hxg4 36.Rh1 Kd6
37.Rh4 Ke6 38.Bb2 Re7 39.Nxg4 Nxg4+ 40.Rxg4 Kf6 41.Rf4+ Ke6 42.Rf8 Re8 43.Rf2 Rh8
44.Bc3 Rh3 45.Kf3 Rh1 46.Bd2 Bh5+ 47.Kg2 Rb1 48.Bg5 Re1 49.Bd8 Rxe4 50.Bxb6 Re2
51.Bxa5 Rxf2+ 52.Kxf2 Bd1 53.Bb6 Bxc2 54.a5 Kd7 55.Ke3 Bxb3 56.Kd3 e4+ 57.Kc3 Bd1
58.Bxc5 Bg4 59.Be3 Kc7 60.Kd4 Be6 61.c5 Bf5 62.Ke5 Bh7 63.g4 Kb7 64.Kd6 Bg6
65.a6+ Kxa6 66.Kxc6 Be8+ 67.Kd6 Kb5 68.g5 1-0
""";

    private const string IndentedEventShortGamePgn = """
   [Event "Indented"]
   [Site "Test"]
   [Date "2026.04.23"]
   [White "Indented White"]
   [Black "Indented Black"]
   [Result "1-0"]
   [ECO "C20"]

   1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0
""";

    [Fact]
    public void OpeningGameParser_LimitsImportToTenFullMoves()
    {
        ImportedGame game = PgnGameParser.Parse(LongGamePgn);
        OpeningGameParser parser = new();

        IReadOnlyList<OpeningImportPly> plies = parser.Parse(game);

        Assert.Equal(20, plies.Count);
        Assert.Equal(10, plies[^1].MoveNumber);
        Assert.Equal("Black", plies[^1].Side);
        Assert.Equal("a8c8", plies[^1].MoveUci);
    }

    [Fact]
    public void OpeningGameParser_BuildsPositionKeysWithoutMoveCounters()
    {
        ImportedGame game = PgnGameParser.Parse("1. e4 e5");
        OpeningGameParser parser = new();

        IReadOnlyList<OpeningImportPly> plies = parser.Parse(game);

        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", plies[0].PositionKeyBefore);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3", plies[0].PositionKeyAfter);
        Assert.False(plies[0].PositionKeyAfter.EndsWith(" 0 1", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningPgnImportService_ImportsFolderAndSavesGamesToExistingStore()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "a.pgn"), LongGamePgn + Environment.NewLine + ShortGamePgn);
            File.WriteAllText(Path.Combine(folder, "b.pgn"), ShortGamePgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store);
            List<OpeningPgnImportProgress> progress = new();

            OpeningPgnImportResult result = service.ImportFolder(folder, progress: progress.Add);

            Assert.Equal(2, result.FilesProcessed);
            Assert.Equal(3, result.GamesProcessed);
            Assert.Equal(0, result.SkippedGames);
            Assert.Equal(34, result.PliesParsed);
            Assert.Equal(3, store.SavedGames.Count);
            Assert.Equal(3, result.ParsedGames.Count);
            Assert.NotEmpty(result.Tree.Nodes);
            Assert.NotEmpty(result.Tree.Edges);
            Assert.NotEmpty(progress);
            Assert.Equal(3, progress[^1].TotalGamesProcessed);
            Assert.Equal(0, progress[^1].SkippedGames);
            Assert.Equal(34, progress[^1].TotalPliesParsed);
            Assert.Equal(result.Tree.Nodes.Count, progress[^1].NodeCount);
            Assert.Equal(result.Tree.Edges.Count, progress[^1].EdgeCount);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void OpeningTreeBuilder_DeduplicatesPositionsAndEdges()
    {
        OpeningGameParser parser = new();
        OpeningParsedGame firstGame = ParseOpeningGame(ItalianSetupPgn, parser);
        OpeningParsedGame secondGame = ParseOpeningGame(BishopSetupPgn, parser);
        OpeningTreeBuilder builder = new();

        OpeningTreeBuildResult tree = builder.Build([firstGame, secondGame]);

        OpeningPositionNode root = Assert.Single(
            tree.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");
        OpeningPositionNode afterE4 = Assert.Single(
            tree.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3");
        OpeningMoveEdge e4 = Assert.Single(
            tree.Edges,
            edge => edge.FromNodeId == root.Id && edge.ToNodeId == afterE4.Id && edge.MoveUci == "e2e4");

        Assert.Equal(7, tree.Nodes.Count);
        Assert.Equal(6, tree.Edges.Count);
        Assert.Equal(2, root.OccurrenceCount);
        Assert.Equal(2, root.DistinctGameCount);
        Assert.Equal(2, afterE4.OccurrenceCount);
        Assert.Equal(2, afterE4.DistinctGameCount);
        Assert.Equal(2, e4.OccurrenceCount);
        Assert.Equal(2, e4.DistinctGameCount);
        Assert.Equal("e4", e4.MoveSan);
    }

    [Fact]
    public void OpeningTreeBuilder_CountsRepeatedPositionOccurrencesSeparatelyFromDistinctGames()
    {
        const string repetitionPgn = """
[Event "Repeat"]
[Site "Test"]
[White "Alpha"]
[Black "Beta"]
[Result "*"]

1. Nf3 Nf6 2. Ng1 Ng8 *
""";
        OpeningGameParser parser = new();
        OpeningTreeBuilder builder = new();

        OpeningTreeBuildResult tree = builder.Build([ParseOpeningGame(repetitionPgn, parser)]);

        OpeningPositionNode root = Assert.Single(
            tree.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");

        Assert.Equal(2, root.OccurrenceCount);
        Assert.Equal(1, root.DistinctGameCount);
    }

    [Fact]
    public void OpeningTreePostProcessor_RanksMainAndPlayableMovesWithinPosition()
    {
        OpeningGameParser parser = new();
        OpeningTreeBuilder builder = new();
        OpeningTreePostProcessor postProcessor = new();

        OpeningTreeBuildResult tree = postProcessor.Process(builder.Build([
            ParseOpeningGame(ItalianSetupPgn, parser),
            ParseOpeningGame(BishopSetupPgn, parser),
            ParseOpeningGame(QueenPawnSetupPgn, parser)
        ]));
        OpeningPositionNode root = Assert.Single(
            tree.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");
        OpeningMoveEdge e4 = Assert.Single(tree.Edges, edge => edge.FromNodeId == root.Id && edge.MoveUci == "e2e4");
        OpeningMoveEdge d4 = Assert.Single(tree.Edges, edge => edge.FromNodeId == root.Id && edge.MoveUci == "d2d4");

        Assert.True(e4.IsMainMove);
        Assert.True(e4.IsPlayableMove);
        Assert.Equal(1, e4.RankWithinPosition);
        Assert.False(d4.IsMainMove);
        Assert.False(d4.IsPlayableMove);
        Assert.Equal(2, d4.RankWithinPosition);
    }

    [Fact]
    public void OpeningTreeBuilder_ChoosesMostFrequentPgnMetadataTagForNode()
    {
        OpeningGameParser parser = new();
        OpeningTreeBuilder builder = new();

        OpeningTreeBuildResult tree = builder.Build([
            ParseOpeningGameWithMetadata(TaggedKingPawnPgn, parser),
            ParseOpeningGameWithMetadata(TaggedKingPawnSecondPgn, parser),
            ParseOpeningGameWithMetadata(TaggedQueenPawnPgn, parser)
        ]);
        OpeningPositionNode root = Assert.Single(
            tree.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");
        OpeningNodeTag tag = Assert.Single(tree.Tags, tag => tag.NodeId == root.Id);

        Assert.Equal("C20", tag.Eco);
        Assert.Equal("King's Pawn Game", tag.OpeningName);
        Assert.Equal("Main Line", tag.VariationName);
        Assert.Equal("pgn", tag.SourceKind);
    }

    [Fact]
    public void OpeningTreePruner_KeepsProductionMovesAndRemovesRareBranches()
    {
        OpeningGameParser parser = new();
        OpeningTreeBuilder builder = new();
        OpeningTreeBuildResult tree = new OpeningTreePostProcessor().Process(builder.Build([
            ParseOpeningGame(ItalianSetupPgn, parser),
            ParseOpeningGame(BishopSetupPgn, parser),
            ParseOpeningGame(TaggedQueenPawnPgn, parser)
        ]));
        OpeningTreePruningOptions options = new(
            MinDistinctGames: 2,
            MaxMovesPerPosition: 1,
            MinMoveShare: 0.50,
            AlwaysKeepMainMove: true);

        OpeningTreeBuildResult pruned = new OpeningTreePruner().Prune(tree, options);

        OpeningPositionNode root = Assert.Single(
            pruned.Nodes,
            node => node.PositionKey == "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -");
        Assert.Contains(pruned.Edges, edge => edge.FromNodeId == root.Id && edge.MoveUci == "e2e4");
        Assert.DoesNotContain(pruned.Edges, edge => edge.FromNodeId == root.Id && edge.MoveUci == "d2d4");
        Assert.All(pruned.Edges, edge => Assert.Contains(pruned.Nodes, node => node.Id == edge.FromNodeId));
        Assert.All(pruned.Edges, edge => Assert.Contains(pruned.Nodes, node => node.Id == edge.ToNodeId));
        Assert.True(pruned.Nodes.Count < tree.Nodes.Count);
        Assert.True(pruned.Edges.Count < tree.Edges.Count);
    }

    [Fact]
    public void OpeningPgnImportService_SkipsInvalidGamesInsteadOfStoppingImport()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "mixed.pgn"),
                ItalianSetupPgn + Environment.NewLine + InvalidSanPgn + Environment.NewLine + BishopSetupPgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store, retainParsedGames: false);

            OpeningPgnImportResult result = service.ImportFolder(folder);

            Assert.Equal(2, result.GamesProcessed);
            Assert.Equal(1, result.SkippedGames);
            Assert.Empty(result.ParsedGames);
            Assert.Equal(2, store.SavedGames.Count);
            Assert.True(result.Tree.Nodes.Count > 0);
            Assert.True(result.Tree.Edges.Count > 0);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void OpeningPgnImportService_SplitsGamesWhenNextEventHeaderIsIndented()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "indented.pgn"),
                ItalianSetupPgn + Environment.NewLine + IndentedEventShortGamePgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store, retainParsedGames: false);

            OpeningPgnImportResult result = service.ImportFolder(folder);

            Assert.Equal(2, result.GamesProcessed);
            Assert.Equal(0, result.SkippedGames);
            Assert.Equal(2, store.SavedGames.Count);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void OpeningPgnImportService_LogsExactGameOrdinalAndHeadersForInvalidGame()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        TextWriter originalError = Console.Error;
        StringWriter errorWriter = new();

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "mixed.pgn"),
                ItalianSetupPgn + Environment.NewLine + InvalidSanPgn + Environment.NewLine + BishopSetupPgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store, retainParsedGames: false);
            Console.SetError(errorWriter);

            OpeningPgnImportResult result = service.ImportFolder(folder);

            string log = errorWriter.ToString();
            Assert.Equal(2, result.GamesProcessed);
            Assert.Equal(1, result.SkippedGames);
            Assert.Contains("game #2", log, StringComparison.Ordinal);
            Assert.Contains("Event=\"Broken\"", log, StringComparison.Ordinal);
            Assert.Contains("White=\"Alpha\"", log, StringComparison.Ordinal);
            Assert.Contains("Black=\"Beta\"", log, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void OpeningPgnImportService_AcceptsPlusSuffixWhenEngineEvaluatesMate()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-plusmate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "plusmate.pgn"), PlusInsteadOfMatePgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store, retainParsedGames: true);

            OpeningPgnImportResult result = service.ImportFolder(folder);

            Assert.Equal(1, result.GamesProcessed);
            Assert.Equal(0, result.SkippedGames);
            Assert.Single(store.SavedGames);
            Assert.Single(result.ParsedGames);
            Assert.Equal("Nf6+", result.ParsedGames[0].Plies[^1].MoveSan);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void OpeningPgnImportService_AcceptsKe7InHunChampionshipGame()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-opening-ke7-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "hun.pgn"), HunChampionshipKe7Pgn);
            FakeAnalysisStore store = new();
            OpeningPgnImportService service = new(store, retainParsedGames: true);

            OpeningPgnImportResult result = service.ImportFolder(folder);

            Assert.Equal(1, result.GamesProcessed);
            Assert.Equal(0, result.SkippedGames);
            Assert.Single(store.SavedGames);
            Assert.Single(result.ParsedGames);
            Assert.Contains(result.ParsedGames[0].Plies, ply => ply.MoveSan == "Ke7");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void PgnGameParser_ParsesKe7TokenInHunChampionshipGame()
    {
        ImportedGame game = PgnGameParser.Parse(HunChampionshipKe7Pgn);

        Assert.Contains("Ke7", game.SanMoves);
        Assert.Contains("O-O-O+", game.SanMoves);
    }

    private static OpeningParsedGame ParseOpeningGame(string pgn, OpeningGameParser parser)
    {
        ImportedGame game = PgnGameParser.Parse(pgn);
        return new OpeningParsedGame(game, parser.Parse(game));
    }

    private static OpeningParsedGame ParseOpeningGameWithMetadata(string pgn, OpeningGameParser parser)
    {
        ImportedGame game = PgnGameParser.Parse(pgn);
        return new OpeningParsedGame(game, parser.Parse(game))
        {
            Metadata = OpeningPgnMetadataParser.Parse(pgn)
        };
    }

    private sealed class FakeAnalysisStore : IAnalysisStore
    {
        public List<ImportedGame> SavedGames { get; } = new();

        public void SaveImportedGame(ImportedGame game) => SavedGames.Add(game);
        public void SaveImportedGames(IReadOnlyList<ImportedGame> games) => SavedGames.AddRange(games);
        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
        {
            game = null;
            return false;
        }

        public bool DeleteImportedGame(string gameFingerprint) => false;
        public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200) => [];
        public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500) => [];
        public IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000) => [];
        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result)
        {
            result = null;
            return false;
        }

        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result)
        {
        }

        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state)
        {
            state = null;
            return false;
        }

        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state)
        {
        }
    }
}
