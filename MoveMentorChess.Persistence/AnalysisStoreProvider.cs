namespace MoveMentorChess.Persistence;

public static class AnalysisStoreProvider
{
    private static readonly object Sync = new();
    private static readonly Lazy<IAnalysisStore?> DefaultStore = new(CreateDefaultStoreSafely);

    private static IAnalysisStore? overrideStore;
    private static bool overrideEnabled;

    public static IAnalysisStore? GetStore()
    {
        lock (Sync)
        {
            if (overrideEnabled)
            {
                return overrideStore;
            }
        }

        return DefaultStore.Value;
    }

    public static void Override(IAnalysisStore? store)
    {
        lock (Sync)
        {
            overrideStore = store;
            overrideEnabled = true;
        }
    }

    public static void ResetOverride()
    {
        lock (Sync)
        {
            overrideStore = null;
            overrideEnabled = false;
        }
    }

    private static IAnalysisStore? CreateDefaultStoreSafely()
    {
        try
        {
            EnsureBundledOpeningSeedImported();
            return SqliteAnalysisStore.CreateDefault();
        }
        catch (Exception ex)
        {
            PersistenceDiagnostics.Warning(
                nameof(AnalysisStoreProvider),
                "Failed to create the default analysis store. Persistent analysis data will be unavailable.",
                ex);
            return null;
        }
    }

    private static void EnsureBundledOpeningSeedImported()
    {
        OpeningSeedBootstrapper bootstrapper = new(
            SqliteAnalysisStore.GetDefaultDatabasePath(),
            OpeningSeedBootstrapper.GetDefaultBundledSeedPath());
        bootstrapper.EnsureSeedImported();
    }
}
