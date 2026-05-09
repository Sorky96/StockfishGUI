namespace MoveMentorChess.Domain;

public sealed record TrainingPlanPriorityBreakdown(
    int FrequencyScore,
    int CostScore,
    int TrendScore,
    int PhaseScore,
    int TotalScore,
    int TrainingScore = 0);
