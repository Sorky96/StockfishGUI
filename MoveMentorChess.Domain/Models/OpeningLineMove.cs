namespace MoveMentorChess.Domain;

public sealed record OpeningLineMove(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string? Uci,
    OpeningPositionKey FromPositionKey,
    OpeningPositionKey ToPositionKey,
    bool IsMainMove,
    OpeningMoveIdea? Idea = null);
