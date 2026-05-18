using MoveMentorChess.Engine;

namespace MoveMentorChess.Presentation.Models;

public enum AnalysisSnapshotMode
{
    Played,
    Best,
    Threat
}

public sealed record AnalysisSnapshotArrow(string FromSquare, string ToSquare, string ColorHex);

public static class AnalysisSnapshotPresentation
{
    public static string BuildPositionContextText(MoveAnalysisResult lead, string label)
        => AnalysisSnapshotTextFormatter.BuildPositionContextText(lead, label);

    public static string BuildThreatText(string label)
        => AnalysisSnapshotTextFormatter.BuildThreatText(label);

    public static string BuildSnapshotThreatText(MoveAnalysisResult lead, string label, AnalysisSnapshotMode mode)
    {
        if (mode == AnalysisSnapshotMode.Best)
        {
            return "Best-move view: compare the green arrow with the move you played.";
        }

        if (mode == AnalysisSnapshotMode.Threat)
        {
            EngineLine? threatLine = lead.AfterAnalysis.Lines.FirstOrDefault();
            string threatMove = AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenAfter, threatLine?.MoveUci);
            return threatLine is null
                ? BuildThreatText(label)
                : $"After the played move, the opponent's key reply is {threatMove}.";
        }

        return BuildThreatText(label);
    }

    public static string BuildMissedIdeaText(MoveAnalysisResult lead)
        => AnalysisSnapshotTextFormatter.BuildMissedIdeaText(lead);

    public static string BuildPositionSnapshotText(MoveAnalysisResult lead, string label)
    {
        string material = lead.MaterialDeltaCp == 0
            ? "Material: balanced"
            : $"Material: {AnalysisCoachingTextFormatter.FormatSignedPawns(lead.MaterialDeltaCp)}";
        string kingSquare = PositionInspector.GetKingSquare(lead.Replay.FenAfter, lead.Replay.Side) ?? "unknown";

        return $"{material}\nKing: {kingSquare}\nMain risk: {BuildThreatText(label)}";
    }

    public static IReadOnlyList<AnalysisSnapshotArrow> BuildSnapshotArrows(MoveAnalysisResult lead, AnalysisSnapshotMode mode)
    {
        List<AnalysisSnapshotArrow> arrows = [];

        if (mode == AnalysisSnapshotMode.Played)
        {
            arrows.Add(new AnalysisSnapshotArrow(lead.Replay.FromSquare, lead.Replay.ToSquare, "#D9822B"));
        }

        if (mode is AnalysisSnapshotMode.Played or AnalysisSnapshotMode.Best
            && TryBuildMoveArrow(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci, "#56C271", out AnalysisSnapshotArrow bestArrow))
        {
            arrows.Add(bestArrow);
        }

        EngineLine? threatLine = lead.AfterAnalysis.Lines.FirstOrDefault();
        if (mode == AnalysisSnapshotMode.Threat
            && TryBuildMoveArrow(lead.Replay.FenAfter, threatLine?.MoveUci, "#D84A4A", out AnalysisSnapshotArrow threatArrow))
        {
            arrows.Add(threatArrow);
        }

        return arrows;
    }

    public static string BuildBestMoveIdeaText(MoveAnalysisResult lead)
    {
        string bestMove = AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);
        EngineLine? bestLine = lead.BeforeAnalysis.Lines.FirstOrDefault();
        string note = bestLine is null
            ? "keeps the cleaner position"
            : AnalysisCoachingTextFormatter.BuildCandidateCoachNote(lead, bestLine, isBest: true);
        return $"{bestMove}: {note}.";
    }

    public static string BuildPlayerMistakeText(MoveAnalysisResult lead, string label)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"{AnalysisDetailsTextFormatter.FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} allowed a forced mate.";
        }

        return label switch
        {
            "material_loss" => $"{AnalysisDetailsTextFormatter.FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} left material vulnerable.",
            "hanging_piece" => $"{AnalysisDetailsTextFormatter.FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} left a piece loose.",
            _ => $"{AnalysisDetailsTextFormatter.FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} created a {AnalysisMistakePresentation.FormatMistakeLabel(label).ToLowerInvariant()} problem."
        };
    }

    public static (string Text, string Brush) BuildMovedPieceSafetyBadge(MoveAnalysisResult lead)
    {
        PositionInspector.SquareSafetySummary? safety = PositionInspector.AnalyzeSquareSafety(
            lead.Replay.FenAfter,
            lead.Replay.ToSquare,
            lead.Replay.Side);

        if (safety is null)
        {
            return ("Moved piece status unknown", "#657386");
        }

        if (safety.Value.IsHanging || safety.Value.IsFreeToTake)
        {
            return ("Moved piece hanging", "#B93838");
        }

        if (safety.Value.LikelyLosesExchange || safety.Value.Attackers > safety.Value.Defenders)
        {
            return ("Moved piece under pressure", "#D9822B");
        }

        return ("Moved piece safe", "#1F7A55");
    }

    public static string BuildBeforeMoveChecklistText(string label)
        => AnalysisSnapshotTextFormatter.BuildBeforeMoveChecklistText(label);

    private static bool TryBuildMoveArrow(string fenBefore, string? uciMove, string colorHex, out AnalysisSnapshotArrow arrow)
    {
        arrow = new AnalysisSnapshotArrow("a1", "a1", colorHex);
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return false;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return false;
        }

        arrow = new AnalysisSnapshotArrow(appliedMove.FromSquare, appliedMove.ToSquare, colorHex);
        return true;
    }
}
