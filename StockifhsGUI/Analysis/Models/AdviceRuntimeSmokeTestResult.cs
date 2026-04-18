namespace StockifhsGUI;

public sealed record AdviceRuntimeSmokeTestResult(
    bool Success,
    string Message,
    string? RawResponse = null,
    string? DiagnosticPath = null);
