using System.Text;
using MoveMentorChess.Analysis;

namespace MoveMentorChess.Presentation.Models;

public static class AnalysisCoachingTextFormatter
{
    public static string BuildEvalSwingText(MoveAnalysisResult lead)
    {
        string before = FormatPawnScore(lead.EvalBeforeCp, lead.BestMateIn);
        string after = FormatPawnScore(lead.EvalAfterCp, lead.PlayedMateIn);
        string swing = lead.EvalBeforeCp is int beforeCp && lead.EvalAfterCp is int afterCp
            ? $", swing {FormatSignedPawns(afterCp - beforeCp)}"
            : string.Empty;
        return $"{before} -> {after}{swing}";
    }

    public static string BuildEvalInterpretation(MoveAnalysisResult lead)
    {
        string mateInterpretation = BuildMateInterpretation(lead);
        if (!string.IsNullOrWhiteSpace(mateInterpretation))
        {
            return mateInterpretation;
        }

        if (lead.EvalBeforeCp is not int before || lead.EvalAfterCp is not int after)
        {
            return "The engine score is mate-based or unavailable, so use the candidate moves below as the main guide.";
        }

        string category = $"Position changed from {DescribeEvaluation(before)} to {DescribeEvaluation(after)}.";
        int swing = after - before;
        if (before > 120 && after < 40)
        {
            return $"{category} Gives back a large part of the advantage.";
        }

        if (before > -80 && after < -180)
        {
            return $"{category} Moves into a clearly worse position.";
        }

        if (Math.Abs(swing) >= 150)
        {
            return $"{category} The swing is tactically significant, so check the opponent's forcing replies.";
        }

        return $"{category} Makes the practical version of the position worse without an immediate collapse.";
    }

    public static string BuildReviewActionText(MoveAnalysisResult lead, string label)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"Next drill: review 3 {AnalysisMistakePresentation.FormatPhase(lead.Replay.Phase)} positions where a natural move allows a forced mate.";
        }

        return label switch
        {
            "hanging_piece" or "material_loss" => $"Review 3 {AnalysisMistakePresentation.FormatPhase(lead.Replay.Phase)} positions where {lead.Replay.San} leaves material loose after a forcing reply.",
            "missed_tactic" => $"Next drill: from move {lead.Replay.MoveNumber}, list checks, captures, and threats before choosing a quiet move.",
            "king_safety" => $"Review 3 positions like move {lead.Replay.MoveNumber} where a move opens a file, diagonal, or forcing check near the king.",
            "opening_principles" => $"Review this opening moment: compare development, king safety, and center control before playing {lead.Replay.San}.",
            "endgame_technique" => $"Next drill: replay move {lead.Replay.MoveNumber} and verify king activity, pawn races, and trades before simplifying.",
            "piece_activity" => $"Review action: before {lead.Replay.San}, identify the least active piece and one improving move.",
            _ => $"Next drill: before move {lead.Replay.MoveNumber}, name the opponent's forcing reply to your candidate."
        };
    }

    public static string BuildTopCandidateMovesText(MoveAnalysisResult lead)
    {
        if (lead.BeforeAnalysis.Lines.Count == 0)
        {
            return "No engine candidate lines are available for this position.";
        }

        StringBuilder builder = new();
        foreach ((EngineLine line, int index) in lead.BeforeAnalysis.Lines.Take(2).Select((line, index) => (line, index)))
        {
            string moveLabel = AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenBefore, line.MoveUci);
            string score = FormatPawnScore(line.Centipawns, line.MateIn);
            string note = index == 0
                ? $"best: {BuildCandidateCoachNote(lead, line, isBest: true)}"
                : BuildCandidateCoachNote(lead, line, isBest: false);
            builder.AppendLine($"{index + 1}. {moveLabel} ({score}) - {note}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildReadableWhyText(MoveAnalysisResult lead, MoveExplanation explanation)
    {
        string detailed = SimplifyAdviceText(explanation.DetailedText);
        if (!string.IsNullOrWhiteSpace(detailed))
        {
            return TakeFirstSentences(detailed, 2);
        }

        string played = AnalysisDetailsTextFormatter.FormatSanAndUci(lead.Replay.San, lead.Replay.Uci);
        string best = AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);
        return lead.CentipawnLoss is int loss
            ? $"{played} gave the opponent a better version of the position. {best} keeps more pressure and avoids the {loss} cp drop."
            : $"{played} made the position harder to play. {best} is the cleaner engine recommendation.";
    }

    public static string SimplifyAdviceText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string result = text.Trim();
        string[] prefixes =
        [
            "Candidate-move check: ",
            "Practical view: ",
            "Calculation note: ",
            "What: ",
            "Speed drill: ",
            "Coach recap: ",
            "Here is the practical idea: ",
            "Levy-style drill: ",
            "Okay, tiny chess crisis, very fixable: ",
            "Stream recap: ",
            "Next-game challenge: "
        ];

        bool changed;
        do
        {
            changed = false;
            foreach (string prefix in prefixes)
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[prefix.Length..].TrimStart();
                    changed = true;
                }
            }
        }
        while (changed);

        return result;
    }

    public static string TakeFirstSentences(string text, int sentenceCount)
    {
        if (string.IsNullOrWhiteSpace(text) || sentenceCount <= 0)
        {
            return string.Empty;
        }

        int found = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                found++;
                if (found >= sentenceCount)
                {
                    return text[..(i + 1)].Trim();
                }
            }
        }

        return text.Trim();
    }

    public static string FormatPawnScore(int? centipawns, int? mateIn)
        => mateIn is int mate ? $"mate {mate}" : centipawns is int cp ? FormatSignedPawns(cp) : "n/a";

    public static string FormatSignedPawns(int centipawns)
    {
        double pawns = centipawns / 100.0;
        return pawns > 0 ? $"+{pawns:0.0}" : $"{pawns:0.0}";
    }

    public static string BuildCandidateCoachNote(MoveAnalysisResult lead, EngineLine line, bool isBest)
    {
        if (line.MateIn is not null)
        {
            return isBest ? "forces the most concrete result" : "playable, but less forcing";
        }

        int? bestScore = lead.BeforeAnalysis.Lines.FirstOrDefault()?.Centipawns;
        int? scoreGap = bestScore is int best && line.Centipawns is int cp ? Math.Abs(best - cp) : null;
        string move = line.MoveUci;
        if (move.Length >= 4)
        {
            string from = move[..2];
            string to = move[2..4];
            if (PositionInspector.CountPieceMobility(lead.Replay.FenBefore, from, lead.Replay.Side) is int beforeMobility
                && TryGetMobilityAfterMove(lead.Replay.FenBefore, move, lead.Replay.Side, to, out int afterMobility)
                && afterMobility > beforeMobility)
            {
                return isBest ? "because it improves piece activity" : "playable and improves activity, but less direct";
            }
        }

        if (lead.Replay.Phase == GamePhase.Opening)
        {
            return isBest ? "because it keeps development and central control on track" : "playable, but less direct for development";
        }

        if (scoreGap is >= 80)
        {
            return "playable, but gives up practical value compared with the best move";
        }

        return isBest ? "because it keeps the cleanest version of the position" : "playable, but slightly less precise";
    }

    private static string BuildMateInterpretation(MoveAnalysisResult lead)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"{FormatSideName(Opponent(lead.Replay.Side))} has a forced mate. Treat this as an urgent king-safety failure, not just an evaluation swing.";
        }

        if (lead.PlayedMateIn is > 0)
        {
            return $"{FormatSideName(lead.Replay.Side)} still has a forced mate, but compare the candidate line to see whether it was the fastest or cleanest route.";
        }

        if (lead.BestMateIn is > 0)
        {
            return $"{FormatSideName(lead.Replay.Side)} had a forced mate available. The played move let that concrete winning line slip.";
        }

        return string.Empty;
    }

    private static string DescribeEvaluation(int centipawns)
    {
        return centipawns switch
        {
            >= 250 => "winning",
            >= 100 => "clearly better",
            >= 40 => "slightly better",
            > -40 => "roughly equal",
            > -100 => "slightly worse",
            > -250 => "worse",
            _ => "lost"
        };
    }

    private static PlayerSide Opponent(PlayerSide side)
        => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;

    private static string FormatSideName(PlayerSide side)
        => side == PlayerSide.White ? "White" : "Black";

    private static bool TryGetMobilityAfterMove(string fenBefore, string uciMove, PlayerSide side, string toSquare, out int mobility)
    {
        mobility = 0;
        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null
            || PositionInspector.CountPieceMobility(appliedMove.FenAfter, toSquare, side) is not int afterMobility)
        {
            return false;
        }

        mobility = afterMobility;
        return true;
    }
}
