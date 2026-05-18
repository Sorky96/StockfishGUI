using System.Globalization;
using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;

namespace MoveMentorChess.Presentation.Models;

public sealed class SelectedMistakeViewItem
{
    public SelectedMistakeViewItem(SelectedMistake mistake, GameAnalysisResult analysisResult, bool isReviewed)
    {
        Mistake = mistake;
        LeadMove = AnalysisMistakePresentation.GetLeadMove(mistake);
        MoveRange = AnalysisMistakePresentation.BuildMoveRange(Mistake);
        RawLabel = Mistake.Tag?.Label ?? LeadMove.MistakeTag?.Label ?? "unclassified";
        LabelText = AnalysisMistakePresentation.FormatMistakeLabel(RawLabel);
        LabelBrush = AnalysisMistakePresentation.GetMistakeLabelBrush(RawLabel);
        LabelForeground = AnalysisMistakePresentation.GetMistakeLabelForeground(RawLabel);
        MetaText = $"{Mistake.Quality} | {AnalysisMistakePresentation.BuildImpactText(LeadMove)} | {AnalysisMistakePresentation.FormatPhase(LeadMove.Replay.Phase)}";
        (PriorityText, PriorityReason, PriorityBrush) = AnalysisMistakePresentation.BuildPriorityInfo(Mistake, LeadMove, RawLabel, analysisResult);
        ReviewStatusText = isReviewed ? "Reviewed" : string.Empty;
        ReviewStatusBrush = isReviewed ? "#9ED7A6" : "#657386";
    }

    public SelectedMistake Mistake { get; }

    public MoveAnalysisResult LeadMove { get; }

    public string MoveRange { get; }

    public string LabelText { get; }

    public string RawLabel { get; }

    public string LabelBrush { get; }

    public string LabelForeground { get; }

    public string MetaText { get; }

    public string PriorityText { get; }

    public string PriorityReason { get; }

    public string PriorityBrush { get; }

    public string ReviewStatusText { get; }

    public string ReviewStatusBrush { get; }

    public override string ToString()
        => $"{MoveRange} | {Mistake.Quality} | {LabelText} | {AnalysisMistakePresentation.BuildImpactText(LeadMove)}";
}

public static class AnalysisMistakePresentation
{
    public static MoveAnalysisResult GetLeadMove(SelectedMistake mistake)
        => mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();

    public static string BuildMoveRange(SelectedMistake mistake)
    {
        MoveAnalysisResult first = mistake.Moves.First();
        MoveAnalysisResult last = mistake.Moves.Last();
        string firstMove = $"{first.Replay.MoveNumber}{(first.Replay.Side == PlayerSide.White ? "." : "...")} {first.Replay.San}";
        if (mistake.Moves.Count == 1)
        {
            return firstMove;
        }

        string lastMove = $"{last.Replay.MoveNumber}{(last.Replay.Side == PlayerSide.White ? "." : "...")} {last.Replay.San}";
        return $"{firstMove} -> {lastMove}";
    }

    public static string FormatPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "opening",
            GamePhase.Middlegame => "middlegame",
            GamePhase.Endgame => "endgame",
            _ => phase.ToString()
        };
    }

    public static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose piece",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material loss",
            "piece_activity" => "Passive pieces",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.Replace('_', ' ').ToLowerInvariant())
        };
    }

    public static string BuildImpactText(MoveAnalysisResult lead)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return "forced mate allowed";
        }

        if (lead.BestMateIn is > 0 && lead.PlayedMateIn is null)
        {
            return "winning tactic missed";
        }

        if (lead.BestMateIn is > 0 && lead.PlayedMateIn is > 0)
        {
            return "mate route changed";
        }

        return $"evaluation loss {lead.CentipawnLoss?.ToString() ?? "n/a"} cp";
    }

    public static (string Text, string Reason, string Brush) BuildPriorityInfo(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        string label,
        GameAnalysisResult analysisResult)
    {
        MoveAnalysisResult costliest = analysisResult.HighlightedMistakes
            .Select(GetLeadMove)
            .OrderByDescending(move => move.CentipawnLoss ?? 0)
            .First();
        if (costliest.Replay.Ply == lead.Replay.Ply)
        {
            return ("Costliest", "Start here: this was the largest evaluation loss in the game.", "#8F3F9F");
        }

        if (analysisResult.OpeningReview?.TheoryExit?.Ply == lead.Replay.Ply
            || analysisResult.OpeningReview?.FirstSignificantMistake?.Ply == lead.Replay.Ply)
        {
            return ("Opening turning point", "This move changed the direction of the opening phase.", "#1F7A55");
        }

        int recurringCount = analysisResult.HighlightedMistakes.Count(item =>
            string.Equals(item.Tag?.Label ?? GetLeadMove(item).MistakeTag?.Label ?? "unclassified", label, StringComparison.Ordinal));
        if (recurringCount >= 2)
        {
            return ("Recurring pattern", $"{FormatMistakeLabel(label)} appears {recurringCount} times in this analysis.", "#2F6FB3");
        }

        if (mistake.Quality == MoveQualityBucket.Blunder || (lead.CentipawnLoss ?? 0) >= 150)
        {
            return ("Review first", "High-impact move: review it before smaller inaccuracies.", "#B93838");
        }

        return ("Review later", "Useful, but lower priority than the main turning points.", "#657386");
    }

    public static string GetMistakeLabelBrush(string label)
    {
        return label switch
        {
            "hanging_piece" => "#B93838",
            "material_loss" => "#8F3F9F",
            "missed_tactic" => "#C56A19",
            "opening_principles" => "#1F7A55",
            "king_safety" => "#B88A10",
            "endgame_technique" => "#2F6FB3",
            "piece_activity" => "#4D6B2E",
            _ => "#657386"
        };
    }

    public static string GetMistakeLabelForeground(string label)
    {
        return label switch
        {
            "king_safety" => "#111827",
            _ => "White"
        };
    }
}
