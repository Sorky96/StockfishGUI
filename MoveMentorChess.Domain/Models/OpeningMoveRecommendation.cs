namespace MoveMentorChess.Domain;

public sealed record OpeningMoveRecommendation(
    string GameFingerprint,
    PlayerSide Side,
    string Eco,
    int Ply,
    int MoveNumber,
    string PlayedSan,
    string BetterMove,
    string? MistakeType,
    int? CentipawnLoss,
    string FenBefore);
