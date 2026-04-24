namespace StockifhsGUI;

public sealed record OpeningImportPly(
    int Ply,
    int MoveNumber,
    string Side,
    string FenBefore,
    string FenAfter,
    string PositionKeyBefore,
    string PositionKeyAfter,
    string MoveSan,
    string MoveUci);

public sealed record OpeningParsedGame(
    ImportedGame Game,
    IReadOnlyList<OpeningImportPly> Plies)
{
    public OpeningGameMetadata Metadata { get; init; } = OpeningGameMetadata.Empty;
}

public sealed record OpeningGameMetadata(
    string Eco,
    string OpeningName,
    string VariationName)
{
    public static OpeningGameMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty);
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Eco)
        || !string.IsNullOrWhiteSpace(OpeningName)
        || !string.IsNullOrWhiteSpace(VariationName);
}

public sealed record OpeningPgnImportProgress(
    string CurrentFilePath,
    int CurrentFileIndex,
    int TotalFiles,
    int GamesProcessedInCurrentFile,
    int TotalGamesProcessed,
    int SkippedGames,
    int TotalPliesParsed,
    int NodeCount,
    int EdgeCount,
    long TotalBytesRead,
    double GamesPerMinute,
    double MegabytesPerMinute,
    DateTime LastUpdatedUtc);

public sealed record OpeningPgnImportResult(
    int FilesProcessed,
    int GamesProcessed,
    int SkippedGames,
    int PliesParsed,
    OpeningTreeBuildResult Tree,
    IReadOnlyList<OpeningParsedGame> ParsedGames);

public sealed class OpeningPositionNode
{
    public Guid Id { get; set; }
    public string PositionKey { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public string SideToMove { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public int DistinctGameCount { get; set; }
}

public sealed class OpeningMoveEdge
{
    public Guid Id { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string MoveUci { get; set; } = string.Empty;
    public string MoveSan { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public int DistinctGameCount { get; set; }
    public bool IsMainMove { get; set; }
    public bool IsPlayableMove { get; set; }
    public int RankWithinPosition { get; set; }
}

public sealed class OpeningNodeTag
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string Eco { get; set; } = string.Empty;
    public string OpeningName { get; set; } = string.Empty;
    public string VariationName { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
}

public sealed record OpeningTreeBuildResult(
    IReadOnlyList<OpeningPositionNode> Nodes,
    IReadOnlyList<OpeningMoveEdge> Edges,
    IReadOnlyList<OpeningNodeTag> Tags);

public sealed record OpeningTreeStoreSummary(
    int NodeCount,
    int EdgeCount,
    int TagCount);

public sealed record OpeningTheoryPosition(
    Guid Id,
    string PositionKey,
    string Fen,
    int Ply,
    int MoveNumber,
    string SideToMove,
    int OccurrenceCount,
    int DistinctGameCount,
    OpeningGameMetadata Metadata);

public sealed record OpeningTheoryMove(
    Guid EdgeId,
    Guid FromNodeId,
    Guid ToNodeId,
    string MoveUci,
    string MoveSan,
    int OccurrenceCount,
    int DistinctGameCount,
    bool IsMainMove,
    bool IsPlayableMove,
    int RankWithinPosition,
    string ToPositionKey,
    string ToFen,
    OpeningGameMetadata ToPositionMetadata);
