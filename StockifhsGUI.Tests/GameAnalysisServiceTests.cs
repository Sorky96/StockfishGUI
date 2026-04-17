using System.IO;
using StockifhsGUI;
using Xunit;

namespace StockifhsGUI.Tests;

public sealed class GameAnalysisServiceTests
{
    private const string MiniPgn = """
[Event "Mini"]
[Site "Local"]
[Date "2026.04.17"]
[White "TesterWhite"]
[Black "TesterBlack"]
[Result "0-1"]
[ECO "A00"]

1. f3 e5 2. g4 Qh4#
""";

    private const string FullGamePgn = """
[Event "Live Chess"]
[Site "Chess.com"]
[Date "2026.04.14"]
[Round "-"]
[White "Anandh4497"]
[Black "Sorky1996"]
[Result "1-0"]
[CurrentPosition "8/1R6/8/8/8/8/kQ5K/8 b - - 1 62"]
[Timezone "UTC"]
[ECO "A00"]
[UTCDate "2026.04.14"]
[UTCTime "14:23:07"]
[WhiteElo "703"]
[BlackElo "666"]
[TimeControl "600"]
[Termination "Anandh4497 won by checkmate"]

1. e3 g5 2. Qf3 g4 3. Qxg4 d5 4. Qf3 Nf6 5. h3 Nc6 6. c3 Bh6 7. Bb5
Bd7 8. Bxc6 Bxc6 9. Ne2 d4 10. Qg3 dxe3 11. dxe3 Ne4 12. Qg4 Ba4 13. b3
Bc6 14. O-O Qd6 15. Nd4 Nf6 16. Ba3 Qe5 17. Qg3 Qh5 18. Nxc6 Rg8 19.
Qf3 Qb5 20. Nd4 Qd3 21. Nf5 Bf8 22. Qxb7 Rd8 23. Qc6+ Nd7 24. Ng3 Rxg3 25.
fxg3 Qxe3+ 26. Rf2 Qe1+ 27. Rf1 Qxg3 28. Bc5 Bh6 29. Bf2 Qe5 30. Qxh6 Nf6 31.
Na3 Ne4 32. Rae1 Qf5 33. Nc2 Nd2 34. Re2 Nxf1 35. Kxf1 Rd1+ 36. Re1 Qd3+ 37.
Kg1 Rd2 38. Qe3 Qd6 39. Bg3 Rxg2+ 40. Kxg2 Qd5+ 41. Qf3 Qd2+ 42. Re2 Qd7 43.
Qg4 Qd5+ 44. Kh2 e6 45. Qg8+ Ke7 46. Bh4+ f6 47. Qg7+ Kd8 48. Qxf6+ Kc8 49.
Qd8+ Qxd8 50. Bxd8 Kxd8 51. Rxe6 Kd7 52. Rh6 c6 53. Rxh7+ Kd6 54. Rxa7 c5 55. h4
Kd5 56. h5 c4 57. bxc4+ Kxc4 58. h6 Kxc3 59. h7 Kxc2 60. h8=Q Kb1 61. Rb7+
Kxa2 62. Qb2# 1-0
""";

    [Fact]
    public void PgnGameParser_ParsesHeadersAndMoves()
    {
        ImportedGame game = PgnGameParser.Parse(MiniPgn);

        Assert.Equal("TesterWhite", game.WhitePlayer);
        Assert.Equal("TesterBlack", game.BlackPlayer);
        Assert.Equal("2026.04.17", game.DateText);
        Assert.Equal("0-1", game.Result);
        Assert.Equal("A00", game.Eco);
        Assert.Equal(4, game.SanMoves.Count);
        Assert.Equal("Qh4#", game.SanMoves[^1]);
    }

    [Fact]
    public void GameReplayService_ReplaysMovesWithSnapshots()
    {
        ImportedGame game = PgnGameParser.Parse("1. e4 e5 2. Nf3 Nc6");
        GameReplayService replayService = new();

        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        Assert.Equal(4, replay.Count);
        Assert.Equal("e2e4", replay[0].Uci);
        Assert.Equal(1, replay[0].MoveNumber);
        Assert.Equal(PlayerSide.White, replay[0].Side);
        Assert.Equal(1, replay[1].MoveNumber);
        Assert.Equal(PlayerSide.Black, replay[1].Side);
        Assert.Equal(2, replay[2].MoveNumber);
        Assert.Equal(PlayerSide.White, replay[2].Side);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", replay[0].FenBefore);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", replay[0].FenAfter);
        Assert.Equal(GamePhase.Opening, replay[0].Phase);
    }

    [Fact]
    public void GameReplayService_AssignsOpeningAndEndgamePhases()
    {
        ImportedGame game = PgnGameParser.Parse(FullGamePgn);
        GameReplayService replayService = new();

        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        Assert.Equal(GamePhase.Opening, replay.First().Phase);
        Assert.Equal(GamePhase.Endgame, replay.Last().Phase);
    }

    [Fact]
    public void MistakeClassifier_AssignsMaterialLoss()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "Q",
            "d1",
            "d5",
            "4k3/8/8/8/8/8/8/3QK3 w - - 0 1",
            "4k3/8/8/3Q4/8/8/8/4K3 b - - 1 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Blunder, 320, -900);

        Assert.NotNull(tag);
        Assert.Equal("material_loss", tag!.Label);
    }

    [Fact]
    public void MistakeClassifier_AssignsHangingPiece()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "g3",
            "e4",
            "4k3/8/8/8/8/6N1/8/4K3 w - - 0 1",
            "4k3/8/8/3p4/4N3/8/8/4K3 b - - 0 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Blunder, 240, 0);

        Assert.NotNull(tag);
        Assert.Equal("hanging_piece", tag!.Label);
    }

    [Fact]
    public void MistakeClassifier_AssignsMissedTactic()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "B",
            "c4",
            "d5",
            "4k3/8/8/8/2B5/8/8/4K3 w - - 0 1",
            "4k3/8/8/3B4/8/8/8/4K3 b - - 1 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 180, 0);

        Assert.NotNull(tag);
        Assert.Equal("missed_tactic", tag!.Label);
    }

    [Fact]
    public void MistakeClassifier_AssignsKingSafety()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "P",
            "g2",
            "g4",
            "6k1/8/8/8/8/8/6PP/6K1 w - - 0 1",
            "6k1/8/8/8/6P1/8/7P/6K1 b - - 0 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 180, 0);

        Assert.NotNull(tag);
        Assert.Equal("king_safety", tag!.Label);
    }

    [Fact]
    public void MistakeClassifier_AssignsOpeningPrinciples()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "4k3/8/8/8/8/8/8/3QK3 w - - 0 1",
            "4k3/8/8/7Q/8/8/8/4K3 b - - 1 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Inaccuracy, 120, 0);

        Assert.NotNull(tag);
        Assert.Equal("opening_principles", tag!.Label);
    }

    [Fact]
    public void MistakeClassifier_AssignsEndgameTechnique()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Endgame,
            "K",
            "e2",
            "e3",
            "4k3/8/8/8/8/8/4K3/8 w - - 0 1",
            "4k3/8/8/8/8/4K3/8/8 b - - 1 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 160, 0);

        Assert.NotNull(tag);
        Assert.Equal("endgame_technique", tag!.Label);
    }

    [Fact]
    public void MistakeSelector_MergesAdjacentMistakesWithSameLabel()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Mistake, "missed_tactic", 210),
            CreateMoveAnalysis(3, 2, MoveQualityBucket.Mistake, "missed_tactic", 180),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "opening_principles", 100)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(2, selected.Count);
        Assert.Equal(2, selected[0].Moves.Count);
        Assert.Equal("missed_tactic", selected[0].Tag!.Label);
    }

    [Fact]
    public void GameAnalysisService_AnalyzesGameWithFakeEngine()
    {
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        ReplayPly whiteFirst = replay[0];
        ReplayPly whiteSecond = replay[2];

        FakeEngineAnalyzer fakeEngine = new(new Dictionary<string, EngineAnalysis>
        {
            [whiteFirst.FenBefore] = AnalysisFor(whiteFirst.FenBefore, "e2e4", 50, null, "e2e4", "e7e5"),
            [whiteFirst.FenAfter] = AnalysisFor(whiteFirst.FenAfter, "e7e5", 120, null, "e7e5", "g1f3"),
            [whiteSecond.FenBefore] = AnalysisFor(whiteSecond.FenBefore, "g2g3", 30, null, "g2g3", "d7d5"),
            [whiteSecond.FenAfter] = AnalysisFor(whiteSecond.FenAfter, "d8h4", null, 1, "d8h4")
        });

        GameAnalysisService service = new(fakeEngine, replayService);
        GameAnalysisResult result = service.AnalyzeGame(game, PlayerSide.White, new EngineAnalysisOptions());

        Assert.Equal(2, result.MoveAnalyses.Count);
        Assert.Equal(MoveQualityBucket.Mistake, result.MoveAnalyses[0].Quality);
        Assert.Equal(MoveQualityBucket.Blunder, result.MoveAnalyses[1].Quality);
        Assert.Single(result.HighlightedMistakes);
        Assert.Equal(2, result.HighlightedMistakes[0].Moves.Count);
        Assert.All(result.HighlightedMistakes, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation.ShortText)));
    }

    [Fact]
    public void PgnImportAndAnalysis_EndToEndProducesOrderedHighlightedMistakes()
    {
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        ReplayPly whiteFirst = replay[0];
        ReplayPly whiteSecond = replay[2];

        FakeEngineAnalyzer fakeEngine = new(new Dictionary<string, EngineAnalysis>
        {
            [whiteFirst.FenBefore] = AnalysisFor(whiteFirst.FenBefore, "e2e4", 40, null, "e2e4"),
            [whiteFirst.FenAfter] = AnalysisFor(whiteFirst.FenAfter, "e7e5", 110, null, "e7e5"),
            [whiteSecond.FenBefore] = AnalysisFor(whiteSecond.FenBefore, "g2g3", 20, null, "g2g3"),
            [whiteSecond.FenAfter] = AnalysisFor(whiteSecond.FenAfter, "d8h4", null, 1, "d8h4")
        });

        GameAnalysisService service = new(fakeEngine, replayService);
        GameAnalysisResult result = service.AnalyzeGame(game, PlayerSide.White, new EngineAnalysisOptions());

        Assert.Single(result.HighlightedMistakes);
        Assert.Equal(2, result.HighlightedMistakes[0].Moves.Count);
        Assert.Contains("stronger option", result.HighlightedMistakes[0].Explanation.ShortText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StockfishEngine_SmokeTest_IfBinaryExists()
    {
        string? stockfishPath = TryFindStockfishExe();
        if (stockfishPath is null)
        {
            return;
        }

        using StockfishEngine engine = new(stockfishPath);
        EngineAnalysis analysis = engine.AnalyzePosition(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            new EngineAnalysisOptions(Depth: 8, MultiPv: 2));

        Assert.NotEmpty(analysis.Lines);
        Assert.False(string.IsNullOrWhiteSpace(analysis.BestMoveUci));
    }

    private static ReplayPly CreateReplay(
        GamePhase phase,
        string movingPiece,
        string fromSquare,
        string toSquare,
        string fenBefore,
        string fenAfter)
    {
        return new ReplayPly(
            1,
            1,
            PlayerSide.White,
            "test",
            "test",
            $"{fromSquare}{toSquare}",
            fenBefore,
            fenAfter,
            string.Empty,
            string.Empty,
            phase,
            movingPiece,
            null,
            fromSquare,
            toSquare,
            false,
            false,
            false);
    }

    private static MoveAnalysisResult CreateMoveAnalysis(int ply, int moveNumber, MoveQualityBucket quality, string label, int cpl)
    {
        ReplayPly replay = new(
            ply,
            moveNumber,
            PlayerSide.White,
            "move",
            "move",
            "a2a3",
            "4k3/8/8/8/8/8/P7/4K3 w - - 0 1",
            "4k3/8/8/8/8/P7/8/4K3 b - - 0 1",
            string.Empty,
            string.Empty,
            GamePhase.Middlegame,
            "P",
            null,
            "a2",
            "a3",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            AnalysisFor(replay.FenBefore, "a2a3", 0, null, "a2a3"),
            AnalysisFor(replay.FenAfter, "a7a6", 0, null, "a7a6"),
            0,
            -cpl,
            null,
            null,
            cpl,
            quality,
            0,
            new MistakeTag(label, 0.8, ["evidence"]),
            new MoveExplanation("Short explanation", "Training hint"));
    }

    private static EngineAnalysis AnalysisFor(string fen, string bestMove, int? centipawns, int? mateIn, params string[] pv)
    {
        string[] principalVariation = pv.Length == 0 ? [bestMove] : pv;
        return new EngineAnalysis(
            fen,
            [new EngineLine(bestMove, centipawns, mateIn, principalVariation)],
            bestMove);
    }

    private static string? TryFindStockfishExe()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "stockfish.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "StockifhsGUI", "bin", "Debug", "net8.0-windows", "stockfish.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "StockifhsGUI.Tests", "bin", "Debug", "net8.0-windows", "stockfish.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class FakeEngineAnalyzer : IEngineAnalyzer
    {
        private readonly IReadOnlyDictionary<string, EngineAnalysis> analysesByFen;

        public FakeEngineAnalyzer(IReadOnlyDictionary<string, EngineAnalysis> analysesByFen)
        {
            this.analysesByFen = analysesByFen;
        }

        public EngineAnalysis AnalyzePosition(string fen, EngineAnalysisOptions options)
        {
            if (analysesByFen.TryGetValue(fen, out EngineAnalysis? analysis))
            {
                return analysis;
            }

            throw new InvalidOperationException($"No fake analysis configured for FEN: {fen}");
        }
    }
}
