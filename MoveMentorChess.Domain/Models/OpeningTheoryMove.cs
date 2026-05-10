namespace MoveMentorChess.Domain;

public sealed record OpeningTheoryMove(
    Guid EdgeId,
    Guid FromNodeId,
    Guid ToNodeId,
    string MoveUci,
    string MoveSan,
    int OccurrenceCount,
    int DistinctGameCount,
    bool IsMainMove,
    bool IsPlayableMove,
    int RankWithinPosition,
    string ToPositionKey,
    OpeningPositionKey ToOpeningPositionKey,
    string ToFen,
    OpeningGameMetadata ToPositionMetadata,
    string SourceKind = "opening_book",
    OpeningMoveIdea? Idea = null)
{
    public OpeningTheoryMove(
        Guid edgeId,
        Guid fromNodeId,
        Guid toNodeId,
        string moveUci,
        string moveSan,
        int occurrenceCount,
        int distinctGameCount,
        bool isMainMove,
        bool isPlayableMove,
        int rankWithinPosition,
        string toPositionKey,
        string toFen,
        OpeningGameMetadata toPositionMetadata,
        string sourceKind = "opening_book")
        : this(
            edgeId,
            fromNodeId,
            toNodeId,
            moveUci,
            moveSan,
            occurrenceCount,
            distinctGameCount,
            isMainMove,
            isPlayableMove,
            rankWithinPosition,
            toPositionKey,
            new OpeningPositionKey(toPositionKey),
            toFen,
            toPositionMetadata,
            sourceKind,
            null)
    {
    }
}
