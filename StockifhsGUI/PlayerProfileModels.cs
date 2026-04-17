namespace StockifhsGUI;

public sealed record PlayerProfileSummary(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss,
    IReadOnlyList<string> TopLabels);

public sealed record ProfileLabelStat(
    string Label,
    int Count,
    double AverageConfidence);

public sealed record ProfilePhaseStat(
    GamePhase Phase,
    int Count);

public sealed record ProfileOpeningStat(
    string Eco,
    int Count);

public sealed record ProfileSideStat(
    PlayerSide Side,
    int GamesAnalyzed,
    int HighlightedMistakes);

public sealed record ProfileMonthlyTrend(
    string MonthKey,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss);

public sealed record TrainingRecommendation(
    string Title,
    string Description);

public sealed record PlayerProfileReport(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int TotalAnalyzedMoves,
    int HighlightedMistakes,
    int? AverageCentipawnLoss,
    IReadOnlyList<ProfileLabelStat> TopMistakeLabels,
    IReadOnlyList<ProfilePhaseStat> MistakesByPhase,
    IReadOnlyList<ProfileOpeningStat> MistakesByOpening,
    IReadOnlyList<ProfileSideStat> GamesBySide,
    IReadOnlyList<ProfileMonthlyTrend> MonthlyTrend,
    IReadOnlyList<TrainingRecommendation> Recommendations);
