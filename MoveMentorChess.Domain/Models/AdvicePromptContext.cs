namespace MoveMentorChess.Domain;

public sealed record AdvicePromptContext(
    string? OpeningName = null,
    string? AnalyzedPlayer = null,
    string? OpponentPlayer = null,
    string? BestMoveSan = null,
    IReadOnlyList<string>? Evidence = null,
    IReadOnlyList<string>? HeuristicNotes = null,
    PlayerMistakeProfile? PlayerProfile = null);
