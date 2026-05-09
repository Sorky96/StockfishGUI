namespace MoveMentorChess.Domain;

public sealed record QualityGateFinding(
    string Code,
    QualityGateSeverity Severity,
    string Message,
    string GameFingerprint,
    int Ply,
    string? Label,
    MoveQualityBucket? Quality,
    string? CorrectiveAction);
