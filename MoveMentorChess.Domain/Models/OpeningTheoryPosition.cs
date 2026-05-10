namespace MoveMentorChess.Domain;

public sealed record OpeningTheoryPosition(
    Guid Id,
    string PositionKey,
    OpeningPositionKey OpeningPositionKey,
    string Fen,
    int Ply,
    int MoveNumber,
    string SideToMove,
    int OccurrenceCount,
    int DistinctGameCount,
    OpeningGameMetadata Metadata)
{
    public OpeningTheoryPosition(
        Guid id,
        string positionKey,
        string fen,
        int ply,
        int moveNumber,
        string sideToMove,
        int occurrenceCount,
        int distinctGameCount,
        OpeningGameMetadata metadata)
        : this(
            id,
            positionKey,
            new OpeningPositionKey(positionKey),
            fen,
            ply,
            moveNumber,
            sideToMove,
            occurrenceCount,
            distinctGameCount,
            metadata)
    {
    }
}
