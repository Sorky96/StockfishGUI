namespace MoveMentorChess.Opening;

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

        return store.TryGetOpeningPositionByKey(OpeningPositionKeyBuilder.BuildKey(fen).Value, out position);
    }

    public bool TryGetPositionByKey(string positionKey, out OpeningTheoryPosition? position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        return store.TryGetOpeningPositionByKey(positionKey, out position);
    }

    public IReadOnlyList<OpeningTheoryMove> GetTopMovesForFen(string fen, int limit = 10, bool playableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        return GetTopMovesForPositionKey(OpeningPositionKeyBuilder.BuildKey(fen).Value, limit, playableOnly);
    }

    public IReadOnlyList<OpeningTheoryMove> GetTopMovesForPositionKey(
        string positionKey,
        int limit = 10,
        bool playableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        return AddMoveIdeas(store.GetOpeningMovesByPositionKey(positionKey, limit, playableOnly));
    }

    public IReadOnlyList<OpeningLineCatalogItem> ListOpeningLines(
        string? filterText = null,
        RepertoireSide? repertoireSide = null,
        int limit = 100)
    {
        int safeLimit = Math.Clamp(limit, 1, 500);
        int fetchLimit = Math.Clamp(safeLimit * 4, safeLimit, 500);

        return store.ListOpeningLines(filterText, repertoireSide, fetchLimit)
            .Where(IsLineEcoConsistent)
            .Take(safeLimit)
            .ToList();
    }

    public bool TryGetOpeningOverview(
        OpeningLineKey lineKey,
        RepertoireSide repertoireSide,
        int maxDepth,
        out OpeningTrainerOverview? overview)
    {
        if (lineKey.IsEmpty)
        {
            overview = null;
            return false;
        }

        OpeningLineCatalogItem? line = store.ListOpeningLines(limit: 500)
            .FirstOrDefault(item => item.LineKey == lineKey);
        if (line is null || !IsLineEcoConsistent(line))
        {
            overview = null;
            return false;
        }

        List<OpeningLineMove> mainLine = [];
        List<OpeningTrainingBranch> branches = [];
        List<OpeningMoveIdea> ideas = [];
        OpeningPositionKey currentPositionKey = line.RootPositionKey;
        int maxPly = Math.Max(1, maxDepth);

        for (int ply = 0; ply < maxPly; ply++)
        {
            IReadOnlyList<OpeningTheoryMove> moves = GetTopMovesForPositionKey(currentPositionKey.Value, 6, playableOnly: false);
            if (moves.Count == 0)
            {
                break;
            }

            OpeningTheoryMove primary = moves[0];
            OpeningMoveIdea primaryIdea = primary.Idea ?? OpeningMoveIdeaHeuristics.Build(primary.MoveSan, primary.IsMainMove);
            ideas.Add(primaryIdea);

            if (TryGetPositionByKey(primary.ToPositionKey, out OpeningTheoryPosition? nextPosition) && nextPosition is not null)
            {
                mainLine.Add(new OpeningLineMove(
                    nextPosition.Ply,
                    nextPosition.MoveNumber,
                    ParsePlayerSide(nextPosition.SideToMove) == PlayerSide.White ? PlayerSide.Black : PlayerSide.White,
                    primary.MoveSan,
                    primary.MoveUci,
                    currentPositionKey,
                    primary.ToOpeningPositionKey,
                    primary.IsMainMove,
                    primaryIdea));
                currentPositionKey = primary.ToOpeningPositionKey;
            }
            else
            {
                break;
            }
        }

        IReadOnlyList<OpeningTheoryMove> branchMoves = GetTopMovesForPositionKey(line.RootPositionKey.Value, 5, playableOnly: false);
        foreach (OpeningTheoryMove move in branchMoves)
        {
            OpeningTrainingMoveOption? recommended = null;
            IReadOnlyList<OpeningTheoryMove> replies = GetTopMovesForPositionKey(move.ToPositionKey, 1, playableOnly: false);
            OpeningTheoryMove? bestReply = replies.FirstOrDefault();
            if (bestReply is not null)
            {
                recommended = new OpeningTrainingMoveOption(
                    bestReply.MoveSan,
                    bestReply.MoveUci,
                    OpeningTrainingMoveRole.Expected,
                    bestReply.IsMainMove,
                    "Best local book response.",
                    OpeningLineRecallReferenceKind.BestMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    bestReply.Idea ?? OpeningMoveIdeaHeuristics.Build(bestReply.MoveSan, bestReply.IsMainMove),
                    bestReply.ToOpeningPositionKey);
            }

            branches.Add(new OpeningTrainingBranch(
                new OpeningBranchKey($"{line.LineKey.Value}|{move.MoveUci}"),
                move.MoveSan,
                move.MoveUci,
                Math.Max(1, move.DistinctGameCount),
                $"Book frequency: {move.OccurrenceCount} occurrence(s), {move.DistinctGameCount} game(s).",
                recommended,
                [],
                [],
                move.ToOpeningPositionKey));
        }

        OpeningCoverageSummary coverage = new(
            TotalBookBranches: Math.Max(branches.Count, 1),
            CoveredBranches: 0,
            WeakBranches: branches.Count,
            UnseenCommonBranches: branches.Count,
            CoveragePercent: 0,
            KnownPositions: mainLine.Count,
            StableBranches: 0,
            KnowledgeBoundaryPly: mainLine.LastOrDefault()?.Ply ?? 0);
        OpponentReplyProfile opponentProfile = new(
            line.LineKey,
            line.RepertoireSide == RepertoireSide.Both ? repertoireSide : line.RepertoireSide,
            branches.Select(branch => new OpponentMoveFrequency(
                branch.OpponentMove,
                branch.OpponentMoveUci,
                branch.Frequency,
                branch.Frequency,
                0,
                0,
                false,
                OpponentMoveFrequencySourceKind.BookFrequency,
                branch.SourceSummary)).ToList(),
            branches.Count == 0
                ? "No opponent branches were found in the local opening book."
                : $"Tracked {branches.Count} opponent branch(es) from the local opening book.");

        IReadOnlyList<OpeningLineMove> pathLineMoves = GetOpeningPathLineMoves(line.RootPositionKey);
        if (pathLineMoves.Count > 0)
        {
            mainLine.InsertRange(0, pathLineMoves);
        }

        overview = new OpeningTrainerOverview(
            line.OpeningKey,
            line.LineKey,
            line.RepertoireSide,
            line.Eco,
            line.OpeningName,
            line.VariationName,
            mainLine,
            branches,
            opponentProfile,
            coverage,
            [],
            [],
            ideas);
        return true;
    }

    public CanonicalLineResolutionResult ResolveCanonicalLine(string fen)
    {
        return new CanonicalLineResolver(this).Resolve(fen);
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

    private bool IsLineEcoConsistent(OpeningLineCatalogItem line)
    {
        IReadOnlyList<string> validationMoves = GetOpeningValidationMoves(line.RootPositionKey);
        return EcoConsistencyService.IsConsistentWithMoves(line.Eco, validationMoves);
    }

    private IReadOnlyList<string> GetOpeningValidationMoves(OpeningPositionKey rootPositionKey)
    {
        if (store is IOpeningLineContextStore contextStore)
        {
            return contextStore.GetOpeningValidationMoves(rootPositionKey);
        }

        return BuildPrimaryContinuationMoves(rootPositionKey, maxPly: 4);
    }

    private IReadOnlyList<OpeningLineMove> GetOpeningPathLineMoves(OpeningPositionKey rootPositionKey)
    {
        return store is IOpeningLineContextStore contextStore
            ? contextStore.GetOpeningPathLineMoves(rootPositionKey)
            : [];
    }

    private IReadOnlyList<string> BuildPrimaryContinuationMoves(OpeningPositionKey rootPositionKey, int maxPly)
    {
        List<string> moves = [];
        OpeningPositionKey currentPositionKey = rootPositionKey;

        for (int ply = 0; ply < maxPly; ply++)
        {
            OpeningTheoryMove? move = GetTopMovesForPositionKey(currentPositionKey.Value, 1, playableOnly: false).FirstOrDefault();
            if (move is null)
            {
                break;
            }

            moves.Add(move.MoveSan);
            currentPositionKey = move.ToOpeningPositionKey;
        }

        return moves;
    }

    private static IReadOnlyList<OpeningTheoryMove> AddMoveIdeas(IReadOnlyList<OpeningTheoryMove> moves)
    {
        return moves
            .Select(move => move.Idea is null
                ? move with { Idea = OpeningMoveIdeaHeuristics.Build(move.MoveSan, move.IsMainMove) }
                : move)
            .ToList();
    }

    private static PlayerSide ParsePlayerSide(string? sideToMove)
    {
        return Enum.TryParse(sideToMove, ignoreCase: true, out PlayerSide side)
            ? side
            : PlayerSide.White;
    }
}
