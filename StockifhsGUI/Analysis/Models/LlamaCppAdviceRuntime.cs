namespace StockifhsGUI;

public sealed record LlamaCppAdviceRuntime(
    string CliPath,
    string ModelPath,
    int MaxTokens = 96,
    int ContextSize = 2048,
    int TimeoutMs = 45000);
