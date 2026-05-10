namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingLine(
    string LineId,
    OpeningLineKey OpeningLineKey,
    OpeningKey OpeningKey,
    OpeningTrainingSourceKind SourceKind,
    string Eco,
    string OpeningName,
    string StartFen,
    OpeningPositionKey StartPositionKey,
    int AnchorPly,
    int AnchorMoveNumber,
    PlayerSide SideToMove,
    string AnchorLabel,
    IReadOnlyList<OpeningTrainingMove> Moves,
    OpeningTrainingReference Reference,
    RepertoireSide RepertoireSide = RepertoireSide.Both)
{
    public OpeningTrainingLine(
        string lineId,
        OpeningTrainingSourceKind sourceKind,
        string eco,
        string openingName,
        string startFen,
        int anchorPly,
        int anchorMoveNumber,
        PlayerSide sideToMove,
        string anchorLabel,
        IReadOnlyList<OpeningTrainingMove> moves,
        OpeningTrainingReference reference)
        : this(
            lineId,
            new OpeningLineKey(lineId),
            new OpeningKey($"{eco}:{openingName}"),
            sourceKind,
            eco,
            openingName,
            startFen,
            new OpeningPositionKey(startFen),
            anchorPly,
            anchorMoveNumber,
            sideToMove,
            anchorLabel,
            moves,
            reference,
            RepertoireSide.Both)
    {
    }
}
