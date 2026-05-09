namespace MoveMentorChess.Domain;

public sealed record ProfileMistakeExample(
    string GameFingerprint,
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string PlayedSan,
    string BetterMove,
    string Label,
    int? CentipawnLoss,
    MoveQualityBucket Quality,
    GamePhase Phase,
    string Eco,
    string FenBefore,
    ProfileMistakeExampleRank Rank);
