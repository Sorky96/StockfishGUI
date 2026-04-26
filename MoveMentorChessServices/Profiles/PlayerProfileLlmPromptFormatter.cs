using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoveMentorChessServices;

public static class PlayerProfileLlmPromptFormatter
{
    public static readonly IReadOnlyList<string> OutputKeys =
    [
        "profile_summary",
        "strengths_and_weaknesses",
        "what_to_focus_next",
        "tone_adapted_version",
        "deep_dive"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static PlayerProfileLlmPromptFormatter()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static PlayerProfileLlmInput BuildInput(
        PlayerProfileReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new PlayerProfileLlmInput(
            report.DisplayName,
            audienceLevel,
            BuildAudienceDescription(audienceLevel),
            trainerStyle,
            BuildTrainerDescription(trainerStyle),
            report.GamesAnalyzed,
            report.TotalAnalyzedMoves,
            report.HighlightedMistakes,
            report.AverageCentipawnLoss,
            report.ProgressSignal.Direction.ToString(),
            report.TopMistakeLabels
                .Take(5)
                .Select(item => $"{PlayerProfileTextFormatter.FormatMistakeLabel(item.Label)}: {item.Count}")
                .ToList(),
            report.CostliestMistakeLabels
                .Take(5)
                .Select(item => $"{PlayerProfileTextFormatter.FormatMistakeLabel(item.Label)}: total CPL {item.TotalCentipawnLoss}, avg CPL {item.AverageCentipawnLoss?.ToString() ?? "n/a"}")
                .ToList(),
            report.MistakesByPhase
                .Take(3)
                .Select(item => $"{PlayerProfileTextFormatter.FormatPhase(item.Phase)}: {item.Count}")
                .ToList(),
            report.MistakesByOpening
                .Take(4)
                .Select(item => $"{PlayerProfileTextFormatter.FormatOpening(item.Eco)} ({item.Eco}): {item.Count}")
                .ToList(),
            report.TrainingPlan.Topics
                .OrderBy(item => item.Priority)
                .Take(4)
                .Select(item => $"{item.Title}: {item.Summary}")
                .ToList(),
            BuildNextActions(report));
    }

    public static string BuildPrompt(
        PlayerProfileReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        PlayerProfileLlmInput input = BuildInput(report, audienceLevel, trainerStyle);
        StringBuilder builder = new();

        builder.AppendLine(BuildSystemBlock(audienceLevel));
        builder.AppendLine(BuildTrainerStyleBlock(trainerStyle));
        builder.AppendLine();
        builder.AppendLine("Task: Format this completed chess player profile for the player. Do not diagnose new problems.");
        builder.AppendLine("Use only facts present in the JSON input. If a fact is not present, omit it.");
        builder.AppendLine("No debug style, no score formulas, no 'Frequency:'/'CPL cost:' labels, and no internal implementation language.");
        builder.AppendLine("Respect audience_description and trainer_description from the JSON input when choosing vocabulary and tone.");
        builder.AppendLine("The trainer style must be visible in every output field, not only in tone_adapted_version.");
        builder.AppendLine("If trainer_style is WittyAlien, use playful alien-coach wording in every field while keeping the chess advice accurate.");
        builder.AppendLine("UI layout contract: profile_summary must be short and come first; deep_dive is optional supporting detail for lower UI sections.");
        builder.AppendLine();
        builder.AppendLine("Input JSON:");
        builder.AppendLine(JsonSerializer.Serialize(input, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Reply with ONLY one JSON object. No markdown and no text outside JSON.");
        builder.AppendLine("Keys: profile_summary, strengths_and_weaknesses, what_to_focus_next, tone_adapted_version, deep_dive.");
        builder.AppendLine("All values must be strings. Use short sentences. deep_dive may be an empty string.");
        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> BuildNextActions(PlayerProfileReport report)
    {
        List<string> actions = [];

        foreach (TrainingRecommendation recommendation in report.Recommendations.Take(2))
        {
            foreach (string item in recommendation.Checklist.Take(2))
            {
                string action = PlayerProfileTextFormatter.TrimSentence(item);
                if (!string.IsNullOrWhiteSpace(action)
                    && !actions.Contains(action, StringComparer.OrdinalIgnoreCase))
                {
                    actions.Add(action);
                }
            }
        }

        if (actions.Count == 0 && report.TrainingPlan.WeeklyPlan.Days.Count > 0)
        {
            actions.AddRange(report.TrainingPlan.WeeklyPlan.Days
                .Take(3)
                .Select(day => $"{day.Topic}: {day.Goal}"));
        }

        return actions.Take(4).ToList();
    }

    private static string BuildSystemBlock(PlayerProfileAudienceLevel level)
    {
        return level switch
        {
            PlayerProfileAudienceLevel.Beginner =>
                """
                You are a friendly chess coach writing for a beginner.
                Use plain language and explain chess terms only when needed.
                Keep the text encouraging, practical, and easy to act on.
                """,
            PlayerProfileAudienceLevel.Advanced =>
                """
                You are a precise chess coach writing for an advanced player.
                You may use chess terminology when it is supported by the input.
                Keep the text compact, concrete, and training-oriented.
                """,
            _ =>
                """
                You are a practical chess coach writing for an intermediate player.
                Use normal chess language without sounding technical for its own sake.
                Keep the text clear, direct, and useful before the next training session.
                """
        };
    }

    public static string BuildAudienceDescription(PlayerProfileAudienceLevel level)
    {
        return level switch
        {
            PlayerProfileAudienceLevel.Beginner => "Beginner: simple language, one clear habit, minimal chess jargon.",
            PlayerProfileAudienceLevel.Advanced => "Advanced: compact chess terminology, concrete training priorities, less hand-holding.",
            _ => "Intermediate: practical chess language, concrete patterns, clear next training step."
        };
    }

    public static string BuildTrainerDescription(AdviceNarrationStyle style)
    {
        return style switch
        {
            AdviceNarrationStyle.LevyRozman => "Energetic online chess educator: lively, direct, practical, with light humor.",
            AdviceNarrationStyle.HikaruNakamura => "Fast calculation-focused grandmaster: candidate moves, concrete checks, no fluff.",
            AdviceNarrationStyle.BotezLive => "Upbeat streaming chess coach: encouraging, conversational, slightly playful.",
            AdviceNarrationStyle.WittyAlien => "Witty alien chess coach: playful, cosmic, slightly strange, but still clear and useful.",
            _ => "Regular trainer: calm, practical, supportive chess coach."
        };
    }

    private static string BuildTrainerStyleBlock(AdviceNarrationStyle style)
    {
        return $"Trainer style: {BuildTrainerDescription(style)}";
    }
}
