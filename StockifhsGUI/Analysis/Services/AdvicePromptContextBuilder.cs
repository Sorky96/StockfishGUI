using System.Collections.Generic;

namespace StockifhsGUI;

public static class AdvicePromptContextBuilder
{
    public static AdvicePromptContext Build(
        ImportedGame game,
        ReplayPly replay,
        PlayerSide analyzedSide,
        MistakeTag? tag,
        string? bestMoveUci,
        MoveHeuristicContext heuristicContext,
        PlayerMistakeProfile? playerProfile = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(heuristicContext);

        return new AdvicePromptContext(
            OpeningName: DescribeOpening(game.Eco),
            AnalyzedPlayer: analyzedSide == PlayerSide.White ? game.WhitePlayer : game.BlackPlayer,
            OpponentPlayer: analyzedSide == PlayerSide.White ? game.BlackPlayer : game.WhitePlayer,
            BestMoveSan: FormatBestMoveFromFen(replay.FenBefore, bestMoveUci),
            Evidence: BuildEvidence(tag),
            HeuristicNotes: BuildHeuristicNotes(replay, heuristicContext),
            PlayerProfile: playerProfile);
    }

    private static string? DescribeOpening(string? eco)
    {
        return string.IsNullOrWhiteSpace(eco) ? null : OpeningCatalog.Describe(eco);
    }

    private static string? FormatBestMoveFromFen(string fenBefore, string? bestMoveUci)
    {
        if (string.IsNullOrWhiteSpace(bestMoveUci))
        {
            return null;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(bestMoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return bestMoveUci;
        }

        return ChessMoveDisplayHelper.FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static IReadOnlyList<string> BuildEvidence(MistakeTag? tag)
    {
        if (tag?.Evidence is null || tag.Evidence.Count == 0)
        {
            return [];
        }

        List<string> evidence = new();
        foreach (string item in tag.Evidence)
        {
            evidence.Add(item switch
            {
                "early_queen_move" => "the queen moved early before development was complete",
                "early_rook_move" => "a rook moved early without a concrete payoff",
                "wing_pawn_before_development" => "a wing pawn move spent time before core development",
                "missed_king_centralization" => "the king could have become more active",
                "missed_king_activation" => "a more active king plan was available",
                "king_left_castled_shelter" => "the king moved away from its castled shelter",
                "king_retreated_to_edge" => "the king stepped toward the edge and became less active",
                "reduced_king_activity" => "king activity dropped in an endgame where activity mattered",
                "missed_piece_activation" => "a more active piece setup was available",
                "king_stayed_passive" => "king activity remained too passive for the phase",
                "piece_lost_or_underdefended" => "the moved piece became loose or tactically vulnerable",
                "material_swing_detected" => "the line allowed a significant material swing",
                "missed_capture_sequence" => "a forcing capture sequence was available but missed",
                _ => item.Replace('_', ' ')
            });
        }

        return evidence;
    }

    private static IReadOnlyList<string> BuildHeuristicNotes(ReplayPly replay, MoveHeuristicContext heuristicContext)
    {
        List<string> notes = new();

        if (heuristicContext.MovedPieceHangingAfterMove)
        {
            notes.Add("the moved piece became hanging on its destination square");
        }

        if (heuristicContext.MovedPieceMobilityBefore is int before
            && heuristicContext.MovedPieceMobilityAfter is int after
            && after < before)
        {
            notes.Add("the move reduced the mobility of the moved piece");
        }

        if (replay.Phase == GamePhase.Opening
            && heuristicContext.DevelopedMinorPiecesAfter <= heuristicContext.DevelopedMinorPiecesBefore)
        {
            notes.Add("development did not improve after the move");
        }

        if (heuristicContext.BestMoveIsCapture)
        {
            notes.Add("the strongest alternative was forcing and involved a capture");
        }

        if (heuristicContext.BestMoveCentralizesKing)
        {
            notes.Add("the stronger line improved king activity");
        }

        if (heuristicContext.BestMoveImprovesPieceActivity)
        {
            notes.Add("the stronger line improved piece activity instead of keeping the position static");
        }

        if (heuristicContext.CastledBeforeMove && heuristicContext.CastledKingWingPawnPush)
        {
            notes.Add("the move loosened the pawn cover in front of a castled king");
        }

        if (heuristicContext.KingLeftCastledShelter)
        {
            notes.Add("the king moved away from its castled shelter and gave up safety");
        }

        if (heuristicContext.MovedPieceToEdge && replay.MovingPiece is "N" or "n" or "B" or "b" or "Q" or "q")
        {
            notes.Add("the moved piece drifted toward the edge instead of improving central influence");
        }

        return notes;
    }
}
