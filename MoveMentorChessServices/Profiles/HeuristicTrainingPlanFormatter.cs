namespace MoveMentorChessServices;

public sealed class HeuristicTrainingPlanFormatter : ITrainingPlanFormatter
{
    public TrainingPlanFormattedOutput Format(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        TrainingPlanTopic? core = report.Topics
            .OrderBy(topic => topic.Priority)
            .FirstOrDefault();
        string coreTitle = core?.Title ?? "critical moment review";
        string timeText = BuildTimeText(report.WeeklyPlan.Budget.TotalMinutes);
        string shortPlan = BuildShortWeeklyPlan(report, coreTitle, timeText);
        string detailedPlan = BuildDetailedWeeklyPlan(report, audienceLevel);
        string rationale = BuildPriorityRationale(report, core);
        string toneAdapted = BuildToneAdaptedVersion(report, audienceLevel, trainerStyle, coreTitle, timeText);

        return new TrainingPlanFormattedOutput(
            ApplyTrainerStyle(shortPlan, trainerStyle, TrainingPlanFormatterField.ShortPlan),
            ApplyTrainerStyle(detailedPlan, trainerStyle, TrainingPlanFormatterField.DetailedPlan),
            ApplyTrainerStyle(rationale, trainerStyle, TrainingPlanFormatterField.Rationale),
            toneAdapted);
    }

    private static string BuildShortWeeklyPlan(TrainingPlanReport report, string coreTitle, string timeText)
    {
        string days = string.Join(
            "; ",
            report.WeeklyPlan.Days
                .OrderBy(day => day.DayNumber)
                .Take(3)
                .Select(day => $"Day {day.DayNumber}: {day.Topic} ({day.EstimatedMinutes} min)"));

        return $"Short weekly plan: use this {timeText} week to start with {coreTitle}. {days}.";
    }

    private static string BuildDetailedWeeklyPlan(TrainingPlanReport report, PlayerProfileAudienceLevel audienceLevel)
    {
        IEnumerable<WeeklyTrainingDay> days = report.WeeklyPlan.Days.OrderBy(day => day.DayNumber);
        string prefix = audienceLevel switch
        {
            PlayerProfileAudienceLevel.Beginner => "Detailed weekly plan: keep each session simple and finish the stated goal.",
            PlayerProfileAudienceLevel.Advanced => "Detailed weekly plan: treat each slot as a focused diagnostic block.",
            _ => "Detailed weekly plan: follow the day order and connect each drill to your own games."
        };
        string schedule = string.Join(
            " ",
            days.Select(day => $"Day {day.DayNumber}: {day.Topic}, {day.WorkType.ToLowerInvariant()}, {day.EstimatedMinutes} min - {TrimSentence(day.Goal)}."));

        return $"{prefix} {schedule}";
    }

    private static string BuildPriorityRationale(TrainingPlanReport report, TrainingPlanTopic? core)
    {
        if (core is null)
        {
            return "Priority rationale: there is not enough stable topic data yet, so the plan uses general review until stronger patterns appear.";
        }

        IEnumerable<string> reasons = report.Topics
            .OrderBy(topic => topic.Priority)
            .Take(3)
            .Select(topic => $"{topic.Title}: {TrimSentence(topic.WhyThisTopicNow)}.");

        return $"Priority rationale: {string.Join(" ", reasons)}";
    }

    private static string BuildToneAdaptedVersion(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle,
        string coreTitle,
        string timeText)
    {
        string baseText = audienceLevel switch
        {
            PlayerProfileAudienceLevel.Beginner =>
                $"Tone adapted version: make the week feel small and doable: one session at a time, mostly around {coreTitle}.",
            PlayerProfileAudienceLevel.Advanced =>
                $"Tone adapted version: use the {timeText} week as deliberate work on {coreTitle}, then test the checklist in practical games.",
            _ =>
                $"Tone adapted version: keep the {timeText} week practical; start with {coreTitle}, then use the later days to make the habit stick."
        };

        return trainerStyle switch
        {
            AdviceNarrationStyle.LevyRozman => $"{baseText} Direct coach note: do the first block before adding anything fancy.",
            AdviceNarrationStyle.HikaruNakamura => $"{baseText} Candidate-move note: check forcing moves before quiet choices.",
            AdviceNarrationStyle.BotezLive => $"{baseText} Keep it upbeat and actually finishable.",
            AdviceNarrationStyle.WittyAlien => $"{baseText} Alien-coach note: keep the spacecraft steady and the pieces defended.",
            _ => baseText
        };
    }

    private static string BuildTimeText(int totalMinutes)
    {
        if (totalMinutes <= 75)
        {
            return "light";
        }

        if (totalMinutes >= 150)
        {
            return "deep";
        }

        return "normal";
    }

    private static string ApplyTrainerStyle(
        string text,
        AdviceNarrationStyle trainerStyle,
        TrainingPlanFormatterField field)
    {
        return trainerStyle switch
        {
            AdviceNarrationStyle.LevyRozman when field == TrainingPlanFormatterField.ShortPlan =>
                text.Replace("Short weekly plan:", "Short weekly plan: Direct trainer read:", StringComparison.Ordinal),
            AdviceNarrationStyle.HikaruNakamura when field == TrainingPlanFormatterField.ShortPlan =>
                text.Replace("Short weekly plan:", "Short weekly plan: Candidate-move scan:", StringComparison.Ordinal),
            AdviceNarrationStyle.BotezLive when field == TrainingPlanFormatterField.ShortPlan =>
                text.Replace("Short weekly plan:", "Short weekly plan: Coach check-in:", StringComparison.Ordinal),
            AdviceNarrationStyle.WittyAlien => field switch
            {
                TrainingPlanFormatterField.ShortPlan => text.Replace("Short weekly plan:", "Short weekly plan: Alien training map:", StringComparison.Ordinal),
                TrainingPlanFormatterField.DetailedPlan => text.Replace("Detailed weekly plan:", "Detailed weekly plan: Orbit-by-orbit plan:", StringComparison.Ordinal),
                TrainingPlanFormatterField.Rationale => text.Replace("Priority rationale:", "Priority rationale: cosmic why-now report:", StringComparison.Ordinal),
                _ => text
            },
            _ => text
        };
    }

    private static string TrimSentence(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim().TrimEnd('.', ';', ':', '!');
    }

    private enum TrainingPlanFormatterField
    {
        ShortPlan,
        DetailedPlan,
        Rationale
    }
}
