namespace StockifhsGUI;

internal sealed record PlayerProfileSummaryItem(string Label, string Value);

internal sealed record PlayerProfileStatItem(string Title, string Detail);

internal sealed record PlayerProfileWorkItem(string Title, string Description, string? Context);

internal sealed record PlayerProfileTrendViewModel(string Headline, string Summary, string Comparison);

internal sealed record PlayerProfileTrainingBlockViewModel(
    string PurposeLabel,
    string KindLabel,
    string Title,
    string Description,
    int EstimatedMinutes);

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

internal sealed record PlayerProfileTrainingTopicViewModel(
    string RoleLabel,
    string Title,
    string FocusArea,
    string Summary,
    string WhyThisTopicNow,
    string Rationale,
    string? Context,
    IReadOnlyList<PlayerProfileTrainingBlockViewModel> Blocks);

internal sealed record PlayerProfileTrainingDayViewModel(
    int DayNumber,
    string Topic,
    string WorkType,
    string Goal,
    int EstimatedMinutes,
    string RoleLabel);

internal sealed record PlayerProfileTrainingPlanViewModel(
    string Headline,
    string Summary,
    string BudgetSummary,
    IReadOnlyList<PlayerProfileTrainingTopicViewModel> Topics,
    IReadOnlyList<PlayerProfileTrainingPlanItemViewModel> Items,
    IReadOnlyList<PlayerProfileTrainingDayViewModel> Days);

internal sealed record PlayerProfilePresentationViewModel(
    string SnapshotCaption,
    IReadOnlyList<PlayerProfileSummaryItem> SummaryItems,
    IReadOnlyList<string> FixFirstItems,
    IReadOnlyList<PlayerProfileStatItem> KeyMistakes,
    IReadOnlyList<PlayerProfileStatItem> CostliestMistakes,
    IReadOnlyList<PlayerProfileWorkItem> WorkOnItems,
    PlayerProfileTrendViewModel RecentTrend,
    PlayerProfileTrainingPlanViewModel TrainingPlan);
