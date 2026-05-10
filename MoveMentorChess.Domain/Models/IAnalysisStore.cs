namespace MoveMentorChess.Domain;

public interface IImportedGameStore
{
    void SaveImportedGame(ImportedGame game);
    void SaveImportedGames(IReadOnlyList<ImportedGame> games);
    bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game);
    bool DeleteImportedGame(string gameFingerprint);
    void ClearImportedAnalysisData() { }
    IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200);
}

public interface IAnalysisResultStore
{
    IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500);
    bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result);
    void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result);
}

public interface IStoredMoveAnalysisStore
{
    IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000);
}

public interface IAdviceFeedbackStore
{
    IReadOnlyList<MoveAdviceFeedback> ListMoveAdviceFeedback(string? filterText = null, int limit = 5000) => [];
    void SaveMoveAdviceFeedback(MoveAdviceFeedback feedback) { }
}

public interface IAnalysisWindowStateStore
{
    bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state);
    void SaveWindowState(string gameFingerprint, AnalysisWindowState state);
}

public interface IAnalysisStore :
    IImportedGameStore,
    IAnalysisResultStore,
    IStoredMoveAnalysisStore,
    IAdviceFeedbackStore,
    IAnalysisWindowStateStore
{
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
    IReadOnlyList<OpeningLineCatalogItem> ListOpeningLines(string? filterText = null, RepertoireSide? repertoireSide = null, int limit = 100) => [];
    bool TryGetOpeningOverview(
        OpeningLineKey lineKey,
        RepertoireSide repertoireSide,
        int maxDepth,
        out OpeningTrainerOverview? overview)
    {
        overview = null;
        return false;
    }
}

public interface IOpeningTrainingHistoryStore
{
    void SaveOpeningTrainingSessionResult(OpeningTrainingSessionResult result);
    IReadOnlyList<OpeningTrainingSessionResult> ListOpeningTrainingSessionResults(string? playerKey = null, int limit = 200);
    void SaveOpeningReviewItems(string playerKey, IReadOnlyList<OpeningReviewItem> items) { }
    IReadOnlyList<OpeningReviewItem> ListOpeningReviewItems(string? playerKey = null, int limit = 1000) => [];
}
