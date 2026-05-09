namespace MoveMentorChess.Domain;

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
