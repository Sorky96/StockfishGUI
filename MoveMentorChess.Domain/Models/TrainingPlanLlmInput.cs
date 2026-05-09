namespace MoveMentorChess.Domain;

public sealed record TrainingPlanLlmInput(
    string Player,
    PlayerProfileAudienceLevel AudienceLevel,
    string AudienceDescription,
    AdviceNarrationStyle TrainerStyle,
    string TrainerDescription,
    string TimeBudgetDescription,
    string RecentTrend,
    string PlanSummary,
    IReadOnlyList<string> PriorityTopics,
    IReadOnlyList<string> PriorityReasons,
    IReadOnlyList<string> WeeklySchedule,
    IReadOnlyList<string> TrainingBlocks,
    IReadOnlyList<string> OpeningTargets);
