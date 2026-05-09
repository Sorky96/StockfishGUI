namespace MoveMentorChess.Analysis;

public interface IAdviceGeneratorDiagnostics
{
    bool UsedFallback { get; }

    string? FallbackReason { get; }
}
