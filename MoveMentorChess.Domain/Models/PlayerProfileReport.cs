namespace MoveMentorChess.Domain;

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
    PlayerRatingTrendReport RatingTrend,
    IReadOnlyList<PlayerRatingTrendReport> RatingTrendsByTimeControl,
    ProfileProgressSignal ProgressSignal,
    IReadOnlyList<ProfileLabelTrend> LabelTrends,
    IReadOnlyList<TrainingRecommendation> Recommendations,
    WeeklyTrainingPlan WeeklyPlan,
    IReadOnlyList<ProfileMistakeExample> MistakeExamples,
    TrainingPlanReport TrainingPlan)
{
    public PlayerProfileReport(
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
        TrainingPlanReport TrainingPlan)
        : this(
            PlayerKey,
            DisplayName,
            GamesAnalyzed,
            TotalAnalyzedMoves,
            HighlightedMistakes,
            AverageCentipawnLoss,
            TopMistakeLabels,
            CostliestMistakeLabels,
            MistakesByPhase,
            MistakesByOpening,
            GamesBySide,
            MonthlyTrend,
            QuarterlyTrend,
            EmptyRatingTrend(),
            [],
            ProgressSignal,
            LabelTrends,
            Recommendations,
            WeeklyPlan,
            MistakeExamples,
            TrainingPlan)
    {
    }

    private static PlayerRatingTrendReport EmptyRatingTrend()
    {
        return new PlayerRatingTrendReport(
            null,
            0,
            null,
            null,
            [],
            [],
            [],
            [],
            "No MoveMentor estimated strength data yet.");
    }
}
