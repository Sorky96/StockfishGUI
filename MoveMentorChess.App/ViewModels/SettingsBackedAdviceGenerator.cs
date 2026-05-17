using MoveMentorChess.Analysis;

namespace MoveMentorChess.App.ViewModels;

internal sealed class SettingsBackedAdviceGenerator(IAdviceGenerator inner) : IAdviceGenerator
{
    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate,
        AdviceGenerationContext? context = null)
    {
        LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
        AdviceGenerationContext enrichedContext = context is null
            ? new AdviceGenerationContext("avalonia-analysis-window", null, NarrationStyle: settings.NarrationStyle)
            : context with { NarrationStyle = settings.NarrationStyle };

        MoveExplanation explanation = inner.Generate(
            replay,
            quality,
            tag,
            bestMoveUci,
            centipawnLoss,
            settings.DefaultExplanationLevel,
            enrichedContext);

        return ApplyNarrationStyle(explanation, settings.NarrationStyle);
    }

    private static MoveExplanation ApplyNarrationStyle(MoveExplanation explanation, AdviceNarrationStyle style)
    {
        return style switch
        {
            AdviceNarrationStyle.LevyRozman => explanation with
            {
                ShortText = AddPrefix(explanation.ShortText, "Here is the practical idea: "),
                DetailedText = AddPrefix(explanation.DetailedText, "Coach recap: "),
                TrainingHint = AddPrefix(explanation.TrainingHint, "Levy-style drill: ")
            },
            AdviceNarrationStyle.HikaruNakamura => explanation with
            {
                ShortText = AddPrefix(explanation.ShortText, "Candidate-move check: "),
                DetailedText = AddPrefix(explanation.DetailedText, "Calculation note: "),
                TrainingHint = AddPrefix(explanation.TrainingHint, "Speed drill: ")
            },
            AdviceNarrationStyle.BotezLive => explanation with
            {
                ShortText = AddPrefix(explanation.ShortText, "Okay, tiny chess crisis, very fixable: "),
                DetailedText = AddPrefix(explanation.DetailedText, "Stream recap: "),
                TrainingHint = AddPrefix(explanation.TrainingHint, "Next-game challenge: ")
            },
            AdviceNarrationStyle.WittyAlien => explanation with
            {
                ShortText = AddPrefix(explanation.ShortText, "Alien coach says the pony wandered into danger: "),
                DetailedText = AddPrefix(explanation.DetailedText, "Free-candy scanner report: "),
                TrainingHint = AddPrefix(explanation.TrainingHint, "Do not grab free candy rule: ")
            },
            _ => explanation
        };
    }

    private static string AddPrefix(string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return $"{prefix}{text}";
    }
}
