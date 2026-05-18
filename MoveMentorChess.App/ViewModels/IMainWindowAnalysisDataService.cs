using MoveMentorChess.Analysis;
using MoveMentorChess.Opening;

namespace MoveMentorChess.App.ViewModels;

internal interface IMainWindowAnalysisDataService
{
    void SaveImportedGame(ImportedGame game);

    void SaveImportedGames(IReadOnlyList<ImportedGame> games);

    bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game);

    bool TryGetCachedAnalysis(ImportedGame game, PlayerSide side, EngineAnalysisOptions options, out GameAnalysisResult? result);

    void StoreAnalysisResult(ImportedGame game, PlayerSide side, EngineAnalysisOptions options, GameAnalysisResult result);

    OpeningTheoryQueryService? CreateOpeningTheory();
}
