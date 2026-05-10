namespace MoveMentorChess.Domain;

public sealed record OpeningTrainerOverview(
    OpeningKey OpeningKey,
    OpeningLineKey LineKey,
    RepertoireSide RepertoireSide,
    string Eco,
    string OpeningName,
    string VariationName,
    IReadOnlyList<OpeningLineMove> MainLine,
    IReadOnlyList<OpeningTrainingBranch> CommonBranches,
    OpponentReplyProfile OpponentReplyProfile,
    OpeningCoverageSummary Coverage,
    IReadOnlyList<OpeningTrainingPosition> WeakPositions,
    IReadOnlyList<OpeningMoveIdea> WhyTheseMovesMatter);
