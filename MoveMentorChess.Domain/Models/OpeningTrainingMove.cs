namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingMove(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string? Uci,
    OpeningTrainingMoveRole Role,
    bool IsPreferred = false,
    string? Note = null);
