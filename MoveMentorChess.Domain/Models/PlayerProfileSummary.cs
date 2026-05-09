namespace MoveMentorChess.Domain;

public sealed record PlayerProfileSummary(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss,
    IReadOnlyList<string> TopLabels);
