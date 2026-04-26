namespace MoveMentorChessServices;

public sealed class HeuristicPlayerProfileFormatter : IPlayerProfileFormatter
{
    public PlayerProfileFormattedOutput Format(
        PlayerProfileReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        string mainIssue = report.TopMistakeLabels.Count > 0
            ? PlayerProfileTextFormatter.FormatMistakeLabel(report.TopMistakeLabels[0].Label)
            : "no single recurring mistake yet";
        string trend = PlayerProfileTextFormatter.FormatTrendHeadline(report.ProgressSignal.Direction).ToLowerInvariant();
        string phase = report.MistakesByPhase.Count > 0
            ? PlayerProfileTextFormatter.FormatPhase(report.MistakesByPhase[0].Phase).ToLowerInvariant()
            : "mixed phases";
        string opening = report.MistakesByOpening.Count > 0
            ? PlayerProfileTextFormatter.FormatOpening(report.MistakesByOpening[0].Eco)
            : "mixed openings";
        TrainingPlanTopic? topic = report.TrainingPlan.Topics
            .OrderBy(item => item.Priority)
            .FirstOrDefault();

        string summary = $"Summary: {report.DisplayName} most needs to clean up {mainIssue.ToLowerInvariant()}; recent form is {trend}.";
        string strengthsAndWeaknesses = BuildStrengthsAndWeaknesses(report, mainIssue, phase, opening);
        string focus = topic is null
            ? "What to focus next: review the biggest evaluation swings from recent games and write down the missed check before each move."
            : $"What to focus next: start with {topic.Title.ToLowerInvariant()} and use the next training block to practice {topic.FocusArea.ToLowerInvariant()}.";
        string toneAdapted = BuildToneAdaptedVersion(audienceLevel, trainerStyle, mainIssue, topic);
        string deepDive = BuildDeepDive(report, phase, opening);

        return new PlayerProfileFormattedOutput(
            ApplyTrainerStyleToField(summary, trainerStyle, ProfileFormatterField.Summary),
            ApplyTrainerStyleToField(strengthsAndWeaknesses, trainerStyle, ProfileFormatterField.StrengthsAndWeaknesses),
            ApplyTrainerStyleToField(focus, trainerStyle, ProfileFormatterField.WhatToFocusNext),
            toneAdapted,
            ApplyTrainerStyleToField(deepDive, trainerStyle, ProfileFormatterField.DeepDive));
    }

    private static string BuildStrengthsAndWeaknesses(PlayerProfileReport report, string mainIssue, string phase, string opening)
    {
        if (report.TopMistakeLabels.Count == 0)
        {
            return "Strengths and weaknesses: there is not enough mistake data for a stable pattern yet.";
        }

        string second = report.TopMistakeLabels.Count > 1
            ? $" A secondary theme is {PlayerProfileTextFormatter.FormatMistakeLabel(report.TopMistakeLabels[1].Label).ToLowerInvariant()}."
            : string.Empty;

        return $"Strengths and weaknesses: the clearest weakness is {mainIssue.ToLowerInvariant()}, especially around the {phase} and {opening}.{second}";
    }

    private static string BuildToneAdaptedVersion(
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle,
        string mainIssue,
        TrainingPlanTopic? topic)
    {
        string next = topic?.Title.ToLowerInvariant() ?? "reviewing critical moments";
        string levelText = audienceLevel switch
        {
            PlayerProfileAudienceLevel.Beginner =>
                $"focus on one simple habit first: before moving, check whether a piece is left loose. That directly targets {mainIssue.ToLowerInvariant()}.",
            PlayerProfileAudienceLevel.Advanced =>
                $"treat {mainIssue.ToLowerInvariant()} as the main leak, then drill {next} with concrete move checks from your own games.",
            _ =>
                $"your next useful step is {next}; keep it practical and connect each drill to {mainIssue.ToLowerInvariant()}."
        };

        return $"Tone adapted version: {ApplyTrainerStyle(levelText, trainerStyle)}";
    }

    private static string ApplyTrainerStyle(string text, AdviceNarrationStyle trainerStyle)
    {
        return trainerStyle switch
        {
            AdviceNarrationStyle.LevyRozman => $"Be direct and energetic: {text}",
            AdviceNarrationStyle.HikaruNakamura => $"Think in candidate checks and forcing moves: {text}",
            AdviceNarrationStyle.BotezLive => $"Keep it upbeat and practical: {text}",
            AdviceNarrationStyle.WittyAlien => $"Alien-coach mode: {text} Keep the spaceship steady and the pieces protected.",
            _ => text
        };
    }

    private static string ApplyTrainerStyleToField(
        string text,
        AdviceNarrationStyle trainerStyle,
        ProfileFormatterField field)
    {
        return trainerStyle switch
        {
            AdviceNarrationStyle.LevyRozman => field switch
            {
                ProfileFormatterField.Summary => text.Replace("Summary:", "Summary: Direct trainer read:", StringComparison.Ordinal),
                ProfileFormatterField.WhatToFocusNext => $"{text} Do this first, no drama.",
                _ => text
            },
            AdviceNarrationStyle.HikaruNakamura => field switch
            {
                ProfileFormatterField.Summary => text.Replace("Summary:", "Summary: Candidate-move scan:", StringComparison.Ordinal),
                ProfileFormatterField.WhatToFocusNext => $"{text} Check forcing moves before quiet choices.",
                _ => text
            },
            AdviceNarrationStyle.BotezLive => field switch
            {
                ProfileFormatterField.Summary => text.Replace("Summary:", "Summary: Coach check-in:", StringComparison.Ordinal),
                ProfileFormatterField.WhatToFocusNext => $"{text} Keep it light, but make the habit automatic.",
                _ => text
            },
            AdviceNarrationStyle.WittyAlien => field switch
            {
                ProfileFormatterField.Summary => text.Replace("Summary:", "Summary: Alien scan from orbit:", StringComparison.Ordinal),
                ProfileFormatterField.StrengthsAndWeaknesses => text.Replace("Strengths and weaknesses:", "Strengths and weaknesses: cosmic board report:", StringComparison.Ordinal),
                ProfileFormatterField.WhatToFocusNext => text.Replace("What to focus next:", "What to focus next: spaceship drill:", StringComparison.Ordinal),
                ProfileFormatterField.DeepDive => text.Replace("Deep dive:", "Deep dive: star-map details:", StringComparison.Ordinal),
                _ => text
            },
            _ => text
        };
    }

    private static string BuildDeepDive(PlayerProfileReport report, string phase, string opening)
    {
        string cpl = report.AverageCentipawnLoss?.ToString() ?? "n/a";
        return $"Deep dive: the profile is based on {report.GamesAnalyzed} games, {report.TotalAnalyzedMoves} analyzed moves, and {report.HighlightedMistakes} highlighted mistakes. Average CPL is {cpl}. The main cluster appears in the {phase} and around {opening}.";
    }

    private enum ProfileFormatterField
    {
        Summary,
        StrengthsAndWeaknesses,
        WhatToFocusNext,
        DeepDive
    }
}
