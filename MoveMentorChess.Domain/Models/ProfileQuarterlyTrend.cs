namespace MoveMentorChess.Domain;

public sealed record ProfileQuarterlyTrend(
    string QuarterKey,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss);
