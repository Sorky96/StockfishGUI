namespace MoveMentorChess.Domain;

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
    OpeningGameMetadata ToPositionMetadata,
    string SourceKind = "opening_book");
