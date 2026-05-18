using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal interface ISavedLibraryDataService
{
    bool IsAvailable { get; }

    IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText);

    bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game);

    bool DeleteGameAndCachedAnalysis(string gameFingerprint);

    IReadOnlyList<GameAnalysisResult> ListResults(string? filterText, int limit);
}
