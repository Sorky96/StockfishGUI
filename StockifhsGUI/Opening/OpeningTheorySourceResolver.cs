namespace StockifhsGUI;

internal static class OpeningTheorySourceResolver
{
    public static OpeningTheoryQueryService? Create(IAnalysisStore analysisStore)
    {
        ArgumentNullException.ThrowIfNull(analysisStore);

        IOpeningTheoryStore? theoryStore = analysisStore is SqliteAnalysisStore
            ? TryCreateBundledSeedStore()
            : analysisStore as IOpeningTheoryStore;

        return theoryStore is null
            ? null
            : new OpeningTheoryQueryService(theoryStore);
    }

    private static IOpeningTheoryStore? TryCreateBundledSeedStore()
    {
        string seedPath = OpeningSeedBootstrapper.GetDefaultBundledSeedPath();
        if (!File.Exists(seedPath))
        {
            return null;
        }

        return new SqliteAnalysisStore(seedPath);
    }
}
