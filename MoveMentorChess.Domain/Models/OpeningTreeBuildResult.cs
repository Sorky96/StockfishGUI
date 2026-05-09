namespace MoveMentorChess.Domain;

public sealed record OpeningTreeBuildResult(
    IReadOnlyList<OpeningPositionNode> Nodes,
    IReadOnlyList<OpeningMoveEdge> Edges,
    IReadOnlyList<OpeningNodeTag> Tags);
