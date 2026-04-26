namespace MoveMentorChessServices;

public sealed record StockfishSettings(
    int Threads,
    int HashMb,
    int BulkAnalysisDepth,
    int BulkAnalysisMultiPv,
    int BulkAnalysisMoveTimeMs)
{
    public static StockfishSettings Default { get; } = new(
        Threads: Math.Max(1, Environment.ProcessorCount - 1),
        HashMb: 256,
        BulkAnalysisDepth: 7,
        BulkAnalysisMultiPv: 1,
        BulkAnalysisMoveTimeMs: 250);

    public StockfishEngineOptions ToEngineOptions()
        => new(
            Threads: Math.Max(1, Threads),
            HashMb: Math.Max(16, HashMb));

    public EngineAnalysisOptions ToBulkAnalysisOptions()
        => new(
            Depth: Math.Max(1, BulkAnalysisDepth),
            MultiPv: Math.Max(1, BulkAnalysisMultiPv),
            MoveTimeMs: Math.Max(1, BulkAnalysisMoveTimeMs));
}
