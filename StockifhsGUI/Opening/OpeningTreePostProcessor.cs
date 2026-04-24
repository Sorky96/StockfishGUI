namespace StockifhsGUI;

public sealed class OpeningTreePostProcessor
{
    private const int MaxPlayableMovesPerPosition = 3;
    private const double PlayableShareThreshold = 0.10;

    public OpeningTreeBuildResult Process(OpeningTreeBuildResult tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        foreach (IGrouping<Guid, OpeningMoveEdge> group in tree.Edges.GroupBy(edge => edge.FromNodeId))
        {
            List<OpeningMoveEdge> rankedEdges = group
                .OrderByDescending(edge => edge.OccurrenceCount)
                .ThenBy(edge => edge.MoveSan, StringComparer.Ordinal)
                .ThenBy(edge => edge.MoveUci, StringComparer.Ordinal)
                .ToList();
            int totalOccurrences = rankedEdges.Sum(edge => edge.OccurrenceCount);

            for (int i = 0; i < rankedEdges.Count; i++)
            {
                OpeningMoveEdge edge = rankedEdges[i];
                int rank = i + 1;
                double share = totalOccurrences > 0 ? (double)edge.OccurrenceCount / totalOccurrences : 0;

                edge.RankWithinPosition = rank;
                edge.IsMainMove = rank == 1;
                edge.IsPlayableMove = edge.OccurrenceCount > 1
                    && (rank <= MaxPlayableMovesPerPosition || share >= PlayableShareThreshold);
            }
        }

        return tree;
    }
}
