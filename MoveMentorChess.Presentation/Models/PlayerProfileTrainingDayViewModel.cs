namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfileTrainingDayViewModel(
    int DayNumber,
    string Topic,
    string WorkType,
    string Goal,
    int EstimatedMinutes,
    string RoleLabel);
