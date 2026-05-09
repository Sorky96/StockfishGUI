namespace MoveMentorChess.Domain;

public sealed record GameAnalysisCacheKey(
    string GameFingerprint,
    PlayerSide Side,
    int Depth,
    int MultiPv,
    int? MoveTimeMs);
