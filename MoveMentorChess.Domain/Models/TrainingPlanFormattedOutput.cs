namespace MoveMentorChess.Domain;

public sealed record TrainingPlanFormattedOutput(
    string ShortWeeklyPlan,
    string DetailedWeeklyPlan,
    string PriorityRationale,
    string ToneAdaptedVersion);
