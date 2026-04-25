namespace MoveMentorChessServices;

public interface IAdviceGeneratorDiagnostics
{
    bool UsedFallback { get; }

    string? FallbackReason { get; }
}
