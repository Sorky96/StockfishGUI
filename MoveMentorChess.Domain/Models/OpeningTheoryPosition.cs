namespace MoveMentorChess.Domain;

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
