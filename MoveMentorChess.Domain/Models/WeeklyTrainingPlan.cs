namespace MoveMentorChess.Domain;

public sealed record WeeklyTrainingPlan(
    string Title,
    string Summary,
    WeeklyTrainingBudget Budget,
    IReadOnlyList<WeeklyTrainingDay> Days);
