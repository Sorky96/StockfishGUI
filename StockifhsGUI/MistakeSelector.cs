namespace StockifhsGUI;

public sealed class MistakeSelector
{
    public IReadOnlyList<SelectedMistake> Select(IReadOnlyList<MoveAnalysisResult> moveAnalyses)
    {
        ArgumentNullException.ThrowIfNull(moveAnalyses);

        List<MoveAnalysisResult> selected = moveAnalyses
            .Where(result => result.Quality is MoveQualityBucket.Mistake or MoveQualityBucket.Blunder)
            .ToList();

        List<MoveAnalysisResult> topInaccuracies = moveAnalyses
            .Where(result => result.Quality == MoveQualityBucket.Inaccuracy)
            .OrderByDescending(result => result.CentipawnLoss ?? 0)
            .Take(3)
            .ToList();

        selected.AddRange(topInaccuracies);

        List<MoveAnalysisResult> ordered = selected
            .Distinct()
            .OrderBy(result => result.Replay.Ply)
            .ToList();

        List<SelectedMistake> grouped = new();
        List<MoveAnalysisResult> currentGroup = new();
        string? currentLabel = null;

        foreach (MoveAnalysisResult result in ordered)
        {
            string label = result.MistakeTag?.Label ?? "unclassified";
            bool canMerge = currentGroup.Count > 0
                && currentLabel == label
                && result.Replay.Ply - currentGroup[^1].Replay.Ply == 2;

            if (!canMerge && currentGroup.Count > 0)
            {
                grouped.Add(BuildGroup(currentGroup));
                currentGroup = new List<MoveAnalysisResult>();
            }

            currentLabel = label;
            currentGroup.Add(result);
        }

        if (currentGroup.Count > 0)
        {
            grouped.Add(BuildGroup(currentGroup));
        }

        return grouped;
    }

    private static SelectedMistake BuildGroup(IReadOnlyList<MoveAnalysisResult> group)
    {
        MoveAnalysisResult lead = group
            .OrderByDescending(item => item.Quality)
            .ThenByDescending(item => item.CentipawnLoss ?? 0)
            .First();

        MoveExplanation explanation = lead.Explanation
            ?? new MoveExplanation("This move was selected as one of the most relevant mistakes in the game.", "Review the forcing replies in the position before moving.");

        return new SelectedMistake(group, lead.Quality, lead.MistakeTag, explanation);
    }
}
