using StockifhsGUI;

namespace StockifhsGUI.OpeningSeedTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            SeedToolArguments parsed = SeedToolArguments.Parse(args);
            string seedVersion = string.IsNullOrWhiteSpace(parsed.SeedVersion)
                ? $"seed-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : parsed.SeedVersion;

            Console.WriteLine("Building opening seed...");
            Console.WriteLine($"Output: {Path.GetFullPath(parsed.OutputSeedPath)}");
            Console.WriteLine($"Seed version: {seedVersion}");
            Console.WriteLine($"Feed profile: {parsed.FeedProfile}");

            NullAnalysisStore scratchStore = new();
            OpeningPgnImportService importService = new(scratchStore, retainParsedGames: false, persistImportedGames: false);
            OpeningPgnImportResult result = !string.IsNullOrWhiteSpace(parsed.InputFolder)
                ? importService.ImportFolder(parsed.InputFolder, parsed.MaxFullMoves, WriteProgress)
                : importService.ImportFile(parsed.InputFile!, parsed.MaxFullMoves, WriteProgress);

            OpeningTreeBuildResult seedTree = result.Tree;
            if (parsed.FeedProfile.Equals("production", StringComparison.OrdinalIgnoreCase))
            {
                OpeningTreePruningOptions pruningOptions = new(
                    parsed.MinDistinctGames,
                    parsed.MaxMovesPerPosition,
                    parsed.MinMoveShare,
                    parsed.AlwaysKeepMainMove);
                seedTree = new OpeningTreePruner().Prune(result.Tree, pruningOptions);
                seedTree = new OpeningTreePostProcessor().Process(seedTree);
            }

            string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(parsed.OutputSeedPath));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (File.Exists(parsed.OutputSeedPath))
            {
                File.Delete(parsed.OutputSeedPath);
            }

            SqliteAnalysisStore seedStore = new(parsed.OutputSeedPath);
            seedStore.ReplaceOpeningTree(seedTree);
            seedStore.SetOpeningSeedVersion(seedVersion);

            Console.WriteLine();
            Console.WriteLine("Opening seed completed.");
            Console.WriteLine($"Files processed: {result.FilesProcessed}");
            Console.WriteLine($"Games processed: {result.GamesProcessed}");
            Console.WriteLine($"Skipped games: {result.SkippedGames}");
            Console.WriteLine($"Raw positions: {result.Tree.Nodes.Count}");
            Console.WriteLine($"Raw edges: {result.Tree.Edges.Count}");
            Console.WriteLine($"Seed positions: {seedTree.Nodes.Count}");
            Console.WriteLine($"Seed edges: {seedTree.Edges.Count}");
            Console.WriteLine($"Seed tags: {seedTree.Tags.Count}");
            Console.WriteLine("Copy the generated seed database into the GUI app under OpeningSeed\\opening-seed.db.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void WriteProgress(OpeningPgnImportProgress progress)
    {
        Console.WriteLine(
            $"[{progress.CurrentFileIndex}/{progress.TotalFiles}] {Path.GetFileName(progress.CurrentFilePath)} | " +
            $"file games: {progress.GamesProcessedInCurrentFile} | total games: {progress.TotalGamesProcessed} | skipped: {progress.SkippedGames} | " +
            $"nodes: {progress.NodeCount} | edges: {progress.EdgeCount} | plies: {progress.TotalPliesParsed} | " +
            $"{progress.GamesPerMinute:F0} games/min | {progress.MegabytesPerMinute:F1} MB/min");
    }

    private sealed record SeedToolArguments(
        string? InputFolder,
        string? InputFile,
        string OutputSeedPath,
        int MaxFullMoves,
        string? SeedVersion,
        string FeedProfile,
        int MinDistinctGames,
        int MaxMovesPerPosition,
        double MinMoveShare,
        bool AlwaysKeepMainMove)
    {
        public static SeedToolArguments Parse(string[] args)
        {
            string? inputFolder = GetArgumentValue(args, "--input-folder");
            string? inputFile = GetArgumentValue(args, "--input-file");
            if (string.IsNullOrWhiteSpace(inputFolder) == string.IsNullOrWhiteSpace(inputFile))
            {
                throw new InvalidOperationException(
                    "Specify exactly one input source: --input-folder <path> or --input-file <path>.");
            }

            string outputSeedPath = GetArgumentValue(args, "--output-seed") ?? "opening-seed.db";
            int maxFullMoves = int.TryParse(GetArgumentValue(args, "--max-full-moves"), out int parsedMaxFullMoves)
                ? Math.Max(0, parsedMaxFullMoves)
                : OpeningGameParser.DefaultMaxFullMoves;
            string? seedVersion = GetArgumentValue(args, "--seed-version");
            string feedProfile = GetArgumentValue(args, "--feed-profile") ?? "production";
            if (!feedProfile.Equals("production", StringComparison.OrdinalIgnoreCase)
                && !feedProfile.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("--feed-profile must be either 'production' or 'full'.");
            }

            OpeningTreePruningOptions defaults = OpeningTreePruningOptions.ProductionDefault;
            int minDistinctGames = int.TryParse(GetArgumentValue(args, "--min-distinct-games"), out int parsedMinDistinctGames)
                ? Math.Max(1, parsedMinDistinctGames)
                : defaults.MinDistinctGames;
            int maxMovesPerPosition = int.TryParse(GetArgumentValue(args, "--max-moves-per-position"), out int parsedMaxMovesPerPosition)
                ? Math.Max(1, parsedMaxMovesPerPosition)
                : defaults.MaxMovesPerPosition;
            double minMoveShare = double.TryParse(GetArgumentValue(args, "--min-move-share"), out double parsedMinMoveShare)
                ? Math.Clamp(parsedMinMoveShare, 0, 1)
                : defaults.MinMoveShare;
            bool alwaysKeepMainMove = !string.Equals(
                GetArgumentValue(args, "--always-keep-main-move"),
                "false",
                StringComparison.OrdinalIgnoreCase);

            return new SeedToolArguments(
                inputFolder,
                inputFile,
                outputSeedPath,
                maxFullMoves,
                seedVersion,
                feedProfile,
                minDistinctGames,
                maxMovesPerPosition,
                minMoveShare,
                alwaysKeepMainMove);
        }

        private static string? GetArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }

    private sealed class NullAnalysisStore : IAnalysisStore
    {
        public void SaveImportedGame(ImportedGame game)
        {
        }

        public void SaveImportedGames(IReadOnlyList<ImportedGame> games)
        {
        }

        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
        {
            game = null;
            return false;
        }

        public bool DeleteImportedGame(string gameFingerprint) => false;
        public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200) => [];
        public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500) => [];
        public IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000) => [];

        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result)
        {
            result = null;
            return false;
        }

        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result)
        {
        }

        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state)
        {
            state = null;
            return false;
        }

        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state)
        {
        }
    }
}
