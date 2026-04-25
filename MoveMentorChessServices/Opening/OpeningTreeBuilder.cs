namespace MoveMentorChessServices;

public sealed class OpeningTreeBuilder
{
    private readonly Dictionary<string, OpeningPositionNode> nodesByPositionKey = new(StringComparer.Ordinal);
    private readonly Dictionary<OpeningEdgeKey, OpeningMoveEdge> edgesByKey = new();
    private readonly Dictionary<Guid, HashSet<string>> nodeGameFingerprints = new();
    private readonly Dictionary<Guid, HashSet<string>> edgeGameFingerprints = new();
    private readonly Dictionary<Guid, Dictionary<OpeningNodeTagKey, int>> tagCountsByNodeId = new();

    public int NodeCount => nodesByPositionKey.Count;
    public int EdgeCount => edgesByKey.Count;

    public OpeningTreeBuildResult Build(IEnumerable<OpeningParsedGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        Clear();
        foreach (OpeningParsedGame game in games)
        {
            AddGame(game);
        }

        return ToResult();
    }

    public void Clear()
    {
        nodesByPositionKey.Clear();
        edgesByKey.Clear();
        nodeGameFingerprints.Clear();
        edgeGameFingerprints.Clear();
        tagCountsByNodeId.Clear();
    }

    public void AddGame(OpeningParsedGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        AddGame(game.Game.PgnText, game.Plies, game.Metadata);
    }

    public void AddGame(
        string pgnText,
        IReadOnlyList<OpeningImportPly> plies,
        OpeningGameMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(pgnText);
        ArgumentNullException.ThrowIfNull(plies);

        string gameFingerprint = GameFingerprint.Compute(pgnText);
        AddParsedGame(gameFingerprint, plies, metadata);
    }

    public void AddGameWithFingerprint(
        string gameFingerprint,
        IReadOnlyList<OpeningImportPly> plies,
        OpeningGameMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);
        ArgumentNullException.ThrowIfNull(plies);

        AddParsedGame(gameFingerprint, plies, metadata);
    }

    public void MergeFrom(OpeningTreeBuilder other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Dictionary<Guid, Guid> nodeIdMap = new();
        foreach ((string positionKey, OpeningPositionNode otherNode) in other.nodesByPositionKey)
        {
            OpeningPositionNode localNode = EnsureNode(positionKey, otherNode.Fen, otherNode.Ply);
            localNode.OccurrenceCount += otherNode.OccurrenceCount;

            HashSet<string> localFingerprints = nodeGameFingerprints[localNode.Id];
            if (other.nodeGameFingerprints.TryGetValue(otherNode.Id, out HashSet<string>? otherFingerprints))
            {
                foreach (string fingerprint in otherFingerprints)
                {
                    localFingerprints.Add(fingerprint);
                }
            }

            localNode.DistinctGameCount = localFingerprints.Count;
            nodeIdMap[otherNode.Id] = localNode.Id;
        }

        foreach ((OpeningEdgeKey otherKey, OpeningMoveEdge otherEdge) in other.edgesByKey)
        {
            OpeningEdgeKey localKey = new(
                nodeIdMap[otherKey.FromNodeId],
                otherKey.MoveUci,
                nodeIdMap[otherKey.ToNodeId]);

            if (!edgesByKey.TryGetValue(localKey, out OpeningMoveEdge? localEdge))
            {
                localEdge = new OpeningMoveEdge
                {
                    Id = Guid.NewGuid(),
                    FromNodeId = localKey.FromNodeId,
                    ToNodeId = localKey.ToNodeId,
                    MoveUci = otherEdge.MoveUci,
                    MoveSan = otherEdge.MoveSan
                };
                edgesByKey[localKey] = localEdge;
                edgeGameFingerprints[localEdge.Id] = new HashSet<string>(StringComparer.Ordinal);
            }

            localEdge.OccurrenceCount += otherEdge.OccurrenceCount;
            HashSet<string> localEdgeFingerprints = edgeGameFingerprints[localEdge.Id];
            if (other.edgeGameFingerprints.TryGetValue(otherEdge.Id, out HashSet<string>? otherEdgeFingerprints))
            {
                foreach (string fingerprint in otherEdgeFingerprints)
                {
                    localEdgeFingerprints.Add(fingerprint);
                }
            }

            localEdge.DistinctGameCount = localEdgeFingerprints.Count;
        }

        foreach ((Guid otherNodeId, Dictionary<OpeningNodeTagKey, int> otherCounts) in other.tagCountsByNodeId)
        {
            Guid localNodeId = nodeIdMap[otherNodeId];
            if (!tagCountsByNodeId.TryGetValue(localNodeId, out Dictionary<OpeningNodeTagKey, int>? localCounts))
            {
                localCounts = new Dictionary<OpeningNodeTagKey, int>();
                tagCountsByNodeId[localNodeId] = localCounts;
            }

            foreach ((OpeningNodeTagKey key, int count) in otherCounts)
            {
                localCounts.TryGetValue(key, out int currentCount);
                localCounts[key] = currentCount + count;
            }
        }
    }

    public OpeningTreeBuildResult ToResult()
    {
        return new OpeningTreeBuildResult(
            nodesByPositionKey.Values
                .OrderBy(node => node.Ply)
                .ThenBy(node => node.PositionKey, StringComparer.Ordinal)
                .ToList(),
            edgesByKey.Values
                .OrderBy(edge => edge.FromNodeId)
                .ThenBy(edge => edge.MoveUci, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToNodeId)
                .ToList(),
            BuildTags());
    }

    private OpeningPositionNode EnsureNode(string positionKey, string fen, int ply)
    {
        if (nodesByPositionKey.TryGetValue(positionKey, out OpeningPositionNode? existingNode))
        {
            return existingNode;
        }

        FenMetadata metadata = ReadFenMetadata(fen);
        OpeningPositionNode node = new()
        {
            Id = Guid.NewGuid(),
            PositionKey = positionKey,
            Fen = fen,
            Ply = ply,
            MoveNumber = metadata.MoveNumber,
            SideToMove = metadata.SideToMove
        };

        nodesByPositionKey[positionKey] = node;
        nodeGameFingerprints[node.Id] = new HashSet<string>(StringComparer.Ordinal);
        return node;
    }

    private void RegisterNodeVisit(OpeningPositionNode node, string gameFingerprint, HashSet<string> distinctPositionKeysInGame)
    {
        node.OccurrenceCount++;
        if (distinctPositionKeysInGame.Add(node.PositionKey) && nodeGameFingerprints[node.Id].Add(gameFingerprint))
        {
            node.DistinctGameCount++;
        }
    }

    private void RegisterEdgeVisit(
        OpeningPositionNode fromNode,
        OpeningPositionNode toNode,
        OpeningImportPly ply,
        string gameFingerprint)
    {
        OpeningEdgeKey key = new(fromNode.Id, ply.MoveUci, toNode.Id);
        if (!edgesByKey.TryGetValue(key, out OpeningMoveEdge? edge))
        {
            edge = new OpeningMoveEdge
            {
                Id = Guid.NewGuid(),
                FromNodeId = fromNode.Id,
                ToNodeId = toNode.Id,
                MoveUci = ply.MoveUci,
                MoveSan = ply.MoveSan
            };
            edgesByKey[key] = edge;
        edgeGameFingerprints[edge.Id] = new HashSet<string>(StringComparer.Ordinal);
        }

        edge.OccurrenceCount++;
        if (edgeGameFingerprints[edge.Id].Add(gameFingerprint))
        {
            edge.DistinctGameCount++;
        }
    }

    private void RegisterNodeTag(OpeningPositionNode node, OpeningGameMetadata metadata)
    {
        if (!metadata.HasAnyValue)
        {
            return;
        }

        OpeningNodeTagKey key = new(metadata.Eco, metadata.OpeningName, metadata.VariationName);
        if (!tagCountsByNodeId.TryGetValue(node.Id, out Dictionary<OpeningNodeTagKey, int>? counts))
        {
            counts = new Dictionary<OpeningNodeTagKey, int>();
            tagCountsByNodeId[node.Id] = counts;
        }

        counts.TryGetValue(key, out int currentCount);
        counts[key] = currentCount + 1;
    }

    private IReadOnlyList<OpeningNodeTag> BuildTags()
    {
        List<OpeningNodeTag> tags = new();
        foreach ((Guid nodeId, Dictionary<OpeningNodeTagKey, int> counts) in tagCountsByNodeId)
        {
            OpeningNodeTagKey winner = counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key.Eco, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.OpeningName, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.VariationName, StringComparer.Ordinal)
                .Select(pair => pair.Key)
                .First();

            tags.Add(new OpeningNodeTag
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Eco = winner.Eco,
                OpeningName = winner.OpeningName,
                VariationName = winner.VariationName,
                SourceKind = "pgn"
            });
        }

        return tags
            .OrderBy(tag => tag.NodeId)
            .ThenBy(tag => tag.Eco, StringComparer.Ordinal)
            .ThenBy(tag => tag.OpeningName, StringComparer.Ordinal)
            .ThenBy(tag => tag.VariationName, StringComparer.Ordinal)
            .ToList();
    }

    private static FenMetadata ReadFenMetadata(string fen)
    {
        string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string sideToMove = parts.Length > 1 && parts[1] == "b" ? "Black" : "White";
        int moveNumber = parts.Length > 5 && int.TryParse(parts[5], out int parsedMoveNumber)
            ? parsedMoveNumber
            : 1;

        return new FenMetadata(sideToMove, moveNumber);
    }

    private readonly record struct OpeningEdgeKey(Guid FromNodeId, string MoveUci, Guid ToNodeId);
    private readonly record struct OpeningNodeTagKey(string Eco, string OpeningName, string VariationName);
    private readonly record struct FenMetadata(string SideToMove, int MoveNumber);

    private void AddParsedGame(
        string gameFingerprint,
        IReadOnlyList<OpeningImportPly> plies,
        OpeningGameMetadata metadata)
    {
        HashSet<string> distinctPositionKeysInGame = new(StringComparer.Ordinal);

        if (plies.Count == 0)
        {
            return;
        }

        OpeningImportPly firstPly = plies[0];
        OpeningPositionNode startNode = EnsureNode(
            firstPly.PositionKeyBefore,
            firstPly.FenBefore,
            ply: 0);
        RegisterNodeVisit(startNode, gameFingerprint, distinctPositionKeysInGame);
        RegisterNodeTag(startNode, metadata);

        foreach (OpeningImportPly ply in plies)
        {
            OpeningPositionNode fromNode = EnsureNode(
                ply.PositionKeyBefore,
                ply.FenBefore,
                ply: ply.Ply - 1);
            OpeningPositionNode toNode = EnsureNode(
                ply.PositionKeyAfter,
                ply.FenAfter,
                ply: ply.Ply);

            RegisterNodeVisit(toNode, gameFingerprint, distinctPositionKeysInGame);
            RegisterNodeTag(toNode, metadata);
            RegisterEdgeVisit(fromNode, toNode, ply, gameFingerprint);
        }
    }
}
