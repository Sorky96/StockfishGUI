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

public sealed record ProfileCostlyLabelStat(
    string Label,
    int Count,
    int TotalCentipawnLoss,
    int? AverageCentipawnLoss);

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

public enum ProfileProgressDirection
{
    InsufficientData,
    Improving,
    Stable,
    Regressing
}

public sealed record ProfileProgressPeriod(
    string Label,
    int GamesAnalyzed,
    int? AverageCentipawnLoss,
    double HighlightedMistakesPerGame);

public sealed record ProfileProgressSignal(
    ProfileProgressDirection Direction,
    string Summary,
    ProfileProgressPeriod? Recent,
    ProfileProgressPeriod? Previous);

public sealed record ProfileLabelTrend(
    string Label,
    ProfileProgressDirection Direction,
    int RecentCount,
    int PreviousCount,
    int? RecentAverageCentipawnLoss,
    int? PreviousAverageCentipawnLoss);

public sealed record ProfileMistakeExample(
    string GameFingerprint,
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string PlayedSan,
    string BetterMove,
    string Label,
    int? CentipawnLoss,
    MoveQualityBucket Quality,
    GamePhase Phase,
    string Eco,
    string FenBefore,
    ProfileMistakeExampleRank Rank);

public enum ProfileMistakeExampleRank
{
    MostFrequent,
    MostCostly,
    MostRepresentative
}

public enum TrainingBlockKind
{
    Tactics,
    OpeningReview,
    EndgameDrill,
    GameReview,
    SlowPlayFocus
}

public enum TrainingBlockPurpose
{
    Repair,
    Maintain,
    Checklist
}

public sealed record TrainingBlock(
    TrainingBlockPurpose Purpose,
    TrainingBlockKind Kind,
    string Title,
    string Description,
    int EstimatedMinutes,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings);

public sealed record TrainingRecommendation(
    int Priority,
    string FocusArea,
    string Title,
    string Description,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> SuggestedDrills,
    IReadOnlyList<ProfileMistakeExample>? Examples = null,
    IReadOnlyList<TrainingBlock>? Blocks = null);

public sealed record WeeklyTrainingDay(
    int DayNumber,
    string Topic,
    string WorkType,
    string Goal,
    int EstimatedMinutes,
    TrainingPlanTopicCategory Category,
    TrainingBlockPurpose Purpose = TrainingBlockPurpose.Maintain,
    TrainingBlockKind BlockKind = TrainingBlockKind.GameReview);

public sealed record WeeklyTrainingBudget(
    int TotalMinutes,
    int CoreWeaknessMinutes,
    int SecondaryWeaknessMinutes,
    int MaintenanceMinutes,
    int IntegrationMinutes,
    string Summary);

public sealed record WeeklyTrainingPlan(
    string Title,
    string Summary,
    WeeklyTrainingBudget Budget,
    IReadOnlyList<WeeklyTrainingDay> Days);

public enum TrainingPlanTopicCategory
{
    CoreWeakness,
    SecondaryWeakness,
    MaintenanceTopic
}

public sealed record TrainingPlanPriorityBreakdown(
    int FrequencyScore,
    int CostScore,
    int TrendScore,
    int PhaseScore,
    int TotalScore);

public sealed record TrainingPlanTopic(
    int Priority,
    TrainingPlanTopicCategory Category,
    string Label,
    string FocusArea,
    string Title,
    string Summary,
    string WhyThisTopicNow,
    string Rationale,
    ProfileProgressDirection TrendDirection,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> SuggestedDrills,
    IReadOnlyList<TrainingBlock> Blocks,
    IReadOnlyList<ProfileMistakeExample> Examples,
    TrainingPlanPriorityBreakdown PriorityBreakdown);

public sealed record TrainingPlanReport(
    string PlayerKey,
    string DisplayName,
    ProfileProgressDirection TrendDirection,
    string Summary,
    IReadOnlyList<TrainingPlanTopic> Topics,
    IReadOnlyList<TrainingRecommendation> Recommendations,
    WeeklyTrainingPlan WeeklyPlan);

public sealed record PlayerProfileReport(
    string PlayerKey,
    string DisplayName,
    int GamesAnalyzed,
    int TotalAnalyzedMoves,
    int HighlightedMistakes,
    int? AverageCentipawnLoss,
    IReadOnlyList<ProfileLabelStat> TopMistakeLabels,
    IReadOnlyList<ProfileCostlyLabelStat> CostliestMistakeLabels,
    IReadOnlyList<ProfilePhaseStat> MistakesByPhase,
    IReadOnlyList<ProfileOpeningStat> MistakesByOpening,
    IReadOnlyList<ProfileSideStat> GamesBySide,
    IReadOnlyList<ProfileMonthlyTrend> MonthlyTrend,
    IReadOnlyList<ProfileQuarterlyTrend> QuarterlyTrend,
    ProfileProgressSignal ProgressSignal,
    IReadOnlyList<ProfileLabelTrend> LabelTrends,
    IReadOnlyList<TrainingRecommendation> Recommendations,
    WeeklyTrainingPlan WeeklyPlan,
    IReadOnlyList<ProfileMistakeExample> MistakeExamples,
    TrainingPlanReport TrainingPlan);
