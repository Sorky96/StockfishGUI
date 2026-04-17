namespace StockifhsGUI;

public static class GameAnalysisCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<GameAnalysisCacheKey, GameAnalysisResult> ResultCache = new();
    private static readonly Dictionary<string, AnalysisWindowState> WindowStateCache = new();

    public static GameAnalysisCacheKey CreateKey(ImportedGame game, PlayerSide side, EngineAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);

        return new GameAnalysisCacheKey(
            GameFingerprint.Compute(game.PgnText),
            side,
            options.Depth,
            options.MultiPv,
            options.MoveTimeMs);
    }

    public static bool TryGetResult(GameAnalysisCacheKey key, out GameAnalysisResult? result)
    {
        lock (Sync)
        {
            if (ResultCache.TryGetValue(key, out result))
            {
                return true;
            }
        }

        IAnalysisStore? store = GetPersistentStore();
        if (store is not null && TryLoadResultFromStore(store, key, out result) && result is not null)
        {
            lock (Sync)
            {
                ResultCache[key] = result;
            }

            return true;
        }

        result = null;
        return false;
    }

    public static void OverridePersistentStore(IAnalysisStore? store)
    {
        AnalysisStoreProvider.Override(store);
        Clear();
    }

    public static void ResetPersistentStoreOverride()
    {
        AnalysisStoreProvider.ResetOverride();
        Clear();
    }

    public static void StoreResult(GameAnalysisCacheKey key, GameAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (Sync)
        {
            ResultCache[key] = result;
        }

        IAnalysisStore? store = GetPersistentStore();
        if (store is null)
        {
            return;
        }

        try
        {
            store.SaveResult(key, result);
        }
        catch
        {
            // Keep in-memory cache working even if persistence is unavailable.
        }
    }

    public static bool TryGetWindowState(ImportedGame game, out AnalysisWindowState? state)
    {
        ArgumentNullException.ThrowIfNull(game);
        string fingerprint = GameFingerprint.Compute(game.PgnText);

        lock (Sync)
        {
            if (WindowStateCache.TryGetValue(fingerprint, out state))
            {
                return true;
            }
        }

        IAnalysisStore? store = GetPersistentStore();
        if (store is not null && TryLoadWindowStateFromStore(store, fingerprint, out state) && state is not null)
        {
            lock (Sync)
            {
                WindowStateCache[fingerprint] = state;
            }

            return true;
        }

        state = null;
        return false;
    }

    public static void StoreWindowState(ImportedGame game, AnalysisWindowState state)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(state);

        string fingerprint = GameFingerprint.Compute(game.PgnText);
        lock (Sync)
        {
            WindowStateCache[fingerprint] = state;
        }

        IAnalysisStore? store = GetPersistentStore();
        if (store is null)
        {
            return;
        }

        try
        {
            store.SaveWindowState(fingerprint, state);
        }
        catch
        {
            // Keep in-memory cache working even if persistence is unavailable.
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            ResultCache.Clear();
            WindowStateCache.Clear();
        }
    }

    public static void RemoveGame(string gameFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);

        lock (Sync)
        {
            List<GameAnalysisCacheKey> keysToRemove = ResultCache.Keys
                .Where(key => string.Equals(key.GameFingerprint, gameFingerprint, StringComparison.Ordinal))
                .ToList();

            foreach (GameAnalysisCacheKey key in keysToRemove)
            {
                ResultCache.Remove(key);
            }

            WindowStateCache.Remove(gameFingerprint);
        }
    }

    private static bool TryLoadResultFromStore(IAnalysisStore store, GameAnalysisCacheKey key, out GameAnalysisResult? result)
    {
        try
        {
            return store.TryLoadResult(key, out result);
        }
        catch
        {
            result = null;
            return false;
        }
    }

    private static bool TryLoadWindowStateFromStore(IAnalysisStore store, string fingerprint, out AnalysisWindowState? state)
    {
        try
        {
            return store.TryLoadWindowState(fingerprint, out state);
        }
        catch
        {
            state = null;
            return false;
        }
    }

    private static IAnalysisStore? GetPersistentStore()
    {
        return AnalysisStoreProvider.GetStore();
    }
}

public sealed record GameAnalysisCacheKey(
    string GameFingerprint,
    PlayerSide Side,
    int Depth,
    int MultiPv,
    int? MoveTimeMs);

public sealed record AnalysisWindowState(
    PlayerSide SelectedSide,
    int QualityFilterIndex,
    int ExplanationLevelIndex = 1);
