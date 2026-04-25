using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace MoveMentorChessServices;

public sealed class OpeningPgnImportService
{
    private const int ProgressReportIntervalGames = 100;
    private const int GamesPerChunk = 128;

    private readonly IAnalysisStore store;
    private readonly IOpeningTreeStore? treeStore;
    private readonly OpeningGameParser parser;
    private readonly OpeningTreePostProcessor postProcessor;
    private readonly bool retainParsedGames;
    private readonly bool persistImportedGames;

    public OpeningPgnImportService(
        IAnalysisStore store,
        OpeningGameParser? parser = null,
        IOpeningTreeStore? treeStore = null,
        OpeningTreePostProcessor? postProcessor = null,
        bool retainParsedGames = true,
        bool persistImportedGames = true)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.treeStore = treeStore;
        this.parser = parser ?? new OpeningGameParser();
        this.postProcessor = postProcessor ?? new OpeningTreePostProcessor();
        this.retainParsedGames = retainParsedGames;
        this.persistImportedGames = persistImportedGames;
    }

    public OpeningPgnImportResult ImportFile(
        string inputFilePath,
        int maxFullMoves = OpeningGameParser.DefaultMaxFullMoves,
        Action<OpeningPgnImportProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);

        return ImportFiles([inputFilePath], maxFullMoves, progress);
    }

    public OpeningPgnImportResult ImportFolder(
        string inputFolderPath,
        int maxFullMoves = OpeningGameParser.DefaultMaxFullMoves,
        Action<OpeningPgnImportProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFolderPath);

        if (!Directory.Exists(inputFolderPath))
        {
            throw new DirectoryNotFoundException($"Input folder does not exist: {inputFolderPath}");
        }

        string[] files = Directory
            .EnumerateFiles(inputFolderPath, "*.pgn", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ImportFiles(files, maxFullMoves, progress);
    }

    private OpeningPgnImportResult ImportFiles(
        IReadOnlyList<string> files,
        int maxFullMoves,
        Action<OpeningPgnImportProgress>? progress)
    {
        List<OpeningParsedGame> parsedGames = new();
        ConcurrentBag<FileImportResult> fileResults = new();
        int totalGames = 0;
        int skippedGames = 0;
        int totalPlies = 0;
        long totalBytesRead = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        int processorCount = Math.Max(1, Environment.ProcessorCount);
        int fileParallelism = files.Count <= 1
            ? 1
            : Math.Min(files.Count, Math.Min(4, Math.Max(1, processorCount / 2)));
        int chunkParallelism = Math.Max(1, processorCount / fileParallelism);

        Parallel.ForEach(
            files.Select((path, index) => new IndexedFile(path, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = fileParallelism
            },
            indexedFile =>
            {
                FileImportResult result = ProcessFile(
                    indexedFile,
                    files.Count,
                    maxFullMoves,
                    chunkParallelism,
                    progress,
                    stopwatch,
                    ref totalGames,
                    ref skippedGames,
                    ref totalPlies,
                    ref totalBytesRead);
                fileResults.Add(result);
            });

        OpeningTreeBuilder treeBuilder = new();
        foreach (FileImportResult fileResult in fileResults.OrderBy(result => result.FileIndex))
        {
            treeBuilder.MergeFrom(fileResult.TreeBuilder);
        }

        if (retainParsedGames)
        {
            parsedGames = fileResults
                .SelectMany(result => result.ParsedGames)
                .ToList();
        }

        OpeningTreeBuildResult tree = postProcessor.Process(treeBuilder.ToResult());
        treeStore?.SaveOpeningTree(tree);

        if (files.Count > 0)
        {
            string lastFile = files[^1];
            progress?.Invoke(new OpeningPgnImportProgress(
                lastFile,
                files.Count,
                files.Count,
                0,
                totalGames,
                skippedGames,
                totalPlies,
                tree.Nodes.Count,
                tree.Edges.Count,
                Volatile.Read(ref totalBytesRead),
                CalculatePerMinute(totalGames, stopwatch),
                CalculateMegabytesPerMinute(Volatile.Read(ref totalBytesRead), stopwatch),
                DateTime.UtcNow));
        }

        return new OpeningPgnImportResult(files.Count, totalGames, skippedGames, totalPlies, tree, parsedGames);
    }

    private FileImportResult ProcessFile(
        IndexedFile indexedFile,
        int totalFiles,
        int maxFullMoves,
        int chunkParallelism,
        Action<OpeningPgnImportProgress>? progress,
        Stopwatch stopwatch,
        ref int totalGames,
        ref int skippedGames,
        ref int totalPlies,
        ref long totalBytesRead)
    {
        OpeningTreeBuilder treeBuilder = new();
        List<OpeningParsedGame> parsedGames = new();
        int gamesInFile = 0;
        int skippedGamesInFile = 0;

        foreach (List<IndexedGameText> chunk in ReadGameChunks(indexedFile.Path, GamesPerChunk))
        {
            long chunkBytes = chunk.Sum(game => (long)game.PgnText.Length);
            Interlocked.Add(ref totalBytesRead, chunkBytes);
            ChunkProcessingResult chunkResult = ProcessChunk(indexedFile.Path, chunk, maxFullMoves, chunkParallelism);
            skippedGamesInFile += chunkResult.SkippedGames;
            Interlocked.Add(ref skippedGames, chunkResult.SkippedGames);

            if (persistImportedGames && chunkResult.ImportedGames.Count > 0)
            {
                store.SaveImportedGames(chunkResult.ImportedGames);
            }

            foreach (ProcessedOpeningGame game in chunkResult.Games)
            {
                treeBuilder.AddGameWithFingerprint(game.GameFingerprint, game.Plies, game.Metadata);

                if (retainParsedGames && game.ImportedGame is not null)
                {
                    parsedGames.Add(new OpeningParsedGame(game.ImportedGame, game.Plies)
                    {
                        Metadata = game.Metadata
                    });
                }
            }

            gamesInFile += chunkResult.Games.Count;
            Interlocked.Add(ref totalGames, chunkResult.Games.Count);
            Interlocked.Add(ref totalPlies, chunkResult.TotalPlies);

            if (gamesInFile == chunkResult.Games.Count
                || gamesInFile / ProgressReportIntervalGames != (gamesInFile - chunkResult.Games.Count) / ProgressReportIntervalGames)
            {
                ReportProgress(
                    progress,
                    indexedFile.Path,
                    indexedFile.Index,
                    totalFiles,
                    gamesInFile,
                    Volatile.Read(ref totalGames),
                    Volatile.Read(ref skippedGames),
                    Volatile.Read(ref totalPlies),
                    Volatile.Read(ref totalBytesRead),
                    CalculatePerMinute(Volatile.Read(ref totalGames), stopwatch),
                    CalculateMegabytesPerMinute(Volatile.Read(ref totalBytesRead), stopwatch),
                    treeBuilder);
            }
        }

        if (gamesInFile > 0 || skippedGamesInFile > 0)
        {
            ReportProgress(
                progress,
                indexedFile.Path,
                indexedFile.Index,
                totalFiles,
                gamesInFile,
                Volatile.Read(ref totalGames),
                Volatile.Read(ref skippedGames),
                Volatile.Read(ref totalPlies),
                Volatile.Read(ref totalBytesRead),
                CalculatePerMinute(Volatile.Read(ref totalGames), stopwatch),
                CalculateMegabytesPerMinute(Volatile.Read(ref totalBytesRead), stopwatch),
                treeBuilder);
        }

        return new FileImportResult(indexedFile.Index, treeBuilder, parsedGames);
    }

    private static void ReportProgress(
        Action<OpeningPgnImportProgress>? progress,
        string file,
        int fileIndex,
        int totalFiles,
        int gamesInFile,
        int totalGames,
        int skippedGames,
        int totalPlies,
        long totalBytesRead,
        double gamesPerMinute,
        double megabytesPerMinute,
        OpeningTreeBuilder treeBuilder)
    {
        progress?.Invoke(new OpeningPgnImportProgress(
            file,
            fileIndex + 1,
            totalFiles,
            gamesInFile,
            totalGames,
            skippedGames,
            totalPlies,
            treeBuilder.NodeCount,
            treeBuilder.EdgeCount,
            totalBytesRead,
            gamesPerMinute,
            megabytesPerMinute,
            DateTime.UtcNow));
    }

    private static double CalculatePerMinute(int count, Stopwatch stopwatch)
    {
        return stopwatch.Elapsed.TotalMinutes > 0
            ? count / stopwatch.Elapsed.TotalMinutes
            : 0;
    }

    private static double CalculateMegabytesPerMinute(long bytes, Stopwatch stopwatch)
    {
        return stopwatch.Elapsed.TotalMinutes > 0
            ? bytes / 1024d / 1024d / stopwatch.Elapsed.TotalMinutes
            : 0;
    }

    private readonly record struct IndexedFile(string Path, int Index);
    private readonly record struct IndexedGameText(int Ordinal, string PgnText);
    private readonly record struct FileImportResult(int FileIndex, OpeningTreeBuilder TreeBuilder, IReadOnlyList<OpeningParsedGame> ParsedGames);
    private readonly record struct ChunkProcessingResult(
        IReadOnlyList<ProcessedOpeningGame> Games,
        IReadOnlyList<ImportedGame> ImportedGames,
        int SkippedGames,
        int TotalPlies);
    private readonly record struct ProcessedOpeningGame(
        int Ordinal,
        string GameFingerprint,
        OpeningGameMetadata Metadata,
        IReadOnlyList<OpeningImportPly> Plies,
        ImportedGame? ImportedGame);

    private ChunkProcessingResult ProcessChunk(
        string filePath,
        IReadOnlyList<IndexedGameText> chunk,
        int maxFullMoves,
        int maxDegreeOfParallelism)
    {
        ConcurrentBag<ProcessedOpeningGame> processedGames = new();
        ConcurrentBag<ImportedGame> importedGames = new();
        int skippedGames = 0;
        int totalPlies = 0;

        Parallel.ForEach(
            chunk,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism)
            },
            gameText =>
            {
                try
                {
                    ProcessedOpeningGame processedGame = ProcessGame(gameText.Ordinal, gameText.PgnText, maxFullMoves);
                    processedGames.Add(processedGame);
                    if (persistImportedGames && processedGame.ImportedGame is not null)
                    {
                        importedGames.Add(processedGame.ImportedGame);
                    }

                    Interlocked.Add(ref totalPlies, processedGame.Plies.Count);
                }
                catch (InvalidOperationException ex)
                {
                    Interlocked.Increment(ref skippedGames);
                    Console.Error.WriteLine(
                        $"Skipping invalid game #{gameText.Ordinal} in {Path.GetFileName(filePath)}. {DescribeGameForDiagnostics(gameText.PgnText)} {ex.Message}");
                }
            });

        return new ChunkProcessingResult(
            processedGames
                .OrderBy(game => game.Ordinal)
                .ToList(),
            importedGames.ToList(),
            skippedGames,
            totalPlies);
    }

    private ProcessedOpeningGame ProcessGame(int ordinal, string pgn, int maxFullMoves)
    {
        int maxPlies = checked(Math.Max(0, maxFullMoves) * 2);
        List<string> sanMoves = SanNotation.ParsePgnMoves(pgn, maxPlies);
        IReadOnlyList<OpeningImportPly> plies = parser.Parse(sanMoves, maxFullMoves);
        OpeningGameMetadata metadata = OpeningPgnMetadataParser.Parse(pgn);
        string gameFingerprint = GameFingerprint.Compute(pgn);
        ImportedGame? importedGame = null;

        if (persistImportedGames || retainParsedGames)
        {
            importedGame = CreateImportedGame(pgn, sanMoves);
        }

        return new ProcessedOpeningGame(ordinal, gameFingerprint, metadata, plies, importedGame);
    }

    private static ImportedGame CreateImportedGame(string pgn, IReadOnlyList<string> sanMoves)
    {
        return new ImportedGame(
            pgn,
            sanMoves,
            TryGetHeaderValue(pgn, "White"),
            TryGetHeaderValue(pgn, "Black"),
            ParseNullableInt(TryGetHeaderValue(pgn, "WhiteElo")),
            ParseNullableInt(TryGetHeaderValue(pgn, "BlackElo")),
            TryGetHeaderValue(pgn, "Date"),
            TryGetHeaderValue(pgn, "Result"),
            TryGetHeaderValue(pgn, "ECO"),
            TryGetHeaderValue(pgn, "Site"));
    }

    private static IEnumerable<List<IndexedGameText>> ReadGameChunks(string inputFilePath, int chunkSize)
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException("Input PGN file does not exist.", inputFilePath);
        }

        using StreamReader reader = new(inputFilePath);
        List<IndexedGameText> chunk = new(chunkSize);
        int ordinal = 0;
        foreach (string gameText in SplitGames(reader))
        {
            if (!string.IsNullOrWhiteSpace(gameText))
            {
                ordinal++;
                chunk.Add(new IndexedGameText(ordinal, gameText.Trim()));
                if (chunk.Count >= chunkSize)
                {
                    yield return chunk;
                    chunk = new List<IndexedGameText>(chunkSize);
                }
            }
        }

        if (chunk.Count > 0)
        {
            yield return chunk;
        }
    }

    private static IEnumerable<string> SplitGames(TextReader reader)
    {
        StringBuilder currentGame = new();
        bool sawMoveText = false;

        while (reader.ReadLine() is { } line)
        {
            string trimmedStart = line.TrimStart();
            bool isHeaderLine = IsHeaderLine(trimmedStart);
            bool startsNewGame = isHeaderLine
                && currentGame.Length > 0
                && (trimmedStart.StartsWith("[Event ", StringComparison.Ordinal) || sawMoveText);

            if (startsNewGame)
            {
                yield return currentGame.ToString().Trim();
                currentGame.Clear();
                sawMoveText = false;
            }

            currentGame.AppendLine(line);

            if (!string.IsNullOrWhiteSpace(trimmedStart) && !isHeaderLine)
            {
                sawMoveText = true;
            }
        }

        if (currentGame.Length > 0)
        {
            yield return currentGame.ToString().Trim();
        }
    }

    private static bool IsHeaderLine(string trimmedLine)
    {
        return trimmedLine.StartsWith("[", StringComparison.Ordinal)
            && trimmedLine.EndsWith("]", StringComparison.Ordinal);
    }

    private static string DescribeGameForDiagnostics(string pgn)
    {
        List<string> parts = new();
        AddHeaderIfPresent(parts, "Event", pgn);
        AddHeaderIfPresent(parts, "White", pgn);
        AddHeaderIfPresent(parts, "Black", pgn);
        AddHeaderIfPresent(parts, "ECO", pgn);

        return parts.Count == 0
            ? string.Empty
            : $"[{string.Join(", ", parts)}]";
    }

    private static void AddHeaderIfPresent(List<string> parts, string headerName, string pgn)
    {
        string? value = TryGetHeaderValue(pgn, headerName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{headerName}=\"{value}\"");
        }
    }

    private static string? TryGetHeaderValue(string pgn, string headerName)
    {
        using StringReader reader = new(pgn);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    break;
                }

                continue;
            }

            string prefix = $"[{headerName} \"";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith("\"]", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed[prefix.Length..^2];
        }

        return null;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, out int parsed) ? parsed : null;
    }
}
