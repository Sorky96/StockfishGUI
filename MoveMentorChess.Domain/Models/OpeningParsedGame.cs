namespace MoveMentorChess.Domain;

public sealed record OpeningParsedGame(
    ImportedGame Game,
    IReadOnlyList<OpeningImportPly> Plies)
{
    public OpeningGameMetadata Metadata { get; init; } = OpeningGameMetadata.Empty;
}
