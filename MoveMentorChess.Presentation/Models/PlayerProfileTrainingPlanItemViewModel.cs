namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfileTrainingPlanItemViewModel(
    int TopicPriority,
    string TopicPriorityLabel,
    string Topic,
    string BlockType,
    string Category,
    int EstimatedMinutes,
    string ShortGoal,
    string WhyThisTopicNow,
    string? Context);
