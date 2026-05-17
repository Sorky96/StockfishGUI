namespace MoveMentorChess.Presentation.Models;

public static class AnalysisSnapshotTextFormatter
{
    public static string BuildPositionContextText(MoveAnalysisResult lead, string label)
        => $"Phase: {AnalysisMistakePresentation.FormatPhase(lead.Replay.Phase)}\nMotif: {AnalysisMistakePresentation.FormatMistakeLabel(label)}\nThreat after move: {BuildThreatText(label)}\nMissed idea: {BuildMissedIdeaText(lead)}";

    public static string BuildThreatText(string label)
    {
        return label switch
        {
            "hanging_piece" or "material_loss" => "the opponent can win material or keep a loose piece under pressure",
            "missed_tactic" => "the opponent may have a forcing reply",
            "king_safety" => "king safety and forcing checks become more important",
            "opening_principles" => "development or central control falls behind",
            "endgame_technique" => "the technical conversion becomes harder",
            "piece_activity" => "pieces lose coordination or active squares",
            _ => "the opponent gets an easier plan"
        };
    }

    public static string BuildMissedIdeaText(MoveAnalysisResult lead)
        => AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);

    public static string BuildBeforeMoveChecklistText(string label)
    {
        string thirdQuestion = label switch
        {
            "king_safety" => "3. Does either king get a new attacking line?",
            "opening_principles" => "3. Am I developing a piece and fighting for the center?",
            "endgame_technique" => "3. After trades, do I keep an active king or passed pawn?",
            _ => "3. What does my move change about king safety?"
        };

        return $"1. Is anything hanging?\n2. Does the opponent have a forcing move?\n{thirdQuestion}";
    }
}
