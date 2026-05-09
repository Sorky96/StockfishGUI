namespace MoveMentorChess.Domain;

public sealed record ProfileMonthlyTrend(
    string MonthKey,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss);
