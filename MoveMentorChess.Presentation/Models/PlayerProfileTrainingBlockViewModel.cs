namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfileTrainingBlockViewModel(
    string PurposeLabel,
    string KindLabel,
    string Title,
    string Description,
    int EstimatedMinutes);
