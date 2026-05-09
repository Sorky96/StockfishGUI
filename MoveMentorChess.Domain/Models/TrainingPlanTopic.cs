namespace MoveMentorChess.Domain;

public sealed record TrainingPlanTopic(
    int Priority,
    TrainingPlanTopicCategory Category,
    string Label,
    string FocusArea,
    string Title,
    string Summary,
    string WhyThisTopicNow,
    string Rationale,
    ProfileProgressDirection TrendDirection,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> SuggestedDrills,
    IReadOnlyList<TrainingBlock> Blocks,
    IReadOnlyList<ProfileMistakeExample> Examples,
    TrainingPlanPriorityBreakdown PriorityBreakdown,
    TrainingPlanTopicStatus Status = TrainingPlanTopicStatus.Stable);
