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
    public void MistakeClassifier_PrefersMissedTactic_WhenPieceIsOnlyUnderPressure()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "f3",
            "e5",
            "4k3/8/8/8/8/5N2/8/4K3 w - - 0 1",
            "4k3/8/8/4N3/8/8/8/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            MovedPieceHangingAfterMove: true,
            MovedPieceFreeToTake: false,
            MovedPieceLikelyLosesExchange: false,
            MovedPieceAttackDeficit: 1,
            MovedPieceValueCp: 320,
            BestMoveIsCapture: true,
            BestMoveMaterialSwingCp: 250);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 180, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("missed_tactic", tag!.Label);
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
    public void MistakeClassifier_AssignsMissedTactic_WhenBestMoveWinsMaterial()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "f3",
            "g5",
            "4k3/8/8/8/8/5N2/8/4K3 w - - 0 1",
            "4k3/8/8/6N1/8/8/8/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            BestMoveIsCapture: true,
            BestMoveMaterialSwingCp: 300);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 170, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("missed_tactic", tag!.Label);
        Assert.Contains("best_move_material_swing_300", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsMaterialLoss_WhenPlayedLineLosesMaterial()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "c3",
            "d5",
            "4k3/8/8/8/8/2N5/8/4K3 w - - 0 1",
            "4k3/8/8/3N4/8/8/8/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            PlayedLineMaterialSwingCp: -320);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 190, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("material_loss", tag!.Label);
        Assert.Contains("played_line_material_swing_-320", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_PrefersMissedTactic_WhenTacticalOpportunityOutweighsSmallMaterialLoss()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "B",
            "c4",
            "b5",
            "4k3/8/8/8/2B5/8/8/4K3 w - - 0 1",
            "4k3/8/8/1B6/8/8/8/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            BestMoveIsCapture: true,
            BestMoveMaterialSwingCp: 900,
            PlayedLineMaterialSwingCp: -300);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Blunder, 260, -300, context);

        Assert.NotNull(tag);
        Assert.Equal("missed_tactic", tag!.Label);
        Assert.Contains("tactical_opportunity_outweighed_small_loss", tag.Evidence);
        Assert.Contains("best_move_material_swing_900", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_PrefersHangingPiece_WhenMovedPieceIsDirectlyHungAndLost()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "R",
            "e1",
            "e4",
            "4k3/8/8/8/8/8/8/4R1K1 w - - 0 1",
            "4k3/8/8/8/4R3/8/8/6K1 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            MovedPieceHangingAfterMove: true,
            MovedPieceFreeToTake: true,
            MovedPieceLikelyLosesExchange: true,
            MovedPieceAttackDeficit: 2,
            MovedPieceValueCp: 500,
            PlayedLineMaterialSwingCp: -500);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Blunder, 280, -500, context);

        Assert.NotNull(tag);
        Assert.Equal("hanging_piece", tag!.Label);
        Assert.Contains("moved_piece_free_to_take", tag.Evidence);
        Assert.Contains("material_delta_-500", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsOpeningPrinciples_WhenWingPawnMoveSkipsDevelopment()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "P",
            "h2",
            "h4",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/8/7P/8/PPPPPPP1/RNBQKBNR b KQkq - 0 1");

        MoveHeuristicContext context = CreateContext(
            EdgePawnPush: true,
            DevelopedMinorPiecesBefore: 0,
            DevelopedMinorPiecesAfter: 0);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Inaccuracy, 100, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("opening_principles", tag!.Label);
        Assert.Contains("wing_pawn_before_development", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsOpeningPrinciples_WhenBestMoveWasCastle()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "e2",
            "r1bqk2r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPPQPPP/RNB2RK1 w kq - 0 1",
            "r1bqk2r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPPQPPP/RNB2RK1 b kq - 1 1");

        MoveHeuristicContext context = CreateContext(
            BestMoveIsCastle: true,
            CastledBeforeMove: false,
            CastledAfterMove: false,
            DevelopedMinorPiecesBefore: 2,
            DevelopedMinorPiecesAfter: 2);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 135, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("opening_principles", tag!.Label);
        Assert.Contains("missed_castling_window", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsOpeningPrinciples_WhenBestMoveWouldDevelopMinorPiece()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "P",
            "a2",
            "a3",
            "rnbqkbnr/pppppppp/8/8/8/8/P1PPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/8/8/P7/2PPPPPP/RNBQKBNR b KQkq - 0 1");

        MoveHeuristicContext context = CreateContext(
            BestMoveDevelopsMinorPiece: true,
            DevelopedMinorPiecesBefore: 0,
            DevelopedMinorPiecesAfter: 0,
            BestMoveDevelopedMinorPiecesAfter: 1);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Inaccuracy, 105, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("opening_principles", tag!.Label);
        Assert.Contains("missed_development_step", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_DoesNotAssignOpeningPrinciples_WhenDevelopmentIsAlreadyDone()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "r1bqkbnr/pppppppp/2n5/8/8/2N2N2/PPPPPPPP/R1BQKB1R w KQkq - 0 1",
            "r1bqkbnr/pppppppp/2n5/7Q/8/2N2N2/PPPPPPPP/R1B1KB1R b KQkq - 1 1");

        MoveHeuristicContext context = CreateContext(
            EarlyQueenMove: true,
            DevelopedMinorPiecesBefore: 3,
            DevelopedMinorPiecesAfter: 3);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Inaccuracy, 100, 0, context);

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
    public void MistakeClassifier_AssignsKingSafety_WhenQueensideCastledPawnShieldIsPushed()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "P",
            "b2",
            "b4",
            "4k3/8/8/8/8/8/1P6/2K5 w - - 0 1",
            "4k3/8/8/8/1P6/8/8/2K5 b - - 0 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 170, 0);

        Assert.NotNull(tag);
        Assert.Equal("king_safety", tag!.Label);
        Assert.Contains("king_shield_weakened", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsKingSafety_WhenKingLeavesCastledShelter()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "K",
            "g1",
            "f1",
            "4k3/8/8/8/8/8/8/5RK1 w - - 0 1",
            "4k3/8/8/8/8/8/8/5KR1 b - - 1 1");

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 155, 0);

        Assert.NotNull(tag);
        Assert.Equal("king_safety", tag!.Label);
        Assert.Contains("king_left_castled_shelter", tag.Evidence);
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

        MoveHeuristicContext context = CreateContext(
            KingCentralizedBeforeMove: false,
            KingCentralizedAfterMove: false,
            BestMoveCentralizesKing: true);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 160, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("endgame_technique", tag!.Label);
        Assert.Contains("missed_king_centralization", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsEndgameTechnique_WhenKingRetreatsToEdgeAndLosesActivity()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Endgame,
            "K",
            "e4",
            "h4",
            "4k3/8/8/8/4K3/8/8/8 w - - 0 1",
            "4k3/8/8/8/7K/8/8/8 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            MovedPieceMobilityBefore: 8,
            MovedPieceMobilityAfter: 3,
            MovedPieceToEdge: true,
            KingCentralizedBeforeMove: true,
            KingCentralizedAfterMove: false);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 140, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("endgame_technique", tag!.Label);
        Assert.Contains("king_retreated_to_edge", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsPieceActivity_WhenPieceBecomesPassive()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "e4",
            "g3",
            "4k3/8/8/8/4N3/8/8/4K3 w - - 0 1",
            "4k3/8/8/8/8/6N1/8/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            MovedPieceMobilityBefore: 8,
            MovedPieceMobilityAfter: 4,
            MovedPieceToEdge: false,
            BestMoveIsCapture: false);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Mistake, 130, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("piece_activity", tag!.Label);
        Assert.Contains("reduced_piece_activity", tag.Evidence);
    }

    [Fact]
    public void MistakeClassifier_AssignsPieceActivity_WhenBestMoveWouldActivatePiece()
    {
        MistakeClassifier classifier = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "R",
            "d1",
            "d2",
            "4k3/8/8/8/8/8/8/3RK3 w - - 0 1",
            "4k3/8/8/8/8/8/3R4/4K3 b - - 1 1");

        MoveHeuristicContext context = CreateContext(
            MovedPieceMobilityBefore: 10,
            MovedPieceMobilityAfter: 10,
            BestMoveImprovesPieceActivity: true,
            BestMoveIsCapture: false);

        MistakeTag? tag = classifier.Classify(replay, PlayerSide.White, MoveQualityBucket.Inaccuracy, 115, 0, context);

        Assert.NotNull(tag);
        Assert.Equal("piece_activity", tag!.Label);
        Assert.Contains("missed_piece_activation", tag.Evidence);
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
    public void MistakeSelector_MergesNearbyMistakesFromSameMotifFamily_WhenPositionDidNotRecover()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Mistake, "hanging_piece", 180, GamePhase.Middlegame, evalBeforeCp: 30, evalAfterCp: -170),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Mistake, "material_loss", 220, GamePhase.Middlegame, evalBeforeCp: -150, evalAfterCp: -320),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "opening_principles", 105, GamePhase.Opening, evalBeforeCp: 20, evalAfterCp: -85)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(2, selected.Count);
        Assert.Equal(2, selected[0].Moves.Count);
        Assert.Equal("hanging_piece", selected[0].Tag!.Label);
    }

    [Fact]
    public void MistakeSelector_UsesFirstTurningPointAsLeadInsideMergedNarrative()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(
                1,
                1,
                MoveQualityBucket.Inaccuracy,
                "opening_principles",
                125,
                GamePhase.Opening,
                evalBeforeCp: 25,
                evalAfterCp: -110,
                explanation: new MoveExplanation("First turning point", "Hint 1", "First turning point detail")),
            CreateMoveAnalysis(
                5,
                3,
                MoveQualityBucket.Inaccuracy,
                "opening_principles",
                170,
                GamePhase.Opening,
                evalBeforeCp: -105,
                evalAfterCp: -165,
                explanation: new MoveExplanation("Late repeat", "Hint 2", "Late repeat detail")),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "piece_activity", 118, GamePhase.Middlegame, evalBeforeCp: 10, evalAfterCp: -108)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);
        SelectedMistake openingNarrative = selected.Single(item => item.Tag?.Label == "opening_principles");

        Assert.Equal(2, openingNarrative.Moves.Count);
        Assert.Equal("First turning point", openingNarrative.Explanation.ShortText);
    }

    [Fact]
    public void MistakeSelector_PrioritizesEducationalInaccuracies_NotOnlyHighestCpl()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Inaccuracy, "unclassified", 150),
            CreateMoveAnalysis(3, 2, MoveQualityBucket.Inaccuracy, "opening_principles", 140),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "material_loss", 110),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "piece_activity", 130)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, item => item.Tag?.Label == "material_loss");
        Assert.Contains(selected, item => item.Tag?.Label == "piece_activity");
        Assert.DoesNotContain(selected, item => item.Tag?.Label == "unclassified");
        Assert.Equal([2, 3, 4], selected.Select(item => item.Moves[0].Replay.MoveNumber).ToArray());
    }

    [Fact]
    public void MistakeSelector_DiversifiesInaccuraciesAcrossThemesBeforeRepeatingSameLabel()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Inaccuracy, "opening_principles", 145, GamePhase.Opening, evalBeforeCp: 20, evalAfterCp: -125),
            CreateMoveAnalysis(3, 2, MoveQualityBucket.Inaccuracy, "opening_principles", 138, GamePhase.Opening, evalBeforeCp: 10, evalAfterCp: -128),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "piece_activity", 132, GamePhase.Middlegame, evalBeforeCp: 5, evalAfterCp: -127),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "endgame_technique", 126, GamePhase.Endgame, evalBeforeCp: 15, evalAfterCp: -111)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(3, selected.Count);
        Assert.Equal(
            ["opening_principles", "piece_activity", "endgame_technique"],
            selected.Select(item => item.Tag?.Label ?? string.Empty).ToArray());
    }

    [Fact]
    public void MistakeSelector_PrefersCriticalInaccuracyFromPlayablePosition_OverBiggerLossInLostPosition()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Inaccuracy, "piece_activity", 170, GamePhase.Middlegame, evalBeforeCp: -320, evalAfterCp: -430),
            CreateMoveAnalysis(3, 2, MoveQualityBucket.Inaccuracy, "missed_tactic", 135, GamePhase.Middlegame, evalBeforeCp: 15, evalAfterCp: -150),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "opening_principles", 120, GamePhase.Opening, evalBeforeCp: 25, evalAfterCp: -95),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "endgame_technique", 118, GamePhase.Endgame, evalBeforeCp: 10, evalAfterCp: -108)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(3, selected.Count);
        Assert.DoesNotContain(selected, item => item.Tag?.Label == "piece_activity");
        Assert.Equal(
            ["missed_tactic", "opening_principles", "endgame_technique"],
            selected.Select(item => item.Tag?.Label ?? string.Empty).ToArray());
    }

    [Fact]
    public void MistakeSelector_PrioritizesThrowingAwayAdvantage_AsEducationalMoment()
    {
        MistakeSelector selector = new();
        List<MoveAnalysisResult> analyses =
        [
            CreateMoveAnalysis(1, 1, MoveQualityBucket.Inaccuracy, "piece_activity", 155, GamePhase.Middlegame, evalBeforeCp: 260, evalAfterCp: 20),
            CreateMoveAnalysis(3, 2, MoveQualityBucket.Inaccuracy, "piece_activity", 165, GamePhase.Middlegame, evalBeforeCp: -280, evalAfterCp: -390),
            CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "opening_principles", 125, GamePhase.Opening, evalBeforeCp: 20, evalAfterCp: -105),
            CreateMoveAnalysis(7, 4, MoveQualityBucket.Inaccuracy, "endgame_technique", 122, GamePhase.Endgame, evalBeforeCp: 0, evalAfterCp: -100)
        ];

        IReadOnlyList<SelectedMistake> selected = selector.Select(analyses);

        Assert.Equal(3, selected.Count);
        Assert.Equal(1, selected.Single(item => item.Tag?.Label == "piece_activity").Moves[0].Replay.MoveNumber);
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
        Assert.Equal(2, result.HighlightedMistakes.Count);
        Assert.Equal("opening_principles", result.HighlightedMistakes[0].Tag?.Label);
        Assert.Equal("missed_tactic", result.HighlightedMistakes[1].Tag?.Label);
        Assert.NotNull(result.OpeningReview);
        Assert.Equal(1, result.OpeningReview!.TheoryExit?.MoveNumber);
        Assert.Equal("f3", result.OpeningReview.TheoryExit?.San);
        Assert.Equal("opening_principles", result.OpeningReview.TheoryExit?.MistakeLabel);
        Assert.Equal("f3", result.OpeningReview.FirstSignificantMistake?.San);
        Assert.Contains("Uncommon Opening (A00)", result.OpeningReview.Branch.BranchLabel);
        Assert.All(result.HighlightedMistakes, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation.ShortText)));
        Assert.All(result.HighlightedMistakes, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation.DetailedText)));
    }

    [Fact]
    public void GameAnalysisService_ReportsAnalyzedPositionsInOrder()
    {
        ImportedGame game = PgnGameParser.Parse(MiniPgn);
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        ReplayPly whiteFirst = replay[0];
        ReplayPly whiteSecond = replay[2];

        FakeEngineAnalyzer fakeEngine = new(new Dictionary<string, EngineAnalysis>
        {
            [whiteFirst.FenBefore] = AnalysisFor(whiteFirst.FenBefore, "e2e4", 50, null, "e2e4"),
            [whiteFirst.FenAfter] = AnalysisFor(whiteFirst.FenAfter, "e7e5", 120, null, "e7e5"),
            [whiteSecond.FenBefore] = AnalysisFor(whiteSecond.FenBefore, "g2g3", 30, null, "g2g3"),
            [whiteSecond.FenAfter] = AnalysisFor(whiteSecond.FenAfter, "d8h4", null, 1, "d8h4")
        });
        List<GameAnalysisProgress> reported = [];
        SynchronousProgress<GameAnalysisProgress> progress = new(reported.Add);

        GameAnalysisService service = new(fakeEngine, replayService);
        service.AnalyzeGame(game, PlayerSide.White, new EngineAnalysisOptions(), progress);

        Assert.Equal(4, reported.Count);
        Assert.Equal(
            [
                whiteFirst.FenBefore,
                whiteFirst.FenAfter,
                whiteSecond.FenBefore,
                whiteSecond.FenAfter
            ],
            reported.Select(item => item.Fen).ToArray());
        Assert.Equal(
            [
                GameAnalysisProgressStage.BeforeMove,
                GameAnalysisProgressStage.AfterMove,
                GameAnalysisProgressStage.BeforeMove,
                GameAnalysisProgressStage.AfterMove
            ],
            reported.Select(item => item.Stage).ToArray());
        Assert.All(reported, item => Assert.Equal(2, item.TotalAnalyzedMoves));
    }

    [Fact]
    public void OpeningPhaseReviewBuilder_UsesExactEcoBranchWhenAvailable()
    {
        ImportedGame game = PgnGameParser.Parse("1. e4 e5 2. Bc4 Nc6 3. Qh5");
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        MoveAnalysisResult quietMove = CreateMoveAnalysis(1, 1, MoveQualityBucket.Good, "opening_principles", 0, GamePhase.Opening, san: "e4");
        MoveAnalysisResult prematureQueenMove = CreateMoveAnalysis(5, 3, MoveQualityBucket.Inaccuracy, "opening_principles", 110, GamePhase.Opening, san: "Qh5", uci: "d1h5");
        OpeningPhaseReview? review = OpeningPhaseReviewBuilder.Build(
            game with { Eco = "C23" },
            PlayerSide.White,
            replay,
            [quietMove, prematureQueenMove]);

        Assert.NotNull(review);
        Assert.False(review!.Branch.UsedFallback);
        Assert.Equal("eco_exact", review.Branch.Source);
        Assert.Contains("Bishop's Opening", review.Branch.BranchLabel);
        Assert.Equal("Qh5", review.TheoryExit?.San);
        Assert.Equal("Qh5", review.FirstSignificantMistake?.San);
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

        Assert.Equal(2, result.HighlightedMistakes.Count);
        Assert.Equal("opening_principles", result.HighlightedMistakes[0].Tag?.Label);
        Assert.Equal("missed_tactic", result.HighlightedMistakes[1].Tag?.Label);
        Assert.Contains("stronger option", result.HighlightedMistakes[0].Explanation.ShortText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stronger option", result.HighlightedMistakes[1].Explanation.ShortText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The move", result.HighlightedMistakes[0].Explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The move", result.HighlightedMistakes[1].Explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameAnalysisService_CachesRepeatedFenAnalysesWithinSingleRun()
    {
        const string repetitionPgn = "1. Nf3 Nf6 2. Ng1 Ng8 3. Nf3 Nf6";
        ImportedGame game = PgnGameParser.Parse(repetitionPgn);
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);

        CountingFakeEngineAnalyzer fakeEngine = new(new Dictionary<string, EngineAnalysis>
        {
            [replay[0].FenBefore] = AnalysisFor(replay[0].FenBefore, "g1f3", 25, null, "g1f3"),
            [replay[0].FenAfter] = AnalysisFor(replay[0].FenAfter, "g8f6", 15, null, "g8f6"),
            [replay[2].FenBefore] = AnalysisFor(replay[2].FenBefore, "g1f3", 20, null, "g1f3"),
            [replay[2].FenAfter] = AnalysisFor(replay[2].FenAfter, "g8f6", 10, null, "g8f6"),
            [replay[4].FenBefore] = AnalysisFor(replay[4].FenBefore, "g1f3", 25, null, "g1f3"),
            [replay[4].FenAfter] = AnalysisFor(replay[4].FenAfter, "g8f6", 15, null, "g8f6")
        });

        GameAnalysisService service = new(fakeEngine, replayService);
        GameAnalysisResult result = service.AnalyzeGame(game, PlayerSide.White, new EngineAnalysisOptions());

        Assert.Equal(3, result.MoveAnalyses.Count);
        Assert.Equal(4, fakeEngine.CallCount);
    }

    [Fact]
    public void GameAnalysisService_DetectsDeferredMaterialLossFromPlayedLinePv()
    {
        ImportedGame game = PgnGameParser.Parse("1. Nc3");
        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);
        ReplayPly whiteFirst = replay[0];

        FakeEngineAnalyzer fakeEngine = new(new Dictionary<string, EngineAnalysis>
        {
            [whiteFirst.FenBefore] = AnalysisFor(whiteFirst.FenBefore, "e2e4", 40, null, "e2e4", "e7e5"),
            [whiteFirst.FenAfter] = AnalysisFor(whiteFirst.FenAfter, "d7d5", 220, null, "d7d5", "c3d5", "d8d5")
        });

        GameAnalysisService service = new(fakeEngine, replayService);
        GameAnalysisResult result = service.AnalyzeGame(game, PlayerSide.White, new EngineAnalysisOptions());

        Assert.Single(result.MoveAnalyses);
        Assert.Equal(MoveQualityBucket.Mistake, result.MoveAnalyses[0].Quality);
        Assert.Equal("material_loss", result.MoveAnalyses[0].MistakeTag?.Label);
        Assert.Contains("played_line_material_swing_-220", result.MoveAnalyses[0].MistakeTag?.Evidence ?? []);
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

    [Fact]
    public void PositionInspector_CountDevelopedMinorPieces_CountsPiecesOffStartingSquares()
    {
        int developed = PositionInspector.CountDevelopedMinorPieces(
            "rnbqkbnr/pppppppp/8/8/8/2N2N2/PPPPPPPP/R1BQKB1R w KQkq - 0 1",
            PlayerSide.White);

        Assert.Equal(2, developed);
    }

    [Fact]
    public void PositionInspector_IsKingCentralized_RecognizesCentralKing()
    {
        bool centralized = PositionInspector.IsKingCentralized(
            "8/8/8/4k3/8/4K3/8/8 w - - 0 1",
            PlayerSide.White);

        Assert.True(centralized);
    }

    [Fact]
    public void PositionInspector_CountPieceMobility_DetectsReducedKnightMobility()
    {
        int? centerMobility = PositionInspector.CountPieceMobility(
            "4k3/8/8/8/4N3/8/8/4K3 w - - 0 1",
            "e4",
            PlayerSide.White);
        int? rimMobility = PositionInspector.CountPieceMobility(
            "4k3/8/8/8/8/7N/8/4K3 w - - 0 1",
            "h3",
            PlayerSide.White);

        Assert.Equal(8, centerMobility);
        Assert.Equal(4, rimMobility);
    }

    [Fact]
    public void PositionInspector_AnalyzeSquareSafety_DetectsFreeCapture()
    {
        PositionInspector.SquareSafetySummary? safety = PositionInspector.AnalyzeSquareSafety(
            "4k3/8/8/3p4/4N3/8/8/4K3 b - - 0 1",
            "e4",
            PlayerSide.White);

        Assert.NotNull(safety);
        Assert.True(safety!.Value.IsHanging);
        Assert.True(safety.Value.IsFreeToTake);
        Assert.True(safety.Value.LikelyLosesExchange);
        Assert.Equal(1, safety.Value.Attackers);
        Assert.Equal(0, safety.Value.Defenders);
    }

    [Fact]
    public void TemplateAdviceGenerator_ProducesDifferentTextsForEachLevel()
    {
        TemplateAdviceGenerator generator = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");
        MistakeTag tag = new("opening_principles", 0.82, ["early_queen_move"]);

        MoveExplanation beginner = generator.Generate(replay, MoveQualityBucket.Inaccuracy, tag, "e2e4", 110, ExplanationLevel.Beginner);
        MoveExplanation intermediate = generator.Generate(replay, MoveQualityBucket.Inaccuracy, tag, "e2e4", 110, ExplanationLevel.Intermediate);
        MoveExplanation advanced = generator.Generate(replay, MoveQualityBucket.Inaccuracy, tag, "e2e4", 110, ExplanationLevel.Advanced);

        Assert.NotEqual(beginner.ShortText, intermediate.ShortText);
        Assert.NotEqual(intermediate.ShortText, advanced.ShortText);
        Assert.Contains("simple habit", beginner.TrainingHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tempo", advanced.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What:", intermediate.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Why:", intermediate.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Better:", intermediate.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch next time:", intermediate.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateAdviceGenerator_RespectsConfiguredMaxLengths()
    {
        TemplateAdviceGenerator generator = new(new AdviceGenerationSettings(
            AdviceGeneratorMode.Template,
            MaxShortTextLength: 90,
            MaxTrainingHintLength: 80,
            MaxDetailedTextLength: 120));
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");
        MistakeTag tag = new("opening_principles", 0.82, ["early_queen_move"]);

        MoveExplanation explanation = generator.Generate(replay, MoveQualityBucket.Inaccuracy, tag, "e2e4", 110, ExplanationLevel.Advanced);

        Assert.True(explanation.ShortText.Length <= 90);
        Assert.True(explanation.TrainingHint.Length <= 80);
        Assert.True(explanation.DetailedText.Length <= 120);
    }

    [Fact]
    public void LocalHeuristicAdviceGenerator_AddsLocalContextWithoutExternalDependency()
    {
        LocalHeuristicAdviceGenerator generator = new(new AdviceGenerationSettings(
            AdviceGeneratorMode.Adaptive,
            MaxShortTextLength: 220,
            MaxTrainingHintLength: 220,
            MaxDetailedTextLength: 320));
        ReplayPly replay = CreateReplay(
            GamePhase.Endgame,
            "K",
            "e2",
            "e3",
            "4k3/8/8/8/8/8/4K3/8 w - - 0 1",
            "4k3/8/8/8/8/4K3/8/8 b - - 1 1");
        MistakeTag tag = new("endgame_technique", 0.94, ["missed_king_centralization"]);

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Mistake,
            tag,
            "e2d3",
            160,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext("test", "game-1", PlayerSide.White));

        Assert.Contains("very consistent", explanation.ShortText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("endgame", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review set", explanation.TrainingHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalHeuristicAdviceGenerator_UsesStructuredPromptContext()
    {
        LocalHeuristicAdviceGenerator generator = new(new AdviceGenerationSettings(
            AdviceGeneratorMode.Adaptive,
            MaxShortTextLength: 420,
            MaxTrainingHintLength: 220,
            MaxDetailedTextLength: 700));
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");
        MistakeTag tag = new("opening_principles", 0.82, ["early_queen_move"]);
        AdvicePromptContext promptContext = new(
            OpeningName: "Bishop's Opening (C23)",
            BestMoveSan: "Nf3 (g1f3)",
            Evidence: ["the queen moved early before development was complete"],
            HeuristicNotes: ["development did not improve after the move", "the moved piece drifted toward the edge instead of improving central influence"]);

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Inaccuracy,
            tag,
            "g1f3",
            110,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext("test", "game-2", PlayerSide.White, promptContext));

        Assert.DoesNotContain("local evidence", explanation.ShortText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Position cues:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bishop's Opening", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("development did not improve", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch next time:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdvicePromptContextBuilder_DescribesOpeningAndHeuristics()
    {
        ImportedGame game = new(
            "1. Qh5",
            ["Qh5"],
            "WhitePlayer",
            "BlackPlayer",
            null,
            null,
            "2026.04.18",
            "1-0",
            "C23",
            "Local");
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");
        MistakeTag tag = new("opening_principles", 0.82, ["early_queen_move"]);
        MoveHeuristicContext heuristicContext = new(
            MovedPieceHangingAfterMove: false,
            MovedPieceFreeToTake: false,
            MovedPieceLikelyLosesExchange: false,
            MovedPieceAttackDeficit: 0,
            MovedPieceValueCp: null,
            MovedPieceMobilityBefore: 8,
            MovedPieceMobilityAfter: 4,
            MovedPieceToEdge: true,
            CastledKingWingPawnPush: false,
            EarlyQueenMove: true,
            EarlyRookMove: false,
            EarlyKingMoveWithoutCastling: false,
            EdgePawnPush: false,
            BestMoveIsCapture: false,
            BestMoveIsCastle: false,
            BestMoveDevelopsMinorPiece: true,
            BestMoveImprovesPieceActivity: false,
            BestMoveMaterialSwingCp: null,
            PlayedLineMaterialSwingCp: null,
            DevelopedMinorPiecesBefore: 0,
            DevelopedMinorPiecesAfter: 0,
            BestMoveDevelopedMinorPiecesAfter: 1,
            CastledBeforeMove: false,
            CastledAfterMove: false,
            KingLeftCastledShelter: false,
            KingCentralizedBeforeMove: false,
            KingCentralizedAfterMove: false,
            BestMoveCentralizesKing: false,
            BestMoveImprovesKingActivity: false);

        AdvicePromptContext context = AdvicePromptContextBuilder.Build(game, replay, PlayerSide.White, tag, "g1f3", heuristicContext);

        Assert.Equal("Bishop's Opening (C23)", context.OpeningName);
        Assert.Equal("WhitePlayer", context.AnalyzedPlayer);
        Assert.Equal("BlackPlayer", context.OpponentPlayer);
        Assert.Contains(context.Evidence ?? [], item => item.Contains("the queen moved early", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.HeuristicNotes ?? [], item => item.Contains("development did not improve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdvicePromptFormatter_BuildsStructuredPrompt()
    {
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");
        LocalModelAdviceRequest request = new(
            replay,
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.82, ["early_queen_move"]),
            "g1f3",
            110,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext(
                "test",
                "game-3",
                PlayerSide.White,
                new AdvicePromptContext(
                    OpeningName: "Bishop's Opening (C23)",
                    AnalyzedPlayer: "WhitePlayer",
                    OpponentPlayer: "BlackPlayer",
                    BestMoveSan: "Nf3 (g1f3)",
                    Evidence: ["the queen moved early before development was complete"],
                    HeuristicNotes: ["development did not improve after the move"])),
            string.Empty);

        string prompt = AdvicePromptFormatter.BuildPrompt(request);

        Assert.Contains("chess coach", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FEN:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Opening: Bishop's Opening (C23)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the queen moved early before development was complete", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("development did not improve after the move", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch next time:", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdvicePromptFormatter_IncludesPlayerHistoryWhenProfileAvailable()
    {
        ReplayPly replay = CreateReplay(GamePhase.Middlegame, "N", "d4", "f5", "rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 1 2", "rnbqkb1r/pppppppp/5n2/5N2/8/8/PPP1PPPP/RNBQKBNR b KQkq - 2 2");
        PlayerMistakeProfile profile = new(
            "TestPlayer",
            GamesAnalyzed: 5,
            AverageCentipawnLoss: 42,
            TopPatterns:
            [
                new PlayerMistakePatternEntry("hanging_piece", 7),
                new PlayerMistakePatternEntry("missed_tactic", 4)
            ],
            WeakestPhase: GamePhase.Middlegame);

        LocalModelAdviceRequest request = new(
            replay,
            MoveQualityBucket.Mistake,
            new MistakeTag("hanging_piece", 0.90, ["piece_lost_or_underdefended"]),
            "e2e4",
            180,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext(
                "test",
                "game-profile-test",
                PlayerSide.White,
                new AdvicePromptContext(
                    AnalyzedPlayer: "TestPlayer",
                    BestMoveSan: "e4 (e2e4)",
                    Evidence: ["the moved piece became loose or tactically vulnerable"],
                    PlayerProfile: profile)),
            string.Empty);

        string prompt = AdvicePromptFormatter.BuildPrompt(request);

        Assert.Contains("Player history", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Games analyzed: 5", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Average centipawn loss: 42", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hanging piece: 7 times", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missed tactic: 4 times", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Weakest phase: Middlegame", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recurring pattern", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdvicePromptFormatter_OmitsPlayerHistoryWhenNoProfile()
    {
        ReplayPly replay = CreateReplay(GamePhase.Opening, "P", "e2", "e4", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");

        LocalModelAdviceRequest request = new(
            replay,
            MoveQualityBucket.Inaccuracy,
            null,
            "d2d4",
            60,
            ExplanationLevel.Beginner,
            new AdviceGenerationContext("test", "game-no-profile", PlayerSide.White),
            string.Empty);

        string prompt = AdvicePromptFormatter.BuildPrompt(request);

        Assert.DoesNotContain("Player history", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_ParsesStructuredFieldsWithMultilineDetailedText()
    {
        const string rawResponse = """
short_text: The move slowed development and gave away initiative.
detailed_text: You brought the queen out before the minor pieces were ready.
That gave Black easy developing moves with tempo.
training_hint: Before every opening queen move, ask which minor piece could improve first.
""";

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.NotNull(response);
        Assert.Contains("slowed development", response!.ShortText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("easy developing moves", response.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minor piece", response.TrainingHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_ParsesJsonPayload()
    {
        const string rawResponse = """
{
  "short_text": "The move gave away central control.",
  "detailed_text": "You moved the queen too early and let the opponent develop with tempo.",
  "training_hint": "In the opening, develop a knight or bishop before repeating queen moves."
}
""";

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.NotNull(response);
        Assert.Equal("The move gave away central control.", response!.ShortText);
        Assert.Contains("develop with tempo", response.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("develop a knight", response.TrainingHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_ParsesMarkdownWrappedJsonPayload()
    {
        const string rawResponse = """
```json
{
  "short_text": "The move lost the initiative.",
  "detailed_text": "You moved the queen too early and let Black develop freely.",
  "training_hint": "Before a queen move in the opening, check whether a knight or bishop can improve first."
}
```
""";

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.NotNull(response);
        Assert.Contains("lost the initiative", response!.ShortText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_RejectsIncompletePayload()
    {
        const string rawResponse = """
short_text: The move lost time.
training_hint: Improve a minor piece first.
""";

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.False(parsed);
        Assert.Null(response);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_ParsesJsonFromLlamaChatStdoutWithEchoedPrompt()
    {
        const string rawResponse = """
            Loading model...


            build      : b8837-59accc886
            model      : qwen2.5-3b-instruct-q4_k_m.gguf
            modalities : text

            available commands:
              /exit or Ctrl+C     stop or exit
              /regen              regenerate the last response
              /clear              clear the chat history
              /read <file>        add a text file
              /glob <pattern>     add text files using globbing pattern


            > Return EXACTLY this JSON object and nothing else:
            {"short_text":"ok","detailed_text":"ok","training_hint":"ok"}

            {"short_text":"ok","detailed_text":"ok","training_hint":"ok"}

            [ Prompt: 49.3 t/s | Generation: 5.2 t/s ]

            Exiting...
            """;

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.NotNull(response);
        Assert.Equal("ok", response!.ShortText);
        Assert.Equal("ok", response.DetailedText);
        Assert.Equal("ok", response.TrainingHint);
    }

    [Fact]
    public void LocalModelAdviceGenerator_FallsBackWhenModelIsUnavailable()
    {
        LocalModelAdviceGenerator generator = new(
            new AdviceGenerationSettings(AdviceGeneratorMode.Adaptive, 260, 220, 420),
            new FakeLocalAdviceModel(isAvailable: false),
            new TemplateAdviceGenerator());
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.82, ["early_queen_move"]),
            "g1f3",
            110,
            ExplanationLevel.Intermediate);

        Assert.True(generator.UsedFallback);
        Assert.Contains("unavailable", generator.FallbackReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(explanation.ShortText);
        Assert.Contains("What:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Better:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalModelAdviceGenerator_UsesLocalModelResponseWhenAvailable()
    {
        LocalModelAdviceGenerator generator = new(
            new AdviceGenerationSettings(AdviceGeneratorMode.Adaptive, 260, 220, 420),
            new FakeLocalAdviceModel(
                isAvailable: true,
                response: """
short_text: Model short text
detailed_text: Model detailed text
training_hint: Model training hint
"""),
            new TemplateAdviceGenerator());
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.82, ["early_queen_move"]),
            "g1f3",
            110,
            ExplanationLevel.Intermediate);

        Assert.False(generator.UsedFallback);
        Assert.Null(generator.FallbackReason);
        Assert.Equal("Model short text", explanation.ShortText);
        Assert.Equal("Model training hint", explanation.TrainingHint);
        Assert.Equal("Model detailed text", explanation.DetailedText);
    }

    [Fact]
    public void LocalModelAdviceGenerator_FallsBackWhenModelResponseCannotBeParsed()
    {
        LocalModelAdviceGenerator generator = new(
            new AdviceGenerationSettings(AdviceGeneratorMode.Adaptive, 260, 220, 420),
            new FakeLocalAdviceModel(
                isAvailable: true,
                response: "This is free-form text without the required fields."),
            new TemplateAdviceGenerator());
        ReplayPly replay = CreateReplay(
            GamePhase.Opening,
            "Q",
            "d1",
            "h5",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1");

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.82, ["early_queen_move"]),
            "g1f3",
            110,
            ExplanationLevel.Intermediate);

        Assert.True(generator.UsedFallback);
        Assert.Contains("unparsable", generator.FallbackReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(explanation.ShortText);
        Assert.Contains("What:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateAdviceGenerator_BuildsWhatWhyBetterWatchStructure()
    {
        TemplateAdviceGenerator generator = new();
        ReplayPly replay = CreateReplay(
            GamePhase.Middlegame,
            "N",
            "f3",
            "h4",
            "r1bqkbnr/pppp1ppp/2n5/4p3/7N/8/PPPPPPPP/RNBQKB1R w KQkq - 0 3",
            "r1bqkbnr/pppp1ppp/2n5/4p3/7N/8/PPPPPPPP/RNBQKB1R b KQkq - 1 3");
        MistakeTag tag = new("piece_activity", 0.73, ["missed_piece_activation"]);

        MoveExplanation explanation = generator.Generate(
            replay,
            MoveQualityBucket.Inaccuracy,
            tag,
            "f3g5",
            105,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext(
                "test",
                "game-structure",
                PlayerSide.White,
                new AdvicePromptContext(BestMoveSan: "Ng5 (f3g5)")));

        Assert.Contains("What:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Why:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Better:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch next time:", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ng5", explanation.DetailedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalProcessAdviceModel_ExecutesLocalCommandAndReturnsStdout()
    {
        string powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        Assert.True(File.Exists(powerShellPath));

        LocalProcessAdviceModel model = new(new LocalAdviceModelOptions(
            powerShellPath,
            "-NoProfile -Command \"$input | Out-Null; Write-Output '{\\\"short_text\\\":\\\"Process short text\\\",\\\"detailed_text\\\":\\\"Process detailed text\\\",\\\"training_hint\\\":\\\"Process training hint\\\"}'\"",
            TimeoutMs: 5000));

        string? rawResponse = model.Generate(new LocalModelAdviceRequest(
            CreateReplay(
                GamePhase.Opening,
                "Q",
                "d1",
                "h5",
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                "rnbqkbnr/pppppppp/8/7Q/8/8/PPPPPPPP/RNB1KBNR b KQkq - 1 1"),
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.82, ["early_queen_move"]),
            "g1f3",
            110,
            ExplanationLevel.Intermediate,
            new AdviceGenerationContext("test", "game-4"),
            "prompt"));

        Assert.NotNull(rawResponse);
        Assert.Contains("Process short text", rawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlamaCppAdviceModel_BuildArguments_UsesGrammarPromptAndModel()
    {
        IReadOnlyList<string> arguments = LlamaCppAdviceModel.BuildArguments(
            "C:\\models\\stockifhsgui-advice.gguf",
            "Prompt body",
            180,
            2048,
            "all");

        Assert.Equal("-m", arguments[0]);
        Assert.Equal("C:\\models\\stockifhsgui-advice.gguf", arguments[1]);
        Assert.Equal("-c", arguments[2]);
        Assert.Equal("2048", arguments[3]);
        Assert.Equal("-n", arguments[4]);
        Assert.Equal("180", arguments[5]);
        Assert.Equal("--single-turn", arguments[6]);
        Assert.Equal("--simple-io", arguments[7]);
        Assert.Equal("--no-display-prompt", arguments[8]);
        Assert.Equal("--log-disable", arguments[9]);
        Assert.Equal("-ngl", arguments[10]);
        Assert.Equal("all", arguments[11]);
        Assert.Equal("--grammar", arguments[12]);
        Assert.Contains("short_text", arguments[13], StringComparison.Ordinal);
        Assert.Equal("-p", arguments[14]);
        Assert.Equal("Prompt body", arguments[15]);
    }

    [Fact]
    public void LlamaCppAdviceModel_BuildJsonGrammar_RequiresAdviceFields()
    {
        string grammar = LlamaCppAdviceModel.BuildJsonGrammar();

        Assert.Contains("short_text", grammar, StringComparison.Ordinal);
        Assert.Contains("detailed_text", grammar, StringComparison.Ordinal);
        Assert.Contains("training_hint", grammar, StringComparison.Ordinal);
    }

    [Fact]
    public void LlamaCppAdviceRuntimeResolver_UsesEnvironmentOverridesWhenFilesExist()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"StockifhsGUI-llama-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string cliPath = Path.Combine(tempDirectory, "llama-cli.exe");
        string modelPath = Path.Combine(tempDirectory, "stockifhsgui-advice.gguf");
        File.WriteAllText(cliPath, "cli");
        File.WriteAllText(modelPath, "model");

        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");
        string? previousMaxTokens = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MAX_TOKENS");
        string? previousTimeout = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_TIMEOUT_MS");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", cliPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", modelPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MAX_TOKENS", "190");
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_TIMEOUT_MS", "12000");

            LlamaCppAdviceRuntime? runtime = LlamaCppAdviceRuntimeResolver.Resolve();

            Assert.NotNull(runtime);
            Assert.Equal(cliPath, runtime!.CliPath);
            Assert.Equal(modelPath, runtime.ModelPath);
            Assert.Equal(190, runtime.MaxTokens);
            Assert.Equal(2048, runtime.ContextSize);
            Assert.Equal(12000, runtime.TimeoutMs);
            Assert.Equal(LlamaGpuSettingsResolver.BalancedGpuLayersArgument, runtime.GpuLayersArgument);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MAX_TOKENS", previousMaxTokens);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_TIMEOUT_MS", previousTimeout);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void AdviceRuntimeCatalog_ReturnsReadyStatusForSupportedLlamaCppSetup()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"StockifhsGUI-llama-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string cliPath = Path.Combine(tempDirectory, "llama-cli.exe");
        string modelPath = Path.Combine(tempDirectory, "stockifhsgui-advice.gguf");
        File.WriteAllText(cliPath, "cli");
        File.WriteAllText(modelPath, "model");

        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", cliPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", modelPath);

            AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();

            Assert.True(status.IsReady);
            Assert.Contains("llama.cpp ready", status.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void AdviceRuntimeCatalog_ReturnsFallbackStatusWhenNoRuntimeIsConfigured()
    {
        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");
        string? previousCommand = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_COMMAND");
        string? previousArgs = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_ARGS");
        string? previousWorkdir = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_WORKDIR");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", null);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", null);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_COMMAND", null);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_ARGS", null);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_WORKDIR", null);

            AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();

            Assert.False(status.IsReady);
            Assert.Contains("heuristic fallback", status.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(status.InstallHint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_COMMAND", previousCommand);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_ARGS", previousArgs);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_WORKDIR", previousWorkdir);
        }
    }

    [Fact]
    public void LlamaCppAdviceRuntimeResolver_RecognizesRecommendedQwenFileName()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"StockifhsGUI-llama-qwen-{Guid.NewGuid():N}");
        string modelsDirectory = Path.Combine(tempDirectory, "Models");
        Directory.CreateDirectory(modelsDirectory);
        string cliPath = Path.Combine(tempDirectory, "llama-cli.exe");
        string modelPath = Path.Combine(modelsDirectory, "qwen2.5-3b-instruct-q4_k_m.gguf");
        File.WriteAllText(cliPath, "cli");
        File.WriteAllText(modelPath, "model");

        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");
        string previousDirectory = Directory.GetCurrentDirectory();

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", cliPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", null);
            Directory.SetCurrentDirectory(tempDirectory);

            LlamaCppAdviceRuntime? runtime = LlamaCppAdviceRuntimeResolver.Resolve();

            Assert.NotNull(runtime);
            Assert.Equal(modelPath, runtime!.ModelPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LlamaCppServerResolver_UsesEnvironmentOverrideWhenFileExists()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"StockifhsGUI-server-resolve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string serverPath = Path.Combine(tempDirectory, "llama-server.exe");
        string modelPath = Path.Combine(tempDirectory, "stockifhsgui-advice.gguf");
        File.WriteAllText(serverPath, "server");
        File.WriteAllText(modelPath, "model");

        string? previousServer = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");
        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", serverPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", modelPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", null);

            LlamaCppServerConfig? config = LlamaCppServerResolver.Resolve();

            Assert.NotNull(config);
            Assert.Equal(serverPath, config!.ServerPath);
            Assert.Equal(modelPath, config.ModelPath);
            Assert.Equal(LlamaGpuSettingsResolver.BalancedGpuLayersArgument, config.GpuLayersArgument);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", previousServer);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LlamaCppServerResolver_ReturnsNullWhenServerExeMissing()
    {
        string? previousServer = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", null);

            string? serverPath = LlamaCppServerResolver.ResolveServerPath();

            // May or may not be null depending on what's on disk, but at least it should not throw.
            Assert.True(serverPath is null || File.Exists(serverPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", previousServer);
        }
    }

    [Fact]
    public void LlamaGpuSettingsResolver_UsesEnvironmentOverrideForFullGpuMode()
    {
        string? previousValue = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_FULL_GPU");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_FULL_GPU", "true");

            string gpuLayersArgument = LlamaGpuSettingsResolver.ResolveGpuLayersArgument();

            Assert.Equal(LlamaGpuSettingsResolver.FullGpuLayersArgument, gpuLayersArgument);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_FULL_GPU", previousValue);
        }
    }

    [Fact]
    public void LlamaCppHttpAdviceModel_ExtractContent_ParsesValidResponse()
    {
        const string responseJson = """
        {
          "content": "{\"short_text\":\"ok\",\"detailed_text\":\"ok\",\"training_hint\":\"ok\"}",
          "id_slot": 0,
          "stop": true,
          "model": "test.gguf"
        }
        """;

        string? content = LlamaCppHttpAdviceModel.ExtractContent(responseJson);

        Assert.NotNull(content);
        Assert.Contains("short_text", content);
        Assert.Contains("ok", content);
    }

    [Fact]
    public void LlamaCppHttpAdviceModel_ExtractContent_ReturnsNullForEmptyContent()
    {
        const string responseJson = """
        {
          "content": "",
          "stop": true
        }
        """;

        string? content = LlamaCppHttpAdviceModel.ExtractContent(responseJson);

        Assert.Null(content);
    }

    [Fact]
    public void LlamaCppHttpAdviceModel_ExtractContent_ReturnsNullForMissingContentField()
    {
        const string responseJson = """
        {
          "stop": true,
          "model": "test.gguf"
        }
        """;

        string? content = LlamaCppHttpAdviceModel.ExtractContent(responseJson);

        Assert.Null(content);
    }

    [Fact]
    public void LlamaCppHttpAdviceModel_ExtractContent_ReturnsNullForInvalidJson()
    {
        string? content = LlamaCppHttpAdviceModel.ExtractContent("not json");

        Assert.Null(content);
    }

    [Fact]
    public void LlamaCppHttpAdviceModel_ExtractContent_ParsedContentCanBeUsedByResponseParser()
    {
        const string responseJson = """
        {
          "content": "{\"short_text\":\"Move explanation\",\"detailed_text\":\"Detailed explanation\",\"training_hint\":\"Training hint\"}",
          "stop": true
        }
        """;

        string? content = LlamaCppHttpAdviceModel.ExtractContent(responseJson);
        Assert.NotNull(content);

        bool parsed = LocalModelAdviceResponseParser.TryParse(content, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.NotNull(response);
        Assert.Equal("Move explanation", response!.ShortText);
        Assert.Equal("Detailed explanation", response.DetailedText);
        Assert.Equal("Training hint", response.TrainingHint);
    }

    [Fact]
    public void AdviceRuntimeCatalog_PrefersServerOverCliWhenBothExist()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"StockifhsGUI-catalog-priority-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string serverPath = Path.Combine(tempDirectory, "llama-server.exe");
        string cliPath = Path.Combine(tempDirectory, "llama-cli.exe");
        string modelPath = Path.Combine(tempDirectory, "stockifhsgui-advice.gguf");
        File.WriteAllText(serverPath, "server");
        File.WriteAllText(cliPath, "cli");
        File.WriteAllText(modelPath, "model");

        string? previousServer = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH");
        string? previousCli = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH");
        string? previousModel = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH");

        try
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", serverPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", cliPath);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", modelPath);

            ILocalAdviceModel? model = AdviceRuntimeCatalog.TryCreateConfiguredModel();

            Assert.NotNull(model);
            Assert.IsType<LlamaCppHttpAdviceModel>(model);
            Assert.Equal("llama.cpp (server)", model!.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH", previousServer);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CLI_PATH", previousCli);
            Environment.SetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH", previousModel);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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

    private static MoveAnalysisResult CreateMoveAnalysis(
        int ply,
        int moveNumber,
        MoveQualityBucket quality,
        string label,
        int cpl,
        GamePhase phase = GamePhase.Middlegame,
        int evalBeforeCp = 0,
        int? evalAfterCp = null,
        MoveExplanation? explanation = null,
        string san = "move",
        string uci = "a2a3")
    {
        ReplayPly replay = new(
            ply,
            moveNumber,
            PlayerSide.White,
            san,
            san,
            uci,
            "4k3/8/8/8/8/8/P7/4K3 w - - 0 1",
            "4k3/8/8/8/8/P7/8/4K3 b - - 0 1",
            string.Empty,
            string.Empty,
            phase,
            "P",
            null,
            "a2",
            "a3",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            AnalysisFor(replay.FenBefore, uci, 0, null, uci),
            AnalysisFor(replay.FenAfter, "a7a6", 0, null, "a7a6"),
            evalBeforeCp,
            evalAfterCp ?? -cpl,
            null,
            null,
            cpl,
            quality,
            0,
            new MistakeTag(label, 0.8, ["evidence"]),
            explanation ?? new MoveExplanation("Short explanation", "Training hint", "Detailed explanation"));
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

    private static MoveHeuristicContext CreateContext(
        bool MovedPieceHangingAfterMove = false,
        bool MovedPieceFreeToTake = false,
        bool MovedPieceLikelyLosesExchange = false,
        int MovedPieceAttackDeficit = 0,
        int? MovedPieceValueCp = null,
        int? MovedPieceMobilityBefore = null,
        int? MovedPieceMobilityAfter = null,
        bool MovedPieceToEdge = false,
        bool CastledKingWingPawnPush = false,
        bool EarlyQueenMove = false,
        bool EarlyRookMove = false,
        bool EarlyKingMoveWithoutCastling = false,
        bool EdgePawnPush = false,
        bool BestMoveIsCapture = false,
        bool BestMoveIsCastle = false,
        bool BestMoveDevelopsMinorPiece = false,
        bool BestMoveImprovesPieceActivity = false,
        int? BestMoveMaterialSwingCp = null,
        int? PlayedLineMaterialSwingCp = null,
        int DevelopedMinorPiecesBefore = 0,
        int DevelopedMinorPiecesAfter = 0,
        int BestMoveDevelopedMinorPiecesAfter = 0,
        bool CastledBeforeMove = false,
        bool CastledAfterMove = false,
        bool KingLeftCastledShelter = false,
        bool KingCentralizedBeforeMove = false,
        bool KingCentralizedAfterMove = false,
        bool BestMoveCentralizesKing = false,
        bool BestMoveImprovesKingActivity = false)
    {
        return new MoveHeuristicContext(
            MovedPieceHangingAfterMove,
            MovedPieceFreeToTake,
            MovedPieceLikelyLosesExchange,
            MovedPieceAttackDeficit,
            MovedPieceValueCp,
            MovedPieceMobilityBefore,
            MovedPieceMobilityAfter,
            MovedPieceToEdge,
            CastledKingWingPawnPush,
            EarlyQueenMove,
            EarlyRookMove,
            EarlyKingMoveWithoutCastling,
            EdgePawnPush,
            BestMoveIsCapture,
            BestMoveIsCastle,
            BestMoveDevelopsMinorPiece,
            BestMoveImprovesPieceActivity,
            BestMoveMaterialSwingCp,
            PlayedLineMaterialSwingCp,
            DevelopedMinorPiecesBefore,
            DevelopedMinorPiecesAfter,
            BestMoveDevelopedMinorPiecesAfter,
            CastledBeforeMove,
            CastledAfterMove,
            KingLeftCastledShelter,
            KingCentralizedBeforeMove,
            KingCentralizedAfterMove,
            BestMoveCentralizesKing,
            BestMoveImprovesKingActivity);
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

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> handler;

        public SynchronousProgress(Action<T> handler)
        {
            this.handler = handler;
        }

        public void Report(T value) => handler(value);
    }

    private sealed class CountingFakeEngineAnalyzer : IEngineAnalyzer
    {
        private readonly IReadOnlyDictionary<string, EngineAnalysis> analysesByFen;

        public CountingFakeEngineAnalyzer(IReadOnlyDictionary<string, EngineAnalysis> analysesByFen)
        {
            this.analysesByFen = analysesByFen;
        }

        public int CallCount { get; private set; }

        public EngineAnalysis AnalyzePosition(string fen, EngineAnalysisOptions options)
        {
            CallCount++;
            if (analysesByFen.TryGetValue(fen, out EngineAnalysis? analysis))
            {
                return analysis;
            }

            throw new InvalidOperationException($"No fake analysis configured for FEN: {fen}");
        }
    }

    private sealed class FakeLocalAdviceModel : ILocalAdviceModel
    {
        private readonly string? response;

        public FakeLocalAdviceModel(bool isAvailable, string? response = null)
        {
            IsAvailable = isAvailable;
            this.response = response;
        }

        public string Name => "fake-local-model";

        public bool IsAvailable { get; }

        public string? Generate(LocalModelAdviceRequest request) => response;
    }
}
