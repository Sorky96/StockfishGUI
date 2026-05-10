namespace MoveMentorChess.Opening;

public static class OpeningMoveIdeaHeuristics
{
    public static OpeningMoveIdea Build(string moveSan, bool isMainMove)
    {
        List<OpeningMoveIdeaTag> tags = [];
        string normalized = moveSan.Trim();
        string explanation;

        if (normalized.StartsWith("O-O", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.KingSafety);
            explanation = "Castling improves king safety and connects the rooks.";
        }
        else if (normalized.StartsWith("N", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.DevelopPiece);
            explanation = "This develops a piece to a more active square.";
        }
        else if (normalized.StartsWith("c4", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("d4", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("e4", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("c5", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("d5", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("e5", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.ControlCenter);
            explanation = "This move fights for central space and influence.";
        }
        else if (normalized.Contains("h6", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("a6", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.PreventThreat);
            explanation = "This limits an opponent idea before it becomes annoying.";
        }
        else if (normalized.Contains("f4", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("c4", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("d4", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("...d5", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.PrepareBreak);
            explanation = "This prepares an important pawn break for the position.";
        }
        else
        {
            tags.Add(OpeningMoveIdeaTag.ImproveWorstPiece);
            explanation = "This improves coordination without creating new weaknesses.";
        }

        if (normalized.Contains("+", StringComparison.Ordinal)
            || normalized.Contains("x", StringComparison.Ordinal))
        {
            tags.Add(OpeningMoveIdeaTag.TacticalResource);
        }

        if (isMainMove)
        {
            tags.Add(OpeningMoveIdeaTag.MainTheoreticalMove);
            explanation = $"{explanation} It is also the main theoretical move here.";
        }

        return new OpeningMoveIdea(
            normalized,
            tags.Distinct().ToList(),
            explanation);
    }
}
