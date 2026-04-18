namespace StockifhsGUI;

public sealed class MistakeClassifier
{
    public MistakeTag? Classify(
        ReplayPly replay,
        PlayerSide analyzedSide,
        MoveQualityBucket quality,
        int? centipawnLoss,
        int materialDeltaCp,
        MoveHeuristicContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(replay);

        if (quality == MoveQualityBucket.Good)
        {
            return null;
        }

        MoveHeuristicContext effectiveContext = context ?? BuildFallbackContext(replay, analyzedSide);
        double qualityBoost = QualityBoost(quality);
        int bestMoveMaterialSwing = effectiveContext.BestMoveMaterialSwingCp ?? 0;
        int playedLineMaterialSwing = effectiveContext.PlayedLineMaterialSwingCp ?? 0;

        if (materialDeltaCp <= -200 || playedLineMaterialSwing <= -200)
        {
            return Build(
                "material_loss",
                ClampConfidence(0.78 + qualityBoost + Math.Min(0.12, Math.Abs(Math.Min(materialDeltaCp, playedLineMaterialSwing)) / 2000.0)),
                materialDeltaCp <= -200 ? $"material_delta_{materialDeltaCp}" : null,
                playedLineMaterialSwing <= -200 ? $"played_line_material_swing_{playedLineMaterialSwing}" : null,
                QualityEvidence(quality));
        }

        if (IsHangingPieceMistake(replay, centipawnLoss, materialDeltaCp, playedLineMaterialSwing, effectiveContext)
            && !replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                "hanging_piece",
                ClampConfidence(0.74 + qualityBoost + HangingPieceBoost(effectiveContext, materialDeltaCp, playedLineMaterialSwing)),
                effectiveContext.MovedPieceFreeToTake ? "moved_piece_free_to_take" : "moved_piece_loses_exchange",
                materialDeltaCp < 0 ? $"material_delta_{materialDeltaCp}" : null,
                playedLineMaterialSwing < 0 ? $"played_line_material_swing_{playedLineMaterialSwing}" : null,
                QualityEvidence(quality));
        }

        if (IsKingSafetyMistake(replay, centipawnLoss, effectiveContext))
        {
            return Build(
                "king_safety",
                ClampConfidence(0.66 + qualityBoost + ((centipawnLoss ?? 0) >= 180 ? 0.05 : 0.0)),
                "king_shield_weakened",
                QualityEvidence(quality));
        }

        if (IsOpeningPrinciplesMistake(replay, centipawnLoss, effectiveContext))
        {
            return Build(
                "opening_principles",
                ClampConfidence(0.62 + qualityBoost + OpeningPenaltyBoost(effectiveContext)),
                OpeningEvidence(effectiveContext),
                QualityEvidence(quality));
        }

        if (bestMoveMaterialSwing >= 200 || (effectiveContext.BestMoveIsCapture && (centipawnLoss ?? 0) >= 120))
        {
            return Build(
                "missed_tactic",
                ClampConfidence(0.64 + qualityBoost + Math.Min(0.10, bestMoveMaterialSwing / 2000.0)),
                effectiveContext.BestMoveIsCapture ? "best_move_is_forcing_capture" : null,
                bestMoveMaterialSwing >= 200 ? $"best_move_material_swing_{bestMoveMaterialSwing}" : null,
                QualityEvidence(quality));
        }

        if (IsPieceActivityMistake(replay, centipawnLoss, effectiveContext))
        {
            return Build(
                "piece_activity",
                ClampConfidence(0.60 + qualityBoost + PieceActivityBoost(effectiveContext)),
                PieceActivityEvidence(effectiveContext),
                QualityEvidence(quality));
        }

        if (IsEndgameTechniqueMistake(replay, centipawnLoss, effectiveContext))
        {
            return Build(
                "endgame_technique",
                ClampConfidence(0.61 + qualityBoost + EndgameTechniqueBoost(effectiveContext) + ((centipawnLoss ?? 0) >= 180 ? 0.05 : 0.0)),
                EndgameEvidence(effectiveContext),
                "endgame_phase",
                QualityEvidence(quality));
        }

        return Build(
            "missed_tactic",
            ClampConfidence(0.58 + qualityBoost),
            "engine_prefers_tactical_alternative",
            QualityEvidence(quality));
    }

    private static bool IsOpeningPrinciplesMistake(ReplayPly replay, int? centipawnLoss, MoveHeuristicContext context)
    {
        if (replay.Phase != GamePhase.Opening || (centipawnLoss ?? 0) < 80)
        {
            return false;
        }

        if (context.EarlyKingMoveWithoutCastling)
        {
            return true;
        }

        if (context.BestMoveIsCastle && !context.CastledAfterMove && !context.CastledBeforeMove)
        {
            return true;
        }

        if (context.BestMoveDevelopsMinorPiece
            && context.BestMoveDevelopedMinorPiecesAfter > context.DevelopedMinorPiecesAfter)
        {
            return true;
        }

        if (context.EarlyQueenMove && context.DevelopedMinorPiecesBefore < 2)
        {
            return true;
        }

        if (context.EarlyRookMove && context.DevelopedMinorPiecesBefore < 2)
        {
            return true;
        }

        if (context.EdgePawnPush
            && !context.CastledBeforeMove
            && context.DevelopedMinorPiecesBefore < 2)
        {
            return true;
        }

        return IsNonDevelopingOpeningMove(replay, context);
    }

    private static bool IsNonDevelopingOpeningMove(ReplayPly replay, MoveHeuristicContext context)
    {
        if (replay.IsCastle || IsMinorPieceMove(replay))
        {
            return false;
        }

        return !context.CastledAfterMove
            && context.DevelopedMinorPiecesAfter <= context.DevelopedMinorPiecesBefore
            && context.DevelopedMinorPiecesAfter < 2;
    }

    private static bool IsMinorPieceMove(ReplayPly replay)
    {
        return replay.MovingPiece.Equals("N", StringComparison.OrdinalIgnoreCase)
            || replay.MovingPiece.Equals("B", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEndgameTechniqueMistake(ReplayPly replay, int? centipawnLoss, MoveHeuristicContext context)
    {
        if (replay.Phase != GamePhase.Endgame || (centipawnLoss ?? 0) < 100)
        {
            return false;
        }

        if (context.BestMoveCentralizesKing && !context.KingCentralizedAfterMove)
        {
            return true;
        }

        return replay.MovingPiece.Equals("K", StringComparison.OrdinalIgnoreCase)
            && context.KingCentralizedBeforeMove
            && !context.KingCentralizedAfterMove;
    }

    private static bool IsKingSafetyMistake(ReplayPly replay, int? centipawnLoss, MoveHeuristicContext context)
    {
        if ((centipawnLoss ?? 0) < 120 || !replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return context.CastledKingWingPawnPush;
    }

    private static bool IsPieceActivityMistake(ReplayPly replay, int? centipawnLoss, MoveHeuristicContext context)
    {
        if (replay.Phase != GamePhase.Middlegame
            || (centipawnLoss ?? 0) < 100
            || replay.IsCapture
            || replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase)
            || replay.MovingPiece.Equals("K", StringComparison.OrdinalIgnoreCase)
            || context.MovedPieceHangingAfterMove
            || context.BestMoveIsCapture)
        {
            return false;
        }

        if (context.MovedPieceMobilityBefore is not int beforeMobility
            || context.MovedPieceMobilityAfter is not int afterMobility)
        {
            return false;
        }

        int mobilityDrop = beforeMobility - afterMobility;
        if (mobilityDrop >= 3)
        {
            return true;
        }

        return context.MovedPieceToEdge
            && afterMobility <= 2
            && mobilityDrop >= 1;
    }

    private static bool IsHangingPieceMistake(
        ReplayPly replay,
        int? centipawnLoss,
        int materialDeltaCp,
        int playedLineMaterialSwing,
        MoveHeuristicContext context)
    {
        if (!context.MovedPieceHangingAfterMove
            || replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.MovedPieceFreeToTake)
        {
            return true;
        }

        if (context.MovedPieceLikelyLosesExchange
            && ((centipawnLoss ?? 0) >= 120 || materialDeltaCp < 0 || playedLineMaterialSwing < 0))
        {
            return true;
        }

        return false;
    }

    private static MistakeTag Build(string label, double confidence, params string?[] evidence)
    {
        string[] normalizedEvidence = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
        return new MistakeTag(label, confidence, normalizedEvidence);
    }

    private static string QualityEvidence(MoveQualityBucket quality)
    {
        return $"quality_{quality.ToString().ToLowerInvariant()}";
    }

    private static MoveHeuristicContext BuildFallbackContext(ReplayPly replay, PlayerSide analyzedSide)
    {
        char movedPiece = char.ToLowerInvariant(replay.MovingPiece[0]);
        char fromFile = replay.FromSquare[0];
        int developedMinorPiecesBefore = PositionInspector.CountDevelopedMinorPieces(replay.FenBefore, analyzedSide);
        int developedMinorPiecesAfter = PositionInspector.CountDevelopedMinorPieces(replay.FenAfter, analyzedSide);
        bool castledBeforeMove = PositionInspector.IsKingOnCastledWing(replay.FenBefore, analyzedSide);
        bool castledAfterMove = PositionInspector.IsKingOnCastledWing(replay.FenAfter, analyzedSide);
        bool kingCentralizedBeforeMove = PositionInspector.IsKingCentralized(replay.FenBefore, analyzedSide);
        bool kingCentralizedAfterMove = PositionInspector.IsKingCentralized(replay.FenAfter, analyzedSide);
        PositionInspector.SquareSafetySummary? movedPieceSafety = PositionInspector.AnalyzeSquareSafety(replay.FenAfter, replay.ToSquare, analyzedSide);

        return new MoveHeuristicContext(
            movedPieceSafety?.IsHanging == true,
            movedPieceSafety?.IsFreeToTake == true,
            movedPieceSafety?.LikelyLosesExchange == true,
            movedPieceSafety is PositionInspector.SquareSafetySummary safety ? safety.Attackers - safety.Defenders : 0,
            movedPieceSafety?.PieceValueCp,
            PositionInspector.CountPieceMobility(replay.FenBefore, replay.FromSquare, analyzedSide),
            PositionInspector.CountPieceMobility(replay.FenAfter, replay.ToSquare, analyzedSide),
            PositionInspector.IsEdgeSquare(replay.ToSquare),
            movedPiece == 'p' && fromFile is 'f' or 'g' or 'h' && castledBeforeMove,
            replay.Phase == GamePhase.Opening && movedPiece == 'q',
            replay.Phase == GamePhase.Opening && movedPiece == 'r',
            replay.Phase == GamePhase.Opening && movedPiece == 'k' && !replay.IsCastle,
            replay.Phase == GamePhase.Opening && movedPiece == 'p' && fromFile is 'a' or 'b' or 'g' or 'h',
            false,
            false,
            false,
            null,
            null,
            developedMinorPiecesBefore,
            developedMinorPiecesAfter,
            developedMinorPiecesBefore,
            castledBeforeMove,
            castledAfterMove,
            kingCentralizedBeforeMove,
            kingCentralizedAfterMove,
            false);
    }

    private static double QualityBoost(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => 0.12,
            MoveQualityBucket.Mistake => 0.07,
            MoveQualityBucket.Inaccuracy => 0.02,
            _ => 0.0
        };
    }

    private static double OpeningPenaltyBoost(MoveHeuristicContext context)
    {
        if (context.EarlyKingMoveWithoutCastling)
        {
            return 0.10;
        }

        if ((context.EarlyQueenMove || context.EarlyRookMove) && context.DevelopedMinorPiecesBefore < 2)
        {
            return 0.08;
        }

        if (context.EdgePawnPush && context.DevelopedMinorPiecesBefore < 2)
        {
            return 0.05;
        }

        return context.DevelopedMinorPiecesAfter <= context.DevelopedMinorPiecesBefore ? 0.04 : 0.0;
    }

    private static string OpeningEvidence(MoveHeuristicContext context)
    {
        if (context.EarlyQueenMove && context.DevelopedMinorPiecesBefore < 2)
        {
            return "early_queen_before_development";
        }

        if (context.BestMoveIsCastle && !context.CastledAfterMove && !context.CastledBeforeMove)
        {
            return "missed_castling_window";
        }

        if (context.BestMoveDevelopsMinorPiece
            && context.BestMoveDevelopedMinorPiecesAfter > context.DevelopedMinorPiecesAfter)
        {
            return "missed_development_step";
        }

        if (context.EarlyRookMove && context.DevelopedMinorPiecesBefore < 2)
        {
            return "early_rook_before_development";
        }

        if (context.EarlyKingMoveWithoutCastling)
        {
            return "early_king_move_without_castling";
        }

        if (context.EdgePawnPush && context.DevelopedMinorPiecesBefore < 2)
        {
            return "wing_pawn_before_development";
        }

        return "no_development_progress";
    }

    private static string EndgameEvidence(MoveHeuristicContext context)
    {
        return context.BestMoveCentralizesKing && !context.KingCentralizedAfterMove
            ? "missed_king_centralization"
            : "king_moved_away_from_center";
    }

    private static double EndgameTechniqueBoost(MoveHeuristicContext context)
    {
        if (context.BestMoveCentralizesKing && !context.KingCentralizedAfterMove)
        {
            return 0.08;
        }

        return context.KingCentralizedBeforeMove && !context.KingCentralizedAfterMove ? 0.05 : 0.0;
    }

    private static double HangingPieceBoost(MoveHeuristicContext context, int materialDeltaCp, int playedLineMaterialSwing)
    {
        double boost = 0.0;
        if (context.MovedPieceFreeToTake)
        {
            boost += 0.08;
        }

        if (context.MovedPieceLikelyLosesExchange)
        {
            boost += 0.05;
        }

        if ((context.MovedPieceValueCp ?? 0) >= 500)
        {
            boost += 0.03;
        }

        if (materialDeltaCp < 0 || playedLineMaterialSwing < 0)
        {
            boost += 0.04;
        }

        return boost;
    }

    private static double PieceActivityBoost(MoveHeuristicContext context)
    {
        int mobilityDrop = (context.MovedPieceMobilityBefore ?? 0) - (context.MovedPieceMobilityAfter ?? 0);
        double boost = 0.0;
        if (mobilityDrop >= 4)
        {
            boost += 0.07;
        }
        else if (mobilityDrop >= 2)
        {
            boost += 0.04;
        }

        if (context.MovedPieceToEdge)
        {
            boost += 0.03;
        }

        return boost;
    }

    private static string PieceActivityEvidence(MoveHeuristicContext context)
    {
        int mobilityDrop = (context.MovedPieceMobilityBefore ?? 0) - (context.MovedPieceMobilityAfter ?? 0);
        if (context.MovedPieceToEdge && mobilityDrop >= 1)
        {
            return "piece_retreated_to_edge";
        }

        return "reduced_piece_activity";
    }

    private static double ClampConfidence(double confidence)
    {
        return Math.Clamp(confidence, 0.0, 0.98);
    }
}
