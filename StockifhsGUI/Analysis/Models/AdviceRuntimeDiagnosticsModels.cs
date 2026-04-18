namespace StockifhsGUI;

public sealed record AdviceRuntimeInvocationLog(
    DateTime TimestampUtc,
    string RuntimeName,
    string ExecutablePath,
    string WorkingDirectory,
    string ModelPath,
    string CommandLine,
    int PromptLength,
    string PromptPreview,
    int MaxTokens,
    int ContextSize,
    int TimeoutMs,
    long DurationMs,
    int? ExitCode,
    bool TimedOut,
    bool Success,
    string Stdout,
    string Stderr,
    string? FailureMessage);
