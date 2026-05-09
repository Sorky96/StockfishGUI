namespace MoveMentorChess.Domain;

public sealed record OpeningPgnImportResult(
    int FilesProcessed,
    int GamesProcessed,
    int SkippedGames,
    int PliesParsed,
    OpeningTreeBuildResult Tree,
    IReadOnlyList<OpeningParsedGame> ParsedGames);
