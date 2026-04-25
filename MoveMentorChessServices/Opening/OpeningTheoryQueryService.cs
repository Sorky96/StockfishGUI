namespace MoveMentorChessServices;

public sealed class OpeningTheoryQueryService
{
    private readonly IOpeningTheoryStore store;

    public OpeningTheoryQueryService(IOpeningTheoryStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool TryGetPositionByFen(string fen, out OpeningTheoryPosition? position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        return store.TryGetOpeningPositionByKey(OpeningPositionKeyBuilder.Build(fen), out position);
    }

    public bool TryGetPositionByKey(string positionKey, out OpeningTheoryPosition? position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        return store.TryGetOpeningPositionByKey(positionKey, out position);
    }

    public IReadOnlyList<OpeningTheoryMove> GetTopMovesForFen(string fen, int limit = 10, bool playableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        return GetTopMovesForPositionKey(OpeningPositionKeyBuilder.Build(fen), limit, playableOnly);
    }

    public IReadOnlyList<OpeningTheoryMove> GetTopMovesForPositionKey(
        string positionKey,
        int limit = 10,
        bool playableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        return store.GetOpeningMovesByPositionKey(positionKey, limit, playableOnly);
    }

    public OpeningTheoryMove? GetMainMoveForFen(string fen)
    {
        return GetTopMovesForFen(fen, limit: 1, playableOnly: false)
            .FirstOrDefault(move => move.IsMainMove);
    }

    public IReadOnlyList<OpeningTheoryMove> GetPlayableMovesForFen(string fen, int limit = 10)
    {
        return GetTopMovesForFen(fen, limit, playableOnly: true);
    }
}
