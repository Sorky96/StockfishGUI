namespace MoveMentorChess.Domain;

public sealed record WeeklyTrainingDay(
    int DayNumber,
    string Topic,
    string WorkType,
    string Goal,
    int EstimatedMinutes,
    TrainingPlanTopicCategory Category,
    TrainingBlockPurpose Purpose = TrainingBlockPurpose.Maintain,
    TrainingBlockKind BlockKind = TrainingBlockKind.GameReview,
    IReadOnlyList<string>? RelatedOpenings = null,
    OpeningTrainingMode? LaunchTrainingMode = null);
