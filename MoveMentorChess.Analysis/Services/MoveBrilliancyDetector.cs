namespace MoveMentorChess.Analysis;

public static class MoveBrilliancyDetector
{
    private const int MaxCentipawnLoss = 20;
    private const int MinimumMaterialSacrificeCp = 300;
    private const int MinimumCompensationCp = -50;
    private const int MinimumMaterialRecoveryGainCp = 100;

    public static bool IsBrilliant(
        ReplayPly replay,
        int? centipawnLoss,
        int? evalAfterCp,
        int? playedMateIn,
        int materialDeltaCp,
        bool isBookMove,
        EngineAnalysis afterAnalysis,
        PlayerSide analyzedSide,
        MoveHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(afterAnalysis);

        if (isBookMove
            || centipawnLoss is not int loss
            || loss > MaxCentipawnLoss)
        {
            return false;
        }

        if (!HasSoundCompensation(evalAfterCp, playedMateIn))
        {
            return false;
        }

        int sacrificeDepthCp = ComputeSacrificeDepth(replay, materialDeltaCp, afterAnalysis, analyzedSide, context);
        if (sacrificeDepthCp < MinimumMaterialSacrificeCp)
        {
            return false;
        }

        return HasCheckingCompensation(replay, playedMateIn)
            || HasMaterialRecoveryCompensation(afterAnalysis, analyzedSide, sacrificeDepthCp);
    }

    public static bool IsBrilliant(
        MoveAnalysisResult result,
        PlayerSide analyzedSide,
        bool isBookMove = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        MoveHeuristicContext context = BuildContext(result, analyzedSide);
        return IsBrilliant(
            result.Replay,
            result.CentipawnLoss,
            result.EvalAfterCp,
            result.PlayedMateIn,
            result.MaterialDeltaCp,
            isBookMove,
            result.AfterAnalysis,
            analyzedSide,
            context);
    }

    private static bool HasSoundCompensation(int? evalAfterCp, int? playedMateIn)
    {
        if (playedMateIn is > 0)
        {
            return true;
        }

        return evalAfterCp is int after && after >= MinimumCompensationCp;
    }

    private static int ComputeSacrificeDepth(
        ReplayPly replay,
        int materialDeltaCp,
        EngineAnalysis afterAnalysis,
        PlayerSide analyzedSide,
        MoveHeuristicContext context)
    {
        int immediateSacrificeCp = Math.Max(0, -materialDeltaCp);
        int firstReplySacrificeCp = !replay.IsCapture
            ? ComputeFirstReplyMaterialLoss(afterAnalysis, analyzedSide)
            : 0;
        int enPriseSacrificeCp = !replay.IsCapture && LeavesValuablePieceEnPrise(context)
            ? context.MovedPieceValueCp ?? 0
            : 0;

        return Math.Max(Math.Max(immediateSacrificeCp, firstReplySacrificeCp), enPriseSacrificeCp);
    }

    private static int ComputeFirstReplyMaterialLoss(EngineAnalysis afterAnalysis, PlayerSide analyzedSide)
    {
        EngineLine? playedLine = afterAnalysis.Lines.FirstOrDefault();
        string? firstReply = playedLine?.Pv.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstReply))
        {
            return 0;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(afterAnalysis.Fen, out _)
            || !game.TryApplyUci(firstReply, out AppliedMoveInfo? reply, out _)
            || reply is null)
        {
            return 0;
        }

        int before = PositionInspector.MaterialScore(afterAnalysis.Fen, analyzedSide);
        int after = PositionInspector.MaterialScore(reply.FenAfter, analyzedSide);
        return Math.Max(0, before - after);
    }

    private static bool LeavesValuablePieceEnPrise(MoveHeuristicContext context)
    {
        return context.MovedPieceValueCp >= MinimumMaterialSacrificeCp
            && (context.MovedPieceFreeToTake
                || context.MovedPieceLikelyLosesExchange
                || context.MovedPieceHangingAfterMove);
    }

    private static bool HasCheckingCompensation(ReplayPly replay, int? playedMateIn)
    {
        return playedMateIn is > 0
            || replay.San.Contains('+', StringComparison.Ordinal)
            || replay.San.Contains('#', StringComparison.Ordinal);
    }

    private static bool HasMaterialRecoveryCompensation(
        EngineAnalysis afterAnalysis,
        PlayerSide analyzedSide,
        int sacrificeDepthCp)
    {
        EngineLine? playedLine = afterAnalysis.Lines.FirstOrDefault();
        PositionInspector.MaterialSwingSummary? swing = PositionInspector.AnalyzeMaterialSwingAlongLine(
            afterAnalysis.Fen,
            analyzedSide,
            playedLine?.Pv,
            maxPlies: 8);

        return swing is not null
            && swing.Value.BestDeltaCp - swing.Value.WorstDeltaCp >= sacrificeDepthCp + MinimumMaterialRecoveryGainCp;
    }

    private static MoveHeuristicContext BuildContext(MoveAnalysisResult result, PlayerSide analyzedSide)
    {
        PositionInspector.SquareSafetySummary? movedPieceSafety = PositionInspector.AnalyzeSquareSafety(
            result.Replay.FenAfter,
            result.Replay.ToSquare,
            analyzedSide);
        PositionInspector.MaterialSwingSummary? playedLineSwing = PositionInspector.AnalyzeMaterialSwingAlongLine(
            result.Replay.FenAfter,
            analyzedSide,
            result.AfterAnalysis.Lines.FirstOrDefault()?.Pv);

        return new MoveHeuristicContext(
            movedPieceSafety?.IsHanging == true,
            movedPieceSafety?.IsFreeToTake == true,
            movedPieceSafety?.LikelyLosesExchange == true,
            movedPieceSafety is null ? 0 : movedPieceSafety.Value.Attackers - movedPieceSafety.Value.Defenders,
            movedPieceSafety?.PieceValueCp,
            null,
            null,
            PositionInspector.IsEdgeSquare(result.Replay.ToSquare),
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            playedLineSwing?.WorstDeltaCp,
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            false);
    }
}
