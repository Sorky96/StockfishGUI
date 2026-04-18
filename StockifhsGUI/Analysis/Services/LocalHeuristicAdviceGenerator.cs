namespace StockifhsGUI;

public sealed class LocalHeuristicAdviceGenerator : IAdviceGenerator
{
    private readonly TemplateAdviceGenerator templateAdviceGenerator;
    private readonly AdviceGenerationSettings settings;

    public LocalHeuristicAdviceGenerator(AdviceGenerationSettings? settings = null)
    {
        this.settings = settings ?? AdviceGenerationSettings.Default;
        templateAdviceGenerator = new TemplateAdviceGenerator(this.settings);
    }

    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate,
        AdviceGenerationContext? context = null)
    {
        MoveExplanation baseline = templateAdviceGenerator.Generate(replay, quality, tag, bestMoveUci, centipawnLoss, level, context);

        string phaseHint = replay.Phase switch
        {
            GamePhase.Opening => "Treat this as an opening pattern to review in your first 10 moves.",
            GamePhase.Middlegame => "This is a middlegame decision, so compare plans and forcing replies before committing.",
            GamePhase.Endgame => "Because this happened in the endgame, activity and technique matter more than cosmetic moves.",
            _ => string.Empty
        };

        string confidenceHint = tag?.Confidence switch
        {
            >= 0.9 => "The pattern is very consistent with the position.",
            >= 0.7 => "The pattern is fairly clear from the local heuristics.",
            > 0 => "The pattern is plausible, but still worth double-checking against the full position.",
            _ => "The move is weak even if the exact motif is less certain."
        };
        string evidenceHint = BuildEvidenceHint(context?.PromptContext);
        string heuristicHint = BuildHeuristicHint(context?.PromptContext);
        string openingHint = replay.Phase == GamePhase.Opening && !string.IsNullOrWhiteSpace(context?.PromptContext?.OpeningName)
            ? $"This came from {context.PromptContext.OpeningName}, so it is a reusable opening pattern."
            : string.Empty;

        string shortText = MergeSentences(MergeSentences(baseline.ShortText, confidenceHint, settings.MaxShortTextLength), evidenceHint, settings.MaxShortTextLength);
        string detailedText = MergeSentences(
            MergeSentences(
                MergeSentences(baseline.DetailedText, heuristicHint, settings.MaxDetailedTextLength),
                openingHint,
                settings.MaxDetailedTextLength),
            phaseHint,
            settings.MaxDetailedTextLength);
        string trainingHint = MergeSentences(baseline.TrainingHint, BuildFocusHint(replay, quality), settings.MaxTrainingHintLength);

        return new MoveExplanation(shortText, trainingHint, detailedText);
    }

    private static string BuildEvidenceHint(AdvicePromptContext? context)
    {
        if (context?.Evidence is null || context.Evidence.Count == 0)
        {
            return string.Empty;
        }

        string summary = string.Join("; ", context.Evidence.Take(2));
        return $"Local evidence points to this because {summary}.";
    }

    private static string BuildHeuristicHint(AdvicePromptContext? context)
    {
        if (context?.HeuristicNotes is null || context.HeuristicNotes.Count == 0)
        {
            return string.Empty;
        }

        string summary = string.Join("; ", context.HeuristicNotes.Take(2));
        return $"Concrete positional cues: {summary}.";
    }

    private static string BuildFocusHint(ReplayPly replay, MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => $"On moves like {replay.San}, slow down and spend one extra verification pass before you release the move.",
            MoveQualityBucket.Mistake => $"Use similar {replay.Phase.ToString().ToLowerInvariant()} positions as a targeted review set.",
            MoveQualityBucket.Inaccuracy => "Small losses add up, so compare two candidate moves before choosing the most natural one.",
            _ => string.Empty
        };
    }

    private static string MergeSentences(string primary, string secondary, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(secondary))
        {
            return primary;
        }

        string merged = string.IsNullOrWhiteSpace(primary)
            ? secondary.Trim()
            : $"{primary.Trim()} {secondary.Trim()}";

        if (merged.Length <= maxLength)
        {
            return merged;
        }

        if (!string.IsNullOrWhiteSpace(primary) && primary.Length <= maxLength)
        {
            return primary;
        }

        int candidateLength = Math.Max(1, maxLength - 3);
        return $"{merged[..candidateLength].Trim()}...";
    }
}
