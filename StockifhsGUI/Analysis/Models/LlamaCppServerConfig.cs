namespace StockifhsGUI;

public sealed record LlamaCppServerConfig(
    string ServerPath,
    string ModelPath,
    int Port = 0,
    int ContextSize = 2048,
    int MaxTokens = 256,
    int TimeoutMs = 30000,
    int StartupTimeoutMs = 90000);
