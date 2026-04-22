namespace StockifhsGUI;

public sealed record OpeningWeaknessReport(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int OpeningGamesAnalyzed,
    int? AverageOpeningCentipawnLoss,
    IReadOnlyList<OpeningWeaknessEntry> WeakOpenings,
    IReadOnlyList<OpeningMistakeSequenceStat> RecurringMistakeSequences);

public sealed record OpeningWeaknessEntry(
    string Eco,
    string OpeningName,
    string OpeningDisplayName,
    int Count,
    int? AverageOpeningCentipawnLoss,
    string? FirstRecurringMistakeType,
    int FirstRecurringMistakeCount,
    IReadOnlyList<OpeningMistakeSequenceStat> RecurringMistakeSequences,
    IReadOnlyList<OpeningExampleGame> ExampleGames,
    IReadOnlyList<OpeningMoveRecommendation> ExampleBetterMoves);

public sealed record OpeningMistakeSequenceStat(
    string Key,
    IReadOnlyList<string> Labels,
    int Count,
    int? AverageFirstPly,
    string? RepresentativeEco);

public sealed record OpeningExampleGame(
    string GameFingerprint,
    PlayerSide Side,
    string OpponentName,
    string? DateText,
    string? Result,
    string Eco,
    string OpeningDisplayName,
    int? FirstMistakePly,
    string? FirstMistakeSan,
    string? FirstMistakeType,
    int? FirstMistakeCentipawnLoss);

public sealed record OpeningMoveRecommendation(
    string GameFingerprint,
    PlayerSide Side,
    string Eco,
    int Ply,
    int MoveNumber,
    string PlayedSan,
    string BetterMove,
    string? MistakeType,
    int? CentipawnLoss,
    string FenBefore);
