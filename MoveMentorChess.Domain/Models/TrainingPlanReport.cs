namespace MoveMentorChess.Domain;

public sealed record TrainingPlanReport(
    string PlayerKey,
    string DisplayName,
    ProfileProgressDirection TrendDirection,
    string Summary,
    IReadOnlyList<TrainingPlanTopic> Topics,
    IReadOnlyList<TrainingRecommendation> Recommendations,
    WeeklyTrainingPlan WeeklyPlan,
    IReadOnlyList<TrainingPlanDashboardItem>? WhyThisIsYourCurrentPlan = null);
