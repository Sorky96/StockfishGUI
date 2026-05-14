namespace MoveMentorChess.Domain;

public sealed record TrainingResultLearningPlan(
    string MasteredText,
    string RepeatText,
    string NextReviewText,
    string ReasonText,
    IReadOnlyList<TrainingResultReviewItem> ReviewItems);

public sealed record TrainingResultReviewItem(
    string PositionId,
    string MoveText,
    string ReasonText,
    int Priority,
    string AttemptedMoveText = "",
    string PreparedMoveText = "",
    string PriorityText = "");
