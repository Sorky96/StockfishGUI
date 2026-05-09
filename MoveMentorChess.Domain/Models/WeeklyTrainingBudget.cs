namespace MoveMentorChess.Domain;

public sealed record WeeklyTrainingBudget(
    int TotalMinutes,
    int CoreWeaknessMinutes,
    int SecondaryWeaknessMinutes,
    int MaintenanceMinutes,
    int IntegrationMinutes,
    string Summary);
