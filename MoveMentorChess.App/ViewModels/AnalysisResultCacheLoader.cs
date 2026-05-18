using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal static class AnalysisResultCacheLoader
{
    public static bool TryLoadExistingResult(
        ImportedGame importedGame,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult> initialResultsBySide,
        out GameAnalysisResult? result,
        out GameAnalysisCacheKey cacheKey,
        out string statusText)
    {
        cacheKey = GameAnalysisCache.CreateKey(importedGame, side, analysisOptions);

        if (TryGetInitialResult(importedGame, side, initialResultsBySide, out result) && result is not null)
        {
            statusText = $"Loaded saved analysis for {side}.";
            return true;
        }

        if (GameAnalysisCache.TryGetResult(cacheKey, out result) && result is not null)
        {
            statusText = $"Loaded cached analysis for {side}.";
            return true;
        }

        statusText = $"No cached analysis for {side}. Run analysis to generate it.";
        return false;
    }

    public static bool TryGetWindowState(ImportedGame importedGame, out AnalysisWindowState? state)
        => GameAnalysisCache.TryGetWindowState(importedGame, out state);

    public static void StoreWindowState(ImportedGame importedGame, AnalysisWindowState state)
        => GameAnalysisCache.StoreWindowState(importedGame, state);

    public static void StoreResult(GameAnalysisCacheKey cacheKey, GameAnalysisResult result)
        => GameAnalysisCache.StoreResult(cacheKey, result);

    private static bool TryGetInitialResult(
        ImportedGame importedGame,
        PlayerSide side,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult> initialResultsBySide,
        out GameAnalysisResult? result)
    {
        result = null;
        if (!initialResultsBySide.TryGetValue(side, out GameAnalysisResult? candidate)
            || !IsAnalysisForGame(candidate, importedGame))
        {
            return false;
        }

        result = candidate;
        return true;
    }

    public static bool IsAnalysisForGame(GameAnalysisResult result, ImportedGame game)
    {
        return string.Equals(
            GameFingerprint.Compute(result.Game.PgnText),
            GameFingerprint.Compute(game.PgnText),
            StringComparison.Ordinal);
    }
}
