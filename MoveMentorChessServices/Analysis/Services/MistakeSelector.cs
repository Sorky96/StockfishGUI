namespace MoveMentorChessServices;

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

        foreach (MoveAnalysisResult result in ordered)
        {
            bool canMerge = currentGroup.Count > 0
                && CanMergeIntoCurrentGroup(currentGroup, result);

            if (!canMerge && currentGroup.Count > 0)
            {
                grouped.Add(BuildGroup(currentGroup));
                currentGroup = new List<MoveAnalysisResult>();
            }

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
            .OrderByDescending(LeadScore)
            .ThenBy(item => item.Replay.Ply)
            .First();

        MoveExplanation explanation = lead.Explanation
            ?? new MoveExplanation(
                "This move was selected as one of the most relevant mistakes in the game.",
                "Review the forcing replies in the position before moving.",
                "This moment was highlighted because it had one of the biggest practical impacts on the game. Compare the played move with the engine's calmer continuation and focus on the first forcing reply you missed.");

        return new SelectedMistake(group, lead.Quality, lead.MistakeTag, explanation);
    }

    private static bool CanMergeIntoCurrentGroup(IReadOnlyList<MoveAnalysisResult> currentGroup, MoveAnalysisResult candidate)
    {
        MoveAnalysisResult last = currentGroup[^1];
        int gap = candidate.Replay.Ply - last.Replay.Ply;
        if (gap < 2 || gap > 4)
        {
            return false;
        }

        string lastLabel = last.MistakeTag?.Label ?? "unclassified";
        string candidateLabel = candidate.MistakeTag?.Label ?? "unclassified";
        if (gap == 2 && string.Equals(lastLabel, candidateLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string currentFamily = MotifFamily(currentGroup[0].MistakeTag?.Label);
        string candidateFamily = MotifFamily(candidate.MistakeTag?.Label);
        if (!string.Equals(currentFamily, candidateFamily, StringComparison.Ordinal))
        {
            return false;
        }

        if (GetDominantPhase(BuildGroup(currentGroup)) != candidate.Replay.Phase
            && last.Replay.Phase != candidate.Replay.Phase)
        {
            return false;
        }

        return !HasMeaningfulRecovery(last, candidate);
    }

    private static List<SelectedMistake> SelectTopInaccuracies(IReadOnlyList<RankedMistake> rankedInaccuracies)
    {
        List<SelectedMistake> selected = new();
        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> motifFamilies = new(StringComparer.OrdinalIgnoreCase);
        HashSet<GamePhase> phases = [];

        foreach (RankedMistake candidate in rankedInaccuracies)
        {
            if (selected.Count >= MaxInaccuracies)
            {
                break;
            }

            string label = candidate.Mistake.Tag?.Label ?? "unclassified";
            string motifFamily = MotifFamily(label);
            GamePhase phase = GetDominantPhase(candidate.Mistake);
            bool duplicateLabel = labels.Contains(label);
            bool duplicateMotif = motifFamilies.Contains(motifFamily);
            bool duplicatePhase = phases.Contains(phase);

            if (duplicateLabel || duplicateMotif || duplicatePhase || HasNearbyNarrative(selected, candidate.Mistake))
            {
                continue;
            }

            selected.Add(candidate.Mistake);
            labels.Add(label);
            motifFamilies.Add(motifFamily);
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
            string motifFamily = MotifFamily(label);
            bool duplicateLabel = labels.Contains(label);
            bool duplicateMotif = motifFamilies.Contains(motifFamily);
            int bestSelectedScore = selected.Count == 0
                ? 0
                : selected.Max(mistake => rankedInaccuracies.First(item => ReferenceEquals(item.Mistake, mistake)).Score);
            bool significantlyStronger = bestSelectedScore - candidate.Score <= 90;

            if ((duplicateLabel || duplicateMotif) && !significantlyStronger)
            {
                continue;
            }

            if (HasNearbyNarrative(selected, candidate.Mistake) && !significantlyStronger)
            {
                continue;
            }

            selected.Add(candidate.Mistake);
            labels.Add(label);
            motifFamilies.Add(motifFamily);
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
        int educationalPenalty = EducationalPenalty(lead);
        int conversionWeight = ConversionWeight(lead);
        int lostPositionPenalty = LostPositionPenalty(lead);

        return qualityWeight + cplWeight + tagWeight + confidenceWeight + groupWeight + criticalWeight + mateWeight + practicalSwingWeight + materialWeight + conversionWeight - educationalPenalty - lostPositionPenalty;
    }

    private static int LeadScore(MoveAnalysisResult result)
    {
        int qualityWeight = result.Quality switch
        {
            MoveQualityBucket.Blunder => 1000,
            MoveQualityBucket.Mistake => 650,
            MoveQualityBucket.Inaccuracy => 250,
            _ => 0
        };

        int cplWeight = Math.Min(400, result.CentipawnLoss ?? 0);
        int turningPointWeight = IsTurningPoint(result) ? 140 : 0;
        int practicalWeight = PracticalSwingWeight(result);
        int conversionWeight = ConversionWeight(result);

        return qualityWeight + cplWeight + turningPointWeight + practicalWeight + conversionWeight;
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

    private static int ConversionWeight(MoveAnalysisResult result)
    {
        if (result.EvalBeforeCp is not int before || result.EvalAfterCp is not int after)
        {
            return 0;
        }

        if (before >= 120 && after <= 40)
        {
            return 110;
        }

        if (before >= 220 && after < before - 140)
        {
            return 80;
        }

        if (Math.Abs(before) <= 60 && after <= -120)
        {
            return 90;
        }

        return 0;
    }

    private static int LostPositionPenalty(MoveAnalysisResult result)
    {
        if (result.EvalBeforeCp is not int before || result.EvalAfterCp is not int after)
        {
            return 0;
        }

        bool alreadyClearlyLost = before <= -260;
        bool stillClearlyLost = after <= -220;
        bool noMeaningfulRecovery = after <= before + 80;

        if (!alreadyClearlyLost || !stillClearlyLost || !noMeaningfulRecovery)
        {
            return 0;
        }

        return result.Quality switch
        {
            MoveQualityBucket.Inaccuracy => 150,
            MoveQualityBucket.Mistake => 90,
            _ => 0
        };
    }

    private static int EducationalPenalty(MoveAnalysisResult result)
    {
        string label = result.MistakeTag?.Label ?? "unclassified";
        if (!string.Equals(label, "unclassified", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return result.Quality switch
        {
            MoveQualityBucket.Inaccuracy => 120,
            MoveQualityBucket.Mistake => 60,
            _ => 0
        };
    }

    private static bool IsTurningPoint(MoveAnalysisResult result)
    {
        if (result.EvalBeforeCp is not int before || result.EvalAfterCp is not int after)
        {
            return false;
        }

        if (before >= 120 && after <= 40)
        {
            return true;
        }

        if (Math.Abs(before) <= 80 && after <= -100)
        {
            return true;
        }

        return after <= before - 120;
    }

    private static bool HasMeaningfulRecovery(MoveAnalysisResult previous, MoveAnalysisResult candidate)
    {
        if (previous.EvalAfterCp is not int previousAfter || candidate.EvalBeforeCp is not int candidateBefore)
        {
            return false;
        }

        return candidateBefore >= previousAfter + 100;
    }

    private static bool HasNearbyNarrative(IReadOnlyList<SelectedMistake> selected, SelectedMistake candidate)
    {
        if (selected.Count == 0)
        {
            return false;
        }

        MoveAnalysisResult candidateLead = candidate.Moves
            .OrderBy(item => item.Replay.Ply)
            .First();
        string candidateFamily = MotifFamily(candidate.Tag?.Label);

        foreach (SelectedMistake existing in selected)
        {
            MoveAnalysisResult existingLast = existing.Moves
                .OrderBy(item => item.Replay.Ply)
                .Last();
            string existingFamily = MotifFamily(existing.Tag?.Label);
            int gap = candidateLead.Replay.Ply - existingLast.Replay.Ply;
            if (gap >= 0
                && gap <= 4
                && string.Equals(existingFamily, candidateFamily, StringComparison.Ordinal)
                && !HasMeaningfulRecovery(existingLast, candidateLead))
            {
                return true;
            }
        }

        return false;
    }

    private static string MotifFamily(string? label)
    {
        return label switch
        {
            "material_loss" or "hanging_piece" => "material_damage",
            "opening_principles" => "opening_principles",
            "king_safety" => "king_safety",
            "missed_tactic" => "missed_tactic",
            "piece_activity" => "piece_activity",
            "endgame_technique" => "endgame_technique",
            _ => label ?? "unclassified"
        };
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
