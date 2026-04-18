namespace StockifhsGUI;

public sealed class MistakeSelector
{
    private const int MaxInaccuracies = 3;

    public IReadOnlyList<SelectedMistake> Select(IReadOnlyList<MoveAnalysisResult> moveAnalyses)
    {
        ArgumentNullException.ThrowIfNull(moveAnalyses);

        List<MoveAnalysisResult> ordered = moveAnalyses
            .Where(result => result.Quality != MoveQualityBucket.Good)
            .OrderBy(result => result.Replay.Ply)
            .ToList();

        List<SelectedMistake> grouped = BuildGroups(ordered);
        List<RankedMistake> ranked = grouped
            .Select(group => new RankedMistake(group, ScoreGroup(group)))
            .ToList();

        List<SelectedMistake> selected = ranked
            .Where(item => item.Mistake.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake)
            .Select(item => item.Mistake)
            .ToList();

        List<RankedMistake> rankedInaccuracies = ranked
            .Where(item => item.Mistake.Quality == MoveQualityBucket.Inaccuracy)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Mistake.Moves.First().Replay.Ply)
            .ToList();
        List<SelectedMistake> topInaccuracies = SelectTopInaccuracies(rankedInaccuracies);

        selected.AddRange(topInaccuracies);

        return selected
            .Distinct()
            .OrderBy(item => item.Moves.First().Replay.Ply)
            .ToList();
    }

    private static List<SelectedMistake> BuildGroups(IReadOnlyList<MoveAnalysisResult> ordered)
    {
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
            ?? new MoveExplanation(
                "This move was selected as one of the most relevant mistakes in the game.",
                "Review the forcing replies in the position before moving.",
                "This moment was highlighted because it had one of the biggest practical impacts on the game. Compare the played move with the engine's calmer continuation and focus on the first forcing reply you missed.");

        return new SelectedMistake(group, lead.Quality, lead.MistakeTag, explanation);
    }

    private static List<SelectedMistake> SelectTopInaccuracies(IReadOnlyList<RankedMistake> rankedInaccuracies)
    {
        List<SelectedMistake> selected = new();
        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        HashSet<GamePhase> phases = [];

        foreach (RankedMistake candidate in rankedInaccuracies)
        {
            if (selected.Count >= MaxInaccuracies)
            {
                break;
            }

            string label = candidate.Mistake.Tag?.Label ?? "unclassified";
            GamePhase phase = GetDominantPhase(candidate.Mistake);
            bool duplicateLabel = labels.Contains(label);
            bool duplicatePhase = phases.Contains(phase);

            if (duplicateLabel || duplicatePhase)
            {
                continue;
            }

            selected.Add(candidate.Mistake);
            labels.Add(label);
            phases.Add(phase);
        }

        foreach (RankedMistake candidate in rankedInaccuracies)
        {
            if (selected.Count >= MaxInaccuracies)
            {
                break;
            }

            if (selected.Contains(candidate.Mistake))
            {
                continue;
            }

            string label = candidate.Mistake.Tag?.Label ?? "unclassified";
            bool duplicateLabel = labels.Contains(label);
            int bestSelectedScore = selected.Count == 0
                ? 0
                : selected.Max(mistake => rankedInaccuracies.First(item => ReferenceEquals(item.Mistake, mistake)).Score);
            bool significantlyStronger = bestSelectedScore - candidate.Score <= 90;

            if (duplicateLabel && !significantlyStronger)
            {
                continue;
            }

            selected.Add(candidate.Mistake);
            labels.Add(label);
        }

        return selected;
    }

    private static int ScoreGroup(SelectedMistake mistake)
    {
        MoveAnalysisResult lead = mistake.Moves
            .OrderByDescending(item => item.Quality)
            .ThenByDescending(item => item.CentipawnLoss ?? 0)
            .First();

        int qualityWeight = lead.Quality switch
        {
            MoveQualityBucket.Blunder => 1000,
            MoveQualityBucket.Mistake => 650,
            MoveQualityBucket.Inaccuracy => 250,
            _ => 0
        };

        int cplWeight = Math.Min(400, lead.CentipawnLoss ?? 0);
        int tagWeight = TagWeight(lead.MistakeTag?.Label);
        int confidenceWeight = (int)Math.Round((lead.MistakeTag?.Confidence ?? 0.0) * 60);
        int groupWeight = Math.Max(0, mistake.Moves.Count - 1) * 25;
        int criticalWeight = IsCriticalMoment(lead) ? 140 : 0;
        int mateWeight = lead.PlayedMateIn is < 0 || (lead.BestMateIn is > 0 && lead.PlayedMateIn is null)
            ? 220
            : 0;
        int practicalSwingWeight = PracticalSwingWeight(lead);
        int materialWeight = lead.MaterialDeltaCp < 0
            ? Math.Min(120, Math.Abs(lead.MaterialDeltaCp))
            : 0;

        return qualityWeight + cplWeight + tagWeight + confidenceWeight + groupWeight + criticalWeight + mateWeight + practicalSwingWeight + materialWeight;
    }

    private static bool IsCriticalMoment(MoveAnalysisResult result)
    {
        if (result.PlayedMateIn is < 0)
        {
            return true;
        }

        if (result.BestMateIn is > 0 && result.PlayedMateIn is null)
        {
            return true;
        }

        return result.EvalBeforeCp is int before
            && Math.Abs(before) <= 80
            && (result.CentipawnLoss ?? 0) >= 120;
    }

    private static int TagWeight(string? label)
    {
        return label switch
        {
            "material_loss" => 230,
            "hanging_piece" => 190,
            "king_safety" => 180,
            "missed_tactic" => 150,
            "endgame_technique" => 100,
            "piece_activity" => 95,
            "opening_principles" => 80,
            _ => 0
        };
    }

    private static int PracticalSwingWeight(MoveAnalysisResult result)
    {
        if (result.EvalBeforeCp is not int before || result.EvalAfterCp is not int after)
        {
            return 0;
        }

        int swing = Math.Abs(after - before);
        int closenessBonus = Math.Max(0, 120 - Math.Abs(before)) / 2;
        return Math.Min(180, (swing / 4) + closenessBonus);
    }

    private static GamePhase GetDominantPhase(SelectedMistake mistake)
    {
        return mistake.Moves
            .GroupBy(item => item.Replay.Phase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private sealed record RankedMistake(SelectedMistake Mistake, int Score);
}
