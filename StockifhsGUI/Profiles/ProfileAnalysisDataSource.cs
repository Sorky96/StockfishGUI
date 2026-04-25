namespace StockifhsGUI;

internal sealed class ProfileAnalysisDataSource
{
    private readonly IAnalysisStore analysisStore;
    private readonly Dictionary<ProfileAnalysisDataKey, ProfileAnalysisDataSet> cache = new();

    public ProfileAnalysisDataSource(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
    }

    public ProfileAnalysisDataSet Load(string? filterText, int limit)
    {
        string normalizedFilter = filterText?.Trim() ?? string.Empty;
        string? effectiveFilter = string.IsNullOrWhiteSpace(normalizedFilter) ? null : normalizedFilter;
        int moveLimit = Math.Clamp(limit * 64, 500, 50000);
        int resultLimit = Math.Max(limit * 8, 200);
        ProfileAnalysisDataKey key = new(normalizedFilter, moveLimit, resultLimit);

        if (cache.TryGetValue(key, out ProfileAnalysisDataSet? cached))
        {
            return cached;
        }

        ProfileAnalysisDataSet loaded = new(
            analysisStore.ListMoveAnalyses(effectiveFilter, moveLimit),
            analysisStore.ListResults(effectiveFilter, resultLimit));
        cache[key] = loaded;
        return loaded;
    }

    private readonly record struct ProfileAnalysisDataKey(
        string FilterText,
        int MoveLimit,
        int ResultLimit);
}

internal sealed record ProfileAnalysisDataSet(
    IReadOnlyList<StoredMoveAnalysis> StoredMoves,
    IReadOnlyList<GameAnalysisResult> Results);
