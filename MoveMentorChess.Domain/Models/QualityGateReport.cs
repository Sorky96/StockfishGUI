namespace MoveMentorChess.Domain;

public sealed record QualityGateReport(
    DateTime TimestampUtc,
    IReadOnlyList<QualityGateFinding> Findings,
    int CorrectedCount,
    int FallbackCount);
