using MoveMentorChess.Opening;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

public interface IAnalysisWindowDataService
{
    DateTime UtcNow { get; }

    bool IsAnalysisForGame(GameAnalysisResult result, ImportedGame game);

    bool TryLoadExistingResult(
        ImportedGame importedGame,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult> initialResultsBySide,
        out GameAnalysisResult? result,
        out GameAnalysisCacheKey cacheKey,
        out string statusText);

    bool TryGetWindowState(ImportedGame importedGame, out AnalysisWindowState? state);

    void StoreWindowState(ImportedGame importedGame, AnalysisWindowState state);

    void StoreResult(GameAnalysisCacheKey cacheKey, GameAnalysisResult result);

    void SaveMoveAdviceFeedback(MoveAdviceFeedback feedback);

    MoveAdviceFeedback? FindLatestFeedback(
        ImportedGame importedGame,
        PlayerSide analyzedSide,
        EngineAnalysisOptions analysisOptions,
        MoveAnalysisResult lead);

    OpeningTheoryQueryService? CreateOpeningTheory();
}
