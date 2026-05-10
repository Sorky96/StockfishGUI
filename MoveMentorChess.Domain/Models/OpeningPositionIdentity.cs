namespace MoveMentorChess.Domain;

public sealed record OpeningPositionIdentity(
    OpeningPositionKey PositionKey,
    string Fen,
    string CanonicalFen,
    int Ply,
    int MoveNumber,
    PlayerSide SideToMove,
    bool ReachedByTransposition);
