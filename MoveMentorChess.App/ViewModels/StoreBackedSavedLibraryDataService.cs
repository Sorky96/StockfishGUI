using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed class StoreBackedSavedLibraryDataService(IAnalysisStore analysisStore) : ISavedLibraryDataService
{
    private readonly IAnalysisStore analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));

    public bool IsAvailable => true;

    public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText)
        => analysisStore.ListImportedGames(filterText);

    public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
        => analysisStore.TryLoadImportedGame(gameFingerprint, out game);

    public bool DeleteGameAndCachedAnalysis(string gameFingerprint)
    {
        if (!analysisStore.DeleteImportedGame(gameFingerprint))
        {
            return false;
        }

        GameAnalysisCache.RemoveGame(gameFingerprint);
        return true;
    }

    public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText, int limit)
        => analysisStore.ListResults(filterText, limit);
}
