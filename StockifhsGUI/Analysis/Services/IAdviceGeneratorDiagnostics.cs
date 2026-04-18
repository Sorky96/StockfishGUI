namespace StockifhsGUI;

public interface IAdviceGeneratorDiagnostics
{
    bool UsedFallback { get; }

    string? FallbackReason { get; }
}
