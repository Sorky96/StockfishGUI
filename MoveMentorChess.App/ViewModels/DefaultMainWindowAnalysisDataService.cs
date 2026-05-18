using MoveMentorChess.Analysis;
using MoveMentorChess.Opening;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed class DefaultMainWindowAnalysisDataService : IMainWindowAnalysisDataService
{
    private readonly Func<IAnalysisStore?> analysisStoreProvider;

    public DefaultMainWindowAnalysisDataService(Func<IAnalysisStore?> analysisStoreProvider)
    {
        this.analysisStoreProvider = analysisStoreProvider ?? throw new ArgumentNullException(nameof(analysisStoreProvider));
    }

    public void SaveImportedGame(ImportedGame game)
    {
        IAnalysisStore? store = analysisStoreProvider();
        if (store is null)
        {
            return;
        }

        try
        {
            store.SaveImportedGame(game);
        }
        catch
        {
            // Import should still succeed even if local persistence is temporarily unavailable.
        }
    }

    public void SaveImportedGames(IReadOnlyList<ImportedGame> games)
    {
        IAnalysisStore? store = analysisStoreProvider();
        if (store is null || games.Count == 0)
        {
            return;
        }

        try
        {
            store.SaveImportedGames(games);
        }
        catch
        {
            // Import should still succeed even if local persistence is temporarily unavailable.
        }
    }

    public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
    {
        IAnalysisStore? store = analysisStoreProvider();
        if (store is null)
        {
            game = null;
            return false;
        }

        return store.TryLoadImportedGame(gameFingerprint, out game) && game is not null;
    }

    public bool TryGetCachedAnalysis(ImportedGame game, PlayerSide side, EngineAnalysisOptions options, out GameAnalysisResult? result)
    {
        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(game, side, options);
        return GameAnalysisCache.TryGetResult(cacheKey, out result) && result is not null;
    }

    public void StoreAnalysisResult(ImportedGame game, PlayerSide side, EngineAnalysisOptions options, GameAnalysisResult result)
    {
        GameAnalysisCache.StoreResult(GameAnalysisCache.CreateKey(game, side, options), result);
    }

    public OpeningTheoryQueryService? CreateOpeningTheory()
    {
        IAnalysisStore? store = analysisStoreProvider();
        return store is null ? null : OpeningTheorySourceResolver.Create(store);
    }
}
