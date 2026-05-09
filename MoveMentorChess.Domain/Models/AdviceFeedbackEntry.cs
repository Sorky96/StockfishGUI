namespace MoveMentorChess.Domain;

public sealed record AdviceFeedbackEntry(
    DateTime TimestampUtc,
    string GameFingerprint,
    int Ply,
    AdviceFeedbackKind FeedbackKind,
    string Label,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    bool UsedFallback,
    string PlayedSan = "",
    string PlayedUci = "",
    string? BestMoveUci = null,
    string? GeneratorName = null);
