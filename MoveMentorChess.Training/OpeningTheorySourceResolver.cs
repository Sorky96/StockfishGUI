namespace MoveMentorChess.Training;

public static class OpeningTheorySourceResolver
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

    public static OpeningTheoryQueryService Create(IOpeningTheoryStore theoryStore)
    {
        ArgumentNullException.ThrowIfNull(theoryStore);

        return new OpeningTheoryQueryService(theoryStore);
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
