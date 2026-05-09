namespace MoveMentorChess.Domain;

public sealed record OpeningWeaknessEntry(
    string Eco,
    string OpeningName,
    string OpeningDisplayName,
    int Count,
    int? AverageOpeningCentipawnLoss,
    string? FirstRecurringMistakeType,
    int FirstRecurringMistakeCount,
    OpeningWeaknessCategory Category,
    ProfileProgressDirection TrendDirection,
    string CategoryReason,
    IReadOnlyList<OpeningMistakeSequenceStat> RecurringMistakeSequences,
    IReadOnlyList<OpeningExampleGame> ExampleGames,
    IReadOnlyList<OpeningMoveRecommendation> ExampleBetterMoves);
