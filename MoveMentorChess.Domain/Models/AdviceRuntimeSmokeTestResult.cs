namespace MoveMentorChess.Domain;

public sealed record AdviceRuntimeSmokeTestResult(
    bool Success,
    string Message,
    string? RawResponse = null,
    string? DiagnosticPath = null);
