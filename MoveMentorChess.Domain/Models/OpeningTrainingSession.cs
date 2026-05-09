namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSession(
    string SessionId,
    string PlayerKey,
    string DisplayName,
    DateTime CreatedUtc,
    IReadOnlyList<OpeningTrainingMode> SupportedModes,
    IReadOnlyList<OpeningTrainingSourceKind> IncludedSources,
    IReadOnlyList<OpeningTrainingSourceSummary> SourceSummaries,
    IReadOnlyList<OpeningTrainingLine> Lines,
    IReadOnlyList<OpeningTrainingPosition> Positions);
