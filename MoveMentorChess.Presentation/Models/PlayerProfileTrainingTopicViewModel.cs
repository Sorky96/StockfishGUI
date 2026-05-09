namespace MoveMentorChess.Presentation.Models;

internal sealed record PlayerProfileTrainingTopicViewModel(
    string RoleLabel,
    string StatusLabel,
    string Title,
    string FocusArea,
    string Summary,
    string WhyThisTopicNow,
    string Rationale,
    string? Context,
    IReadOnlyList<PlayerProfileTrainingBlockViewModel> Blocks);
