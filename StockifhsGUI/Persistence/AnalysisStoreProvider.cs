namespace StockifhsGUI;

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
            return SqliteAnalysisStore.CreateDefault();
        }
        catch
        {
            return null;
        }
    }
}
