using MoveMentorChess.Opening;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed class DefaultAnalysisWindowDataService(Func<IAnalysisStore?> storeProvider) : IAnalysisWindowDataService
{
    public DefaultAnalysisWindowDataService()
        : this(AnalysisStoreProvider.GetStore)
    {
    }

    public bool IsAnalysisForGame(GameAnalysisResult result, ImportedGame game)
        => AnalysisResultCacheLoader.IsAnalysisForGame(result, game);

    public bool TryLoadExistingResult(
        ImportedGame importedGame,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult> initialResultsBySide,
        out GameAnalysisResult? result,
        out GameAnalysisCacheKey cacheKey,
        out string statusText)
        => AnalysisResultCacheLoader.TryLoadExistingResult(
            importedGame,
            side,
            analysisOptions,
            initialResultsBySide,
            out result,
            out cacheKey,
            out statusText);

    public bool TryGetWindowState(ImportedGame importedGame, out AnalysisWindowState? state)
        => AnalysisResultCacheLoader.TryGetWindowState(importedGame, out state);

    public void StoreWindowState(ImportedGame importedGame, AnalysisWindowState state)
        => AnalysisResultCacheLoader.StoreWindowState(importedGame, state);

    public void StoreResult(GameAnalysisCacheKey cacheKey, GameAnalysisResult result)
        => AnalysisResultCacheLoader.StoreResult(cacheKey, result);

    public void SaveMoveAdviceFeedback(MoveAdviceFeedback feedback)
        => storeProvider()?.SaveMoveAdviceFeedback(feedback);

    public MoveAdviceFeedback? FindLatestFeedback(
        ImportedGame importedGame,
        PlayerSide analyzedSide,
        EngineAnalysisOptions analysisOptions,
        MoveAnalysisResult lead)
        => AnalysisFeedbackService.FindLatestFeedback(
            storeProvider(),
            importedGame,
            analyzedSide,
            analysisOptions,
            lead);

    public OpeningTheoryQueryService? CreateOpeningTheory()
    {
        IAnalysisStore? store = storeProvider();
        return store is null ? null : OpeningTheorySourceResolver.Create(store);
    }
}
