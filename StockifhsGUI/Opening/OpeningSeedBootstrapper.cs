namespace StockifhsGUI;

public sealed class OpeningSeedBootstrapper
{
    public const string BundledSeedRelativePath = @"OpeningSeed\opening-seed.db";

    private readonly string localDatabasePath;
    private readonly string bundledSeedPath;

    public OpeningSeedBootstrapper(string localDatabasePath, string bundledSeedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundledSeedPath);

        this.localDatabasePath = localDatabasePath;
        this.bundledSeedPath = bundledSeedPath;
    }

    public static string GetDefaultBundledSeedPath()
        => Path.Combine(AppContext.BaseDirectory, BundledSeedRelativePath);

    public OpeningSeedBootstrapResult EnsureSeedImported()
    {
        if (!File.Exists(bundledSeedPath))
        {
            return new OpeningSeedBootstrapResult(false, false, null, new OpeningTreeStoreSummary(0, 0, 0));
        }

        SqliteAnalysisStore bundledSeedStore = new(bundledSeedPath);
        SqliteAnalysisStore localStore = new(localDatabasePath);
        string seedVersion = bundledSeedStore.GetOpeningSeedVersion() ?? BuildFallbackSeedVersion();
        OpeningTreeStoreSummary localSummary = localStore.GetOpeningTreeSummary();

        if (string.Equals(localStore.GetOpeningSeedVersion(), seedVersion, StringComparison.Ordinal)
            && localSummary.NodeCount > 0
            && localSummary.EdgeCount > 0)
        {
            return new OpeningSeedBootstrapResult(true, false, seedVersion, localSummary);
        }

        OpeningTreeBuildResult tree = bundledSeedStore.LoadOpeningTree();
        if (tree.Nodes.Count == 0 || tree.Edges.Count == 0)
        {
            return new OpeningSeedBootstrapResult(true, false, seedVersion, localSummary);
        }

        localStore.ReplaceOpeningTree(tree);
        localStore.SetOpeningSeedVersion(seedVersion);
        OpeningTreeStoreSummary importedSummary = localStore.GetOpeningTreeSummary();
        return new OpeningSeedBootstrapResult(true, true, seedVersion, importedSummary);
    }

    private string BuildFallbackSeedVersion()
    {
        DateTime utc = File.GetLastWriteTimeUtc(bundledSeedPath);
        return $"file-{utc:yyyyMMddHHmmss}";
    }
}

public sealed record OpeningSeedBootstrapResult(
    bool SeedFileFound,
    bool Imported,
    string? SeedVersion,
    OpeningTreeStoreSummary Summary);
