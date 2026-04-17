namespace StockifhsGUI;

public interface IAnalysisStore
{
    void SaveImportedGame(ImportedGame game);
    bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game);
    IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200);
    IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500);
    bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result);
    void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result);
    bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state);
    void SaveWindowState(string gameFingerprint, AnalysisWindowState state);
}
