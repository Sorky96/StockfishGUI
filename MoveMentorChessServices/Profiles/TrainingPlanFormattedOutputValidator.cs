using System.Text.RegularExpressions;

namespace MoveMentorChessServices;

public static partial class TrainingPlanFormattedOutputValidator
{
    private static readonly string[] DebugPhrases =
    [
        "frequency:",
        "cpl cost:",
        "priority score",
        "json",
        "debug",
        "model",
        "prompt",
        "deterministic"
    ];

    public static bool IsValid(TrainingPlanFormattedOutput output, TrainingPlanReport report)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        string combined = string.Join(
            " ",
            output.ShortWeeklyPlan,
            output.DetailedWeeklyPlan,
            output.PriorityRationale,
            output.ToneAdaptedVersion);

        if (string.IsNullOrWhiteSpace(output.ShortWeeklyPlan)
            || string.IsNullOrWhiteSpace(output.DetailedWeeklyPlan)
            || string.IsNullOrWhiteSpace(output.PriorityRationale)
            || string.IsNullOrWhiteSpace(output.ToneAdaptedVersion))
        {
            return false;
        }

        if (DebugPhrases.Any(phrase => combined.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return UsesOnlyKnownEcoCodes(combined, report)
            && UsesOnlyKnownTrainingNumbers(combined, report);
    }

    private static bool UsesOnlyKnownEcoCodes(string text, TrainingPlanReport report)
    {
        HashSet<string> allowed = report.Topics
            .SelectMany(topic => topic.RelatedOpenings)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in EcoCodeRegex().Matches(text))
        {
            if (!allowed.Contains(match.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsesOnlyKnownTrainingNumbers(string text, TrainingPlanReport report)
    {
        HashSet<int> allowed =
        [
            report.WeeklyPlan.Budget.TotalMinutes,
            report.WeeklyPlan.Budget.CoreWeaknessMinutes,
            report.WeeklyPlan.Budget.SecondaryWeaknessMinutes,
            report.WeeklyPlan.Budget.MaintenanceMinutes,
            report.WeeklyPlan.Budget.IntegrationMinutes
        ];

        foreach (TrainingPlanTopic topic in report.Topics)
        {
            allowed.Add(topic.Priority);
            allowed.Add(topic.PriorityBreakdown.FrequencyScore);
            allowed.Add(topic.PriorityBreakdown.CostScore);
            allowed.Add(topic.PriorityBreakdown.TrendScore);
            allowed.Add(topic.PriorityBreakdown.PhaseScore);
            allowed.Add(topic.PriorityBreakdown.TotalScore);

            foreach (TrainingBlock block in topic.Blocks)
            {
                allowed.Add(block.EstimatedMinutes);
            }
        }

        foreach (WeeklyTrainingDay day in report.WeeklyPlan.Days)
        {
            allowed.Add(day.DayNumber);
            allowed.Add(day.EstimatedMinutes);
        }

        foreach (Match match in NumberRegex().Matches(text))
        {
            if (int.TryParse(match.Value, out int number) && !allowed.Contains(number))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\b[A-E][0-9]{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EcoCodeRegex();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex NumberRegex();
}
