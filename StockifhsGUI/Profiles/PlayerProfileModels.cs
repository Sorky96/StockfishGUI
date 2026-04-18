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

public sealed record ProfileQuarterlyTrend(
    string QuarterKey,
    int GamesAnalyzed,
    int HighlightedMistakes,
    int? AverageCentipawnLoss);

public sealed record TrainingRecommendation(
    int Priority,
    string FocusArea,
    string Title,
    string Description,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> SuggestedDrills);

public sealed record WeeklyTrainingDay(
    int DayNumber,
    string Theme,
    string PrimaryFocus,
    int EstimatedMinutes,
    IReadOnlyList<string> Activities,
    string SuccessCheck);

public sealed record WeeklyTrainingPlan(
    string Title,
    string Summary,
    IReadOnlyList<WeeklyTrainingDay> Days);

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
    IReadOnlyList<ProfileQuarterlyTrend> QuarterlyTrend,
    IReadOnlyList<TrainingRecommendation> Recommendations,
    WeeklyTrainingPlan WeeklyPlan);
