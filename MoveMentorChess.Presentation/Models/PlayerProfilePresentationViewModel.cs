namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfilePresentationViewModel(
    string SnapshotCaption,
    IReadOnlyList<PlayerProfileSummaryItem> SummaryItems,
    IReadOnlyList<string> FixFirstItems,
    IReadOnlyList<PlayerProfileStatItem> KeyMistakes,
    IReadOnlyList<PlayerProfileStatItem> CostliestMistakes,
    IReadOnlyList<PlayerProfileWorkItem> WorkOnItems,
    PlayerProfileTrendViewModel RecentTrend,
    PlayerProfileTrainingPlanViewModel TrainingPlan);
