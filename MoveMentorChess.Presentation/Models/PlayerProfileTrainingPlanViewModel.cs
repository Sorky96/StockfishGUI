namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfileTrainingPlanViewModel(
    string Headline,
    string Summary,
    string BudgetSummary,
    IReadOnlyList<PlayerProfileTrainingTopicViewModel> Topics,
    IReadOnlyList<PlayerProfileTrainingPlanItemViewModel> Items,
    IReadOnlyList<PlayerProfileTrainingDayViewModel> Days,
    IReadOnlyList<PlayerProfileStatItem> WhyThisPlan);
