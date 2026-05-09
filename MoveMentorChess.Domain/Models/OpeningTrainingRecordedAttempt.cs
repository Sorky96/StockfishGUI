namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingRecordedAttempt(
    string PositionId,
    OpeningTrainingMode Mode,
    OpeningTrainingSourceKind PositionSource,
    string Eco,
    string OpeningName,
    string? ThemeLabel,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    OpeningTrainingScore Score,
    DateTime RecordedUtc);
