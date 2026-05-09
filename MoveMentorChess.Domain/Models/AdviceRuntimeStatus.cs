namespace MoveMentorChess.Domain;

public sealed record AdviceRuntimeStatus(
    bool IsReady,
    string StatusText,
    string? RuntimeName = null,
    string? InstallHint = null);
