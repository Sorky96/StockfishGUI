namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSession(
    string SessionId,
    string PlayerKey,
    string DisplayName,
    DateTime CreatedUtc,
    OpeningTrainingStyle TrainingStyle,
    OpeningTrainingStrictness Strictness,
    RepertoireSide RepertoireSide,
    IReadOnlyList<OpeningTrainingMode> SupportedModes,
    IReadOnlyList<OpeningTrainingSourceKind> IncludedSources,
    IReadOnlyList<OpeningTrainingSourceSummary> SourceSummaries,
    IReadOnlyList<OpeningTrainingLine> Lines,
    IReadOnlyList<OpeningTrainingPosition> Positions)
{
    public OpeningTrainingSession(
        string sessionId,
        string playerKey,
        string displayName,
        DateTime createdUtc,
        IReadOnlyList<OpeningTrainingMode> supportedModes,
        IReadOnlyList<OpeningTrainingSourceKind> includedSources,
        IReadOnlyList<OpeningTrainingSourceSummary> sourceSummaries,
        IReadOnlyList<OpeningTrainingLine> lines,
        IReadOnlyList<OpeningTrainingPosition> positions)
        : this(
            sessionId,
            playerKey,
            displayName,
            createdUtc,
            OpeningTrainingStyle.Mixed,
            OpeningTrainingStrictness.BookFlexible,
            RepertoireSide.Both,
            supportedModes,
            includedSources,
            sourceSummaries,
            lines,
            positions)
    {
    }
}
