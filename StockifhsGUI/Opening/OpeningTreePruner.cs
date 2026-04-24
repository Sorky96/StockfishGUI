namespace StockifhsGUI;

public sealed class OpeningTreePruner
{
    public OpeningTreeBuildResult Prune(
        OpeningTreeBuildResult tree,
        OpeningTreePruningOptions options)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(options);

        if (tree.Nodes.Count == 0 || tree.Edges.Count == 0)
        {
            return tree;
        }

        Dictionary<Guid, OpeningPositionNode> nodesById = tree.Nodes.ToDictionary(node => node.Id);
        OpeningPositionNode? root = tree.Nodes
            .OrderBy(node => node.Ply)
            .ThenByDescending(node => node.DistinctGameCount)
            .FirstOrDefault();
        if (root is null)
        {
            return tree;
        }

        Dictionary<Guid, List<OpeningMoveEdge>> keptEdgesByFromNode = new();
        foreach (IGrouping<Guid, OpeningMoveEdge> group in tree.Edges.GroupBy(edge => edge.FromNodeId))
        {
            if (!nodesById.TryGetValue(group.Key, out OpeningPositionNode? fromNode))
            {
                continue;
            }

            List<OpeningMoveEdge> rankedEdges = group
                .OrderBy(edge => edge.RankWithinPosition == 0 ? int.MaxValue : edge.RankWithinPosition)
                .ThenByDescending(edge => edge.DistinctGameCount)
                .ThenBy(edge => edge.MoveSan, StringComparer.Ordinal)
                .ThenBy(edge => edge.MoveUci, StringComparer.Ordinal)
                .ToList();

            List<OpeningMoveEdge> keptEdges = rankedEdges
                .Where((edge, index) => ShouldKeepEdge(edge, fromNode, index + 1, options))
                .Take(options.MaxMovesPerPosition)
                .ToList();

            if (keptEdges.Count > 0)
            {
                keptEdgesByFromNode[group.Key] = keptEdges;
            }
        }

        HashSet<Guid> reachableNodeIds = new() { root.Id };
        HashSet<Guid> reachableEdgeIds = new();
        Queue<Guid> queue = new();
        queue.Enqueue(root.Id);

        while (queue.Count > 0)
        {
            Guid nodeId = queue.Dequeue();
            if (!keptEdgesByFromNode.TryGetValue(nodeId, out List<OpeningMoveEdge>? outgoingEdges))
            {
                continue;
            }

            foreach (OpeningMoveEdge edge in outgoingEdges)
            {
                if (!nodesById.ContainsKey(edge.ToNodeId))
                {
                    continue;
                }

                reachableEdgeIds.Add(edge.Id);
                if (reachableNodeIds.Add(edge.ToNodeId))
                {
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        List<OpeningPositionNode> nodes = tree.Nodes
            .Where(node => reachableNodeIds.Contains(node.Id))
            .OrderBy(node => node.Ply)
            .ThenBy(node => node.PositionKey, StringComparer.Ordinal)
            .ToList();
        List<OpeningMoveEdge> edges = tree.Edges
            .Where(edge => reachableEdgeIds.Contains(edge.Id))
            .OrderBy(edge => edge.FromNodeId)
            .ThenBy(edge => edge.RankWithinPosition == 0 ? int.MaxValue : edge.RankWithinPosition)
            .ThenBy(edge => edge.MoveUci, StringComparer.Ordinal)
            .ToList();
        List<OpeningNodeTag> tags = tree.Tags
            .Where(tag => reachableNodeIds.Contains(tag.NodeId))
            .OrderBy(tag => tag.NodeId)
            .ThenBy(tag => tag.Eco, StringComparer.Ordinal)
            .ThenBy(tag => tag.OpeningName, StringComparer.Ordinal)
            .ThenBy(tag => tag.VariationName, StringComparer.Ordinal)
            .ToList();

        return new OpeningTreeBuildResult(nodes, edges, tags);
    }

    private static bool ShouldKeepEdge(
        OpeningMoveEdge edge,
        OpeningPositionNode fromNode,
        int rank,
        OpeningTreePruningOptions options)
    {
        if (edge.DistinctGameCount >= options.MinDistinctGames
            && rank <= options.MaxMovesPerPosition)
        {
            return true;
        }

        double share = fromNode.DistinctGameCount > 0
            ? (double)edge.DistinctGameCount / fromNode.DistinctGameCount
            : 0;
        if (edge.DistinctGameCount >= options.MinDistinctGames
            && share >= options.MinMoveShare)
        {
            return true;
        }

        return options.AlwaysKeepMainMove
            && rank == 1
            && fromNode.DistinctGameCount >= options.MinDistinctGames
            && edge.DistinctGameCount > 0;
    }
}

public sealed record OpeningTreePruningOptions(
    int MinDistinctGames,
    int MaxMovesPerPosition,
    double MinMoveShare,
    bool AlwaysKeepMainMove)
{
    public static OpeningTreePruningOptions ProductionDefault { get; } = new(
        MinDistinctGames: 30,
        MaxMovesPerPosition: 5,
        MinMoveShare: 0.05,
        AlwaysKeepMainMove: true);
}
