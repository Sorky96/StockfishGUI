namespace MoveMentorChess.Domain;

public sealed record ProfileProgressPeriod(
    string Label,
    int GamesAnalyzed,
    int? AverageCentipawnLoss,
    double HighlightedMistakesPerGame);
