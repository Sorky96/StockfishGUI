namespace MoveMentorChess.Opening;

public sealed class CanonicalLineResolver
{
    private readonly OpeningTheoryQueryService theoryQueryService;

    public CanonicalLineResolver(OpeningTheoryQueryService theoryQueryService)
    {
        this.theoryQueryService = theoryQueryService ?? throw new ArgumentNullException(nameof(theoryQueryService));
    }

    public CanonicalLineResolutionResult Resolve(string fen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        OpeningPositionKey positionKey = OpeningPositionKeyBuilder.BuildKey(fen);
        bool found = theoryQueryService.TryGetPositionByKey(positionKey.Value, out OpeningTheoryPosition? position);
        string canonicalFen = found && position is not null
            ? position.Fen
            : OpeningPositionKeyBuilder.NormalizeFen(fen);

        OpeningPositionIdentity identity = new(
            positionKey,
            fen,
            canonicalFen,
            position?.Ply ?? 0,
            position?.MoveNumber ?? 1,
            ParseSide(position?.SideToMove),
            !string.Equals(canonicalFen, fen, StringComparison.Ordinal));

        OpeningLineKey? canonicalLineKey = null;
        if (position is not null)
        {
            IReadOnlyList<OpeningLineCatalogItem> lines = theoryQueryService.ListOpeningLines(limit: 500);
            OpeningLineCatalogItem? matching = lines.FirstOrDefault(item => item.RootPositionKey.Equals(position.OpeningPositionKey))
                ?? lines.FirstOrDefault(item => string.Equals(item.Eco, position.Metadata.Eco, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.OpeningName, position.Metadata.OpeningName, StringComparison.OrdinalIgnoreCase));
            if (matching is not null && !matching.LineKey.IsEmpty)
            {
                canonicalLineKey = matching.LineKey;
            }
        }

        return new CanonicalLineResolutionResult(
            identity,
            canonicalLineKey,
            found && position is not null,
            identity.ReachedByTransposition,
            found && position is not null
                ? "Resolved to a known theory position."
                : "Position is not present in the local opening theory.");
    }

    private static PlayerSide ParseSide(string? value)
    {
        return string.Equals(value, "Black", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "b", StringComparison.OrdinalIgnoreCase)
            ? PlayerSide.Black
            : PlayerSide.White;
    }
}
