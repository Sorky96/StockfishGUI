using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed class DefaultSavedLibraryDataService(Func<IAnalysisStore?> analysisStoreProvider) : ISavedLibraryDataService
{
    public bool IsAvailable => analysisStoreProvider() is not null;

    public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText)
        => GetRequiredStore().ListImportedGames(filterText);

    public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
        => GetRequiredStore().TryLoadImportedGame(gameFingerprint, out game);

    public bool DeleteGameAndCachedAnalysis(string gameFingerprint)
    {
        if (!GetRequiredStore().DeleteImportedGame(gameFingerprint))
        {
            return false;
        }

        GameAnalysisCache.RemoveGame(gameFingerprint);
        return true;
    }

    public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText, int limit)
        => GetRequiredStore().ListResults(filterText, limit);

    private IAnalysisStore GetRequiredStore()
        => analysisStoreProvider() ?? throw new InvalidOperationException("Local analysis store is unavailable.");
}
