using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoveMentorChessServices;

public static class TrainingPlanLlmPromptFormatter
{
    public static readonly IReadOnlyList<string> OutputKeys =
    [
        "short_weekly_plan",
        "detailed_weekly_plan",
        "priority_rationale",
        "tone_adapted_version"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static TrainingPlanLlmPromptFormatter()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static TrainingPlanLlmInput BuildInput(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new TrainingPlanLlmInput(
            report.DisplayName,
            audienceLevel,
            PlayerProfileLlmPromptFormatter.BuildAudienceDescription(audienceLevel),
            trainerStyle,
            PlayerProfileLlmPromptFormatter.BuildTrainerDescription(trainerStyle),
            BuildTimeBudgetDescription(report.WeeklyPlan.Budget),
            report.TrendDirection.ToString(),
            report.Summary,
            report.Topics
                .OrderBy(topic => topic.Priority)
                .Select(topic => $"Priority {topic.Priority}: {topic.Title} ({FormatCategory(topic.Category)}) - {topic.Summary}")
                .ToList(),
            report.Topics
                .OrderBy(topic => topic.Priority)
                .Select(topic => $"Priority {topic.Priority}: {topic.WhyThisTopicNow}")
                .ToList(),
            report.WeeklyPlan.Days
                .OrderBy(day => day.DayNumber)
                .Select(day => $"Day {day.DayNumber}: {day.Topic} | {day.WorkType} | {day.EstimatedMinutes} min | {day.Goal}")
                .ToList(),
            report.Topics
                .OrderBy(topic => topic.Priority)
                .SelectMany(topic => topic.Blocks.Select(block =>
                    $"{topic.Title}: {FormatPurpose(block.Purpose)} {FormatKind(block.Kind)}, {block.EstimatedMinutes} min - {block.Description}"))
                .ToList(),
            report.Topics
                .SelectMany(topic => topic.RelatedOpenings)
                .Where(opening => !string.IsNullOrWhiteSpace(opening))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(PlayerProfileTextFormatter.FormatOpening)
                .ToList());
    }

    public static string BuildPrompt(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        TrainingPlanLlmInput input = BuildInput(report, audienceLevel, trainerStyle);
        StringBuilder builder = new();

        builder.AppendLine(BuildSystemBlock(audienceLevel));
        builder.AppendLine($"Trainer style: {PlayerProfileLlmPromptFormatter.BuildTrainerDescription(trainerStyle)}");
        builder.AppendLine();
        builder.AppendLine("Task: Format this completed chess weekly training plan for the player. Do not change priorities, topics, days, minutes, or training logic.");
        builder.AppendLine("Use only facts present in the JSON input. If a fact is not present, omit it.");
        builder.AppendLine("Generate a short weekly version and a more detailed weekly version.");
        builder.AppendLine("Explain why the priorities matter in understandable player-facing language.");
        builder.AppendLine("Adapt the tone to audience_description and time_budget_description.");
        builder.AppendLine("No debug labels, no score formulas, no internal implementation language, no markdown.");
        builder.AppendLine("If trainer_style is WittyAlien, use playful alien-coach wording while keeping the training plan accurate.");
        builder.AppendLine();
        builder.AppendLine("Input JSON:");
        builder.AppendLine(JsonSerializer.Serialize(input, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Reply with ONLY one JSON object. No text outside JSON.");
        builder.AppendLine("Keys: short_weekly_plan, detailed_weekly_plan, priority_rationale, tone_adapted_version.");
        builder.AppendLine("All values must be strings. Keep short_weekly_plan compact; detailed_weekly_plan may contain day-by-day sentences.");
        return builder.ToString().Trim();
    }

    private static string BuildSystemBlock(PlayerProfileAudienceLevel level)
    {
        return level switch
        {
            PlayerProfileAudienceLevel.Beginner =>
                """
                You are a friendly chess coach turning a fixed training plan into simple weekly instructions.
                Use plain language, one habit at a time, and keep the plan encouraging.
                """,
            PlayerProfileAudienceLevel.Advanced =>
                """
                You are a precise chess coach turning a fixed training plan into compact weekly work.
                Use concrete chess vocabulary and keep every sentence training-oriented.
                """,
            _ =>
                """
                You are a practical chess coach turning a fixed training plan into usable weekly work.
                Use clear chess language and make the next session easy to start.
                """
        };
    }

    private static string BuildTimeBudgetDescription(WeeklyTrainingBudget budget)
    {
        if (budget.TotalMinutes <= 75)
        {
            return $"Light week: {budget.TotalMinutes} minutes total. Keep instructions short and easy to complete.";
        }

        if (budget.TotalMinutes >= 150)
        {
            return $"Deep week: {budget.TotalMinutes} minutes total. The player can handle a fuller explanation and more deliberate review.";
        }

        return $"Normal week: {budget.TotalMinutes} minutes total. Balance clarity with enough detail to guide each session.";
    }

    private static string FormatCategory(TrainingPlanTopicCategory category)
    {
        return category switch
        {
            TrainingPlanTopicCategory.CoreWeakness => "core weakness",
            TrainingPlanTopicCategory.SecondaryWeakness => "secondary weakness",
            TrainingPlanTopicCategory.MaintenanceTopic => "maintenance topic",
            _ => category.ToString()
        };
    }

    private static string FormatPurpose(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => "repair",
            TrainingBlockPurpose.Maintain => "maintain",
            TrainingBlockPurpose.Checklist => "checklist",
            _ => purpose.ToString().ToLowerInvariant()
        };
    }

    private static string FormatKind(TrainingBlockKind kind)
    {
        return kind switch
        {
            TrainingBlockKind.Tactics => "tactics",
            TrainingBlockKind.OpeningReview => "opening review",
            TrainingBlockKind.EndgameDrill => "endgame drill",
            TrainingBlockKind.GameReview => "game review",
            TrainingBlockKind.SlowPlayFocus => "slow play focus",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}
