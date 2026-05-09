namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingLine(
    string LineId,
    OpeningTrainingSourceKind SourceKind,
    string Eco,
    string OpeningName,
    string StartFen,
    int AnchorPly,
    int AnchorMoveNumber,
    PlayerSide SideToMove,
    string AnchorLabel,
    IReadOnlyList<OpeningTrainingMove> Moves,
    OpeningTrainingReference Reference);
