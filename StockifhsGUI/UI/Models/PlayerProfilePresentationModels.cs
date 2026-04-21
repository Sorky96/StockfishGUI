namespace StockifhsGUI;

internal sealed record PlayerProfileSummaryItem(string Label, string Value);

internal sealed record PlayerProfileStatItem(string Title, string Detail);

internal sealed record PlayerProfileWorkItem(string Title, string Description, string? Context);

internal sealed record PlayerProfileTrendViewModel(string Headline, string Summary, string Comparison);

internal sealed record PlayerProfilePresentationViewModel(
    string SnapshotCaption,
    IReadOnlyList<PlayerProfileSummaryItem> SummaryItems,
    IReadOnlyList<string> FixFirstItems,
    IReadOnlyList<PlayerProfileStatItem> KeyMistakes,
    IReadOnlyList<PlayerProfileStatItem> CostliestMistakes,
    IReadOnlyList<PlayerProfileWorkItem> WorkOnItems,
    PlayerProfileTrendViewModel RecentTrend);
