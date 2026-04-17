namespace StockifhsGUI;

public sealed class MistakeClassifier
{
    public MistakeTag? Classify(
        ReplayPly replay,
        PlayerSide analyzedSide,
        MoveQualityBucket quality,
        int? centipawnLoss,
        int materialDeltaCp)
    {
        ArgumentNullException.ThrowIfNull(replay);

        if (quality == MoveQualityBucket.Good)
        {
            return null;
        }

        if (materialDeltaCp <= -200)
        {
            return Build("material_loss", 0.90, $"material_delta_{materialDeltaCp}", QualityEvidence(quality));
        }

        if (PositionInspector.IsMovedPieceHanging(replay.FenAfter, replay.ToSquare, analyzedSide)
            && !replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return Build("hanging_piece", 0.92, "moved_piece_is_hanging", QualityEvidence(quality));
        }

        if (IsKingSafetyMistake(replay, analyzedSide, centipawnLoss))
        {
            return Build("king_safety", 0.78, "king_shield_weakened", QualityEvidence(quality));
        }

        if (IsOpeningPrinciplesMistake(replay, centipawnLoss))
        {
            return Build("opening_principles", 0.74, "opening_development_issue", QualityEvidence(quality));
        }

        if (replay.Phase == GamePhase.Endgame)
        {
            return Build("endgame_technique", 0.70, "endgame_phase", QualityEvidence(quality));
        }

        return Build("missed_tactic", 0.65, "engine_prefers_tactical_alternative", QualityEvidence(quality));
    }

    private static bool IsOpeningPrinciplesMistake(ReplayPly replay, int? centipawnLoss)
    {
        if (replay.Phase != GamePhase.Opening || (centipawnLoss ?? 0) < 80)
        {
            return false;
        }

        char movedPiece = char.ToLowerInvariant(replay.MovingPiece[0]);
        char fromFile = replay.FromSquare[0];
        return movedPiece == 'q'
            || (movedPiece == 'p' && (fromFile is 'a' or 'b' or 'g' or 'h'))
            || replay.IsCastle == false && movedPiece == 'k';
    }

    private static bool IsKingSafetyMistake(ReplayPly replay, PlayerSide analyzedSide, int? centipawnLoss)
    {
        if ((centipawnLoss ?? 0) < 120 || !replay.MovingPiece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        char fromFile = replay.FromSquare[0];
        if (fromFile is not ('f' or 'g' or 'h'))
        {
            return false;
        }

        return PositionInspector.IsKingOnCastledWing(replay.FenBefore, analyzedSide);
    }

    private static MistakeTag Build(string label, double confidence, params string[] evidence)
    {
        return new MistakeTag(label, confidence, evidence.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray());
    }

    private static string QualityEvidence(MoveQualityBucket quality)
    {
        return $"quality_{quality.ToString().ToLowerInvariant()}";
    }
}
