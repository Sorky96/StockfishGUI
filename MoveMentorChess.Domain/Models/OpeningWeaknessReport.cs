namespace MoveMentorChess.Domain;

public sealed record OpeningWeaknessReport(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int OpeningGamesAnalyzed,
    int? AverageOpeningCentipawnLoss,
    IReadOnlyList<OpeningWeaknessEntry> WeakOpenings,
    IReadOnlyList<OpeningMistakeSequenceStat> RecurringMistakeSequences);
