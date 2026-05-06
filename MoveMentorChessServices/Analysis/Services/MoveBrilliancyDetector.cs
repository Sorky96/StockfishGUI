namespace MoveMentorChessServices;

public static class MoveBrilliancyDetector
{
    private const int MaxCentipawnLoss = 20;
    private const int MinimumSacrificedPieceValueCp = 300;
    private const int MinimumMaterialSacrificeCp = 300;
    private const int MinimumCompensationCp = -50;

    public static bool IsBrilliant(
        ReplayPly replay,
        int? centipawnLoss,
        int? evalAfterCp,
        int? playedMateIn,
        int materialDeltaCp,
        bool isBookMove,
        MoveHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(replay);

        if (isBookMove
            || centipawnLoss is not int loss
            || loss > MaxCentipawnLoss
            || replay.IsCastle)
        {
            return false;
        }

        if (!HasSoundCompensation(evalAfterCp, playedMateIn))
        {
            return false;
        }

        return HasMaterialSacrifice(materialDeltaCp)
            || LeavesValuablePieceEnPrise(context)
            || PlayedLineAcceptsMaterialDrop(context);
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

    private static bool HasMaterialSacrifice(int materialDeltaCp)
    {
        return materialDeltaCp <= -MinimumMaterialSacrificeCp;
    }

    private static bool LeavesValuablePieceEnPrise(MoveHeuristicContext context)
    {
        return context.MovedPieceValueCp >= MinimumSacrificedPieceValueCp
            && (context.MovedPieceFreeToTake
                || context.MovedPieceLikelyLosesExchange
                || context.MovedPieceHangingAfterMove);
    }

    private static bool PlayedLineAcceptsMaterialDrop(MoveHeuristicContext context)
    {
        return context.PlayedLineMaterialSwingCp <= -MinimumMaterialSacrificeCp;
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
