using System.Text;
using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;
using MoveMentorChess.Opening;

namespace MoveMentorChess.Presentation.Models;

public static class AnalysisDetailsTextFormatter
{
    public static string BuildDetailsText(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        OpeningPhaseReview? openingReview,
        MoveExplanation explanation,
        bool isLoading,
        MoveAdviceFeedback? feedback)
    {
        StringBuilder builder = new();
        string effectiveLabel = feedback?.CorrectedLabel ?? mistake.Tag?.Label ?? "unclassified";
        builder.AppendLine("Move facts:");
        builder.AppendLine($"Quality: {mistake.Quality}");
        builder.AppendLine($"Label: {AnalysisMistakePresentation.FormatMistakeLabel(effectiveLabel)}");
        if (feedback is not null)
        {
            builder.AppendLine($"Original label: {AnalysisMistakePresentation.FormatMistakeLabel(feedback.OriginalLabel ?? "unclassified")}");
            builder.AppendLine($"Manual feedback: {feedback.FeedbackKind}");
            if (!string.IsNullOrWhiteSpace(feedback.CorrectedLabel))
            {
                builder.AppendLine($"Manual/effective label: {AnalysisMistakePresentation.FormatMistakeLabel(feedback.CorrectedLabel)}");
            }

            if (!string.IsNullOrWhiteSpace(feedback.Comment))
            {
                builder.AppendLine($"Manual comment: {feedback.Comment}");
            }
        }

        builder.AppendLine($"Confidence: {(mistake.Tag?.Confidence ?? 0):0.00}");
        builder.AppendLine($"Phase: {lead.Replay.Phase}");
        builder.AppendLine($"Played move: {FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)}");
        builder.AppendLine($"Best move: {FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci)}");
        builder.AppendLine($"Eval before: {FormatScore(lead.EvalBeforeCp, lead.BestMateIn)}");
        builder.AppendLine($"Eval after: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
        builder.AppendLine($"Centipawn loss: {(lead.CentipawnLoss?.ToString() ?? "n/a")}");
        builder.AppendLine($"Material delta: {lead.MaterialDeltaCp}");

        if (openingReview is not null && lead.Replay.Phase == GamePhase.Opening)
        {
            builder.AppendLine();
            builder.AppendLine("Opening review:");
            builder.AppendLine($"Branch: {openingReview.Branch.BranchLabel}");

            if (openingReview.TheoryExit?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is marked as the theory exit: {openingReview.TheoryExit.Trigger}");
            }

            if (openingReview.FirstSignificantMistake?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is the first significant opening mistake: {openingReview.FirstSignificantMistake.Trigger}");
            }
        }

        if (isLoading)
        {
            builder.AppendLine();
            builder.AppendLine("Local advice model is generating a richer explanation in the background...");
        }

        if (mistake.Tag?.Evidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Evidence:");
            foreach (string evidence in mistake.Tag.Evidence)
            {
                builder.AppendLine($"- {evidence}");
            }
        }

        if (lead.BeforeAnalysis.Lines.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Engine candidates:");
            builder.AppendLine(FormatEngineCandidates(lead.Replay.FenBefore, lead.BeforeAnalysis.Lines));
        }

        EngineLine? playedContinuation = lead.AfterAnalysis.Lines.FirstOrDefault();
        if (playedContinuation is not null && playedContinuation.Pv.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Likely continuation after played move:");
            builder.AppendLine($"Score: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
            builder.AppendLine(FormatPrincipalVariation(lead.Replay.FenAfter, playedContinuation.Pv, maxHalfMoves: 8));
        }

        builder.AppendLine();
        builder.AppendLine("Board navigation:");
        builder.AppendLine("Use 'Show On Board' to jump to this position in the main window.");

        return builder.ToString().TrimEnd();
    }

    public static string FormatScore(int? centipawns, int? mateIn)
        => mateIn is int mate ? $"mate {mate}" : centipawns is int cp ? $"{cp} cp" : "n/a";

    public static string FormatMoveFromFen(string fenBefore, string? uciMove)
    {
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return "(unknown)";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return uciMove;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    public static string FormatSanAndUci(string san, string uci)
        => string.Equals(san, uci, StringComparison.OrdinalIgnoreCase) ? san : $"{san} ({uci})";

    public static string FormatEngineCandidates(string fenBefore, IReadOnlyList<EngineLine> lines)
    {
        StringBuilder builder = new();

        for (int i = 0; i < lines.Count; i++)
        {
            EngineLine line = lines[i];
            string moveLabel = FormatMoveFromFen(fenBefore, line.MoveUci);
            string pv = FormatPrincipalVariation(fenBefore, line.Pv, maxHalfMoves: 8);
            builder.AppendLine($"{i + 1}. {moveLabel} | {FormatScore(line.Centipawns, line.MateIn)}");
            if (!string.IsNullOrWhiteSpace(pv))
            {
                builder.AppendLine($"   PV: {pv}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatPrincipalVariation(string fenBefore, IReadOnlyList<string> pv, int maxHalfMoves)
    {
        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _))
        {
            return string.Join(' ', pv.Take(maxHalfMoves));
        }

        List<string> formattedMoves = new(Math.Min(pv.Count, maxHalfMoves));
        foreach (string uciMove in pv.Take(maxHalfMoves))
        {
            if (!game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _) || appliedMove is null)
            {
                formattedMoves.Add(uciMove);
                continue;
            }

            formattedMoves.Add($"{appliedMove.MoveNumber}{(appliedMove.WhiteMoved ? "." : "...")} {appliedMove.San}");
        }

        if (pv.Count > maxHalfMoves)
        {
            formattedMoves.Add("...");
        }

        return string.Join(" ", formattedMoves);
    }
}
