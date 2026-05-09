namespace MoveMentorChess.Training;

internal sealed class TrainingAnalysisDataSource
{
    private readonly IStoredMoveAnalysisStore moveAnalysisStore;
    private readonly IAnalysisResultStore resultStore;
    private readonly Dictionary<TrainingAnalysisDataKey, TrainingAnalysisDataSet> cache = new();

    public TrainingAnalysisDataSource(IStoredMoveAnalysisStore moveAnalysisStore, IAnalysisResultStore resultStore)
    {
        this.moveAnalysisStore = moveAnalysisStore ?? throw new ArgumentNullException(nameof(moveAnalysisStore));
        this.resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
    }

    public TrainingAnalysisDataSet Load(string? filterText, int limit)
    {
        string normalizedFilter = filterText?.Trim() ?? string.Empty;
        string? effectiveFilter = string.IsNullOrWhiteSpace(normalizedFilter) ? null : normalizedFilter;
        int moveLimit = Math.Clamp(limit * 64, 500, 50000);
        int resultLimit = Math.Max(limit * 8, 200);
        TrainingAnalysisDataKey key = new(normalizedFilter, moveLimit, resultLimit);

        if (cache.TryGetValue(key, out TrainingAnalysisDataSet? cached))
        {
            return cached;
        }

        TrainingAnalysisDataSet loaded = new(
            moveAnalysisStore.ListMoveAnalyses(effectiveFilter, moveLimit),
            resultStore.ListResults(effectiveFilter, resultLimit));
        cache[key] = loaded;
        return loaded;
    }

    private readonly record struct TrainingAnalysisDataKey(
        string FilterText,
        int MoveLimit,
        int ResultLimit);
}

internal sealed record TrainingAnalysisDataSet(
    IReadOnlyList<StoredMoveAnalysis> StoredMoves,
    IReadOnlyList<GameAnalysisResult> Results);
