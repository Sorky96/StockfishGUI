namespace StockifhsGUI;

public interface IAnalysisStore
{
    void SaveImportedGame(ImportedGame game);
    void SaveImportedGames(IReadOnlyList<ImportedGame> games);
    bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game);
    bool DeleteImportedGame(string gameFingerprint);
    IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200);
    IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500);
    IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000);
    bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result);
    void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result);
    bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state);
    void SaveWindowState(string gameFingerprint, AnalysisWindowState state);
}

public interface IOpeningTreeStore
{
    void SaveOpeningTree(OpeningTreeBuildResult tree);
    OpeningTreeStoreSummary GetOpeningTreeSummary();
}

public interface IOpeningTheoryStore
{
    bool TryGetOpeningPositionByKey(string positionKey, out OpeningTheoryPosition? position);
    IReadOnlyList<OpeningTheoryMove> GetOpeningMovesByPositionKey(
        string positionKey,
        int limit = 10,
        bool playableOnly = false);
}
