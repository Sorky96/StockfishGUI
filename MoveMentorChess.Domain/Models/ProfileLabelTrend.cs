namespace MoveMentorChess.Domain;

public sealed record ProfileLabelTrend(
    string Label,
    ProfileProgressDirection Direction,
    int RecentCount,
    int PreviousCount,
    int? RecentAverageCentipawnLoss,
    int? PreviousAverageCentipawnLoss);
