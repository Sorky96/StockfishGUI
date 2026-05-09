namespace MoveMentorChess.Domain;

public sealed record LocalAdviceModelOptions(
    string Command,
    string? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutMs = 45000);
