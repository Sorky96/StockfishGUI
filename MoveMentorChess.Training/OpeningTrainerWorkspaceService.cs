namespace MoveMentorChess.Training;

public sealed class OpeningTrainerWorkspaceService
{
    private readonly IAnalysisStore analysisStore;
    private readonly OpeningTheoryQueryService? openingTheory;
    private readonly OpeningTrainerService trainerService;
    private readonly IOpeningTrainingHistoryStore? historyStore;

    public OpeningTrainerWorkspaceService(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
        openingTheory = OpeningTheorySourceResolver.Create(analysisStore);
        trainerService = new OpeningTrainerService(analysisStore);
        historyStore = analysisStore as IOpeningTrainingHistoryStore;
    }

    public IReadOnlyList<OpeningLineCatalogItem> ListOpeningLines(string? filterText, RepertoireSide side, int limit = 100)
    {
        return openingTheory?.ListOpeningLines(filterText, side == RepertoireSide.Both ? null : side, limit) ?? [];
    }

    public bool TryGetOverview(OpeningLineCatalogItem item, string? playerKey, out OpeningTrainerOverview? overview)
    {
        overview = null;
        if (openingTheory is null)
        {
            return false;
        }

        if (!openingTheory.TryGetOpeningOverview(item.LineKey, item.RepertoireSide, 12, out OpeningTrainerOverview? baseOverview)
            || baseOverview is null)
        {
            return false;
        }

        IReadOnlyList<OpeningReviewItem> reviewItems = historyStore?.ListOpeningReviewItems(playerKey, 2000) ?? [];
        HashSet<string> coveredBranchKeys = reviewItems
            .Select(review => review.BranchKey.Value)
            .ToHashSet(StringComparer.Ordinal);
        int totalBranches = Math.Max(baseOverview.CommonBranches.Count, 1);
        int coveredBranches = baseOverview.CommonBranches.Count(branch => coveredBranchKeys.Contains(branch.BranchKey.Value));
        int weakBranches = Math.Max(0, totalBranches - coveredBranches);

        OpeningCoverageSummary coverage = baseOverview.Coverage with
        {
            CoveredBranches = coveredBranches,
            WeakBranches = weakBranches,
            UnseenCommonBranches = weakBranches,
            CoveragePercent = Math.Round((double)coveredBranches / totalBranches * 100.0, 1),
            StableBranches = coveredBranches
        };

        IReadOnlyList<OpeningTrainingPosition> weakPositions = BuildWeakPositionsFromHistory(playerKey, item, baseOverview);
        overview = baseOverview with
        {
            Coverage = coverage,
            WeakPositions = weakPositions
        };
        return true;
    }

    public OpeningTrainingSession BuildGuidedStudySession(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        string? playerKey,
        OpeningTrainingStrictness strictness)
    {
        List<OpeningTrainingPosition> positions = [];
        List<OpeningTrainingLine> lines = [];
        string normalizedPlayerKey = string.IsNullOrWhiteSpace(playerKey) ? "theory" : playerKey.Trim().ToLowerInvariant();
        string displayName = string.IsNullOrWhiteSpace(playerKey) ? "Theory study" : playerKey.Trim();
        ChessGame game = new();
        if (!game.TryLoadFen(item.RootFen, out _))
        {
            game.Reset();
        }

        List<OpeningTrainingMove> lineMoves = [];
        foreach (OpeningLineMove lineMove in overview.MainLine)
        {
            string currentFen = game.GetFen();
            IReadOnlyList<OpeningTheoryMove> theoryMoves = openingTheory?.GetTopMovesForFen(currentFen, 5) ?? [];
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = theoryMoves
                .Select((move, index) => new OpeningTrainingMoveOption(
                    move.MoveSan,
                    move.MoveUci,
                    index == 0 ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Alternative,
                    string.Equals(move.MoveUci, lineMove.Uci, StringComparison.OrdinalIgnoreCase),
                    index == 0 ? "Main move from local opening book." : "Playable side move from local opening book.",
                    index == 0 ? OpeningLineRecallReferenceKind.ReferenceLine : OpeningLineRecallReferenceKind.BetterMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    move.Idea,
                    move.ToOpeningPositionKey))
                .ToList();

            positions.Add(new OpeningTrainingPosition(
                $"guided:{item.LineKey.Value}:{lineMove.Ply}",
                item.OpeningKey,
                item.LineKey,
                null,
                new OpeningPositionKey(item.RootPositionKey.Value),
                OpeningTrainingMode.LineRecall,
                OpeningTrainingSourceKind.ExampleGame,
                item.Eco,
                item.DisplayName,
                currentFen,
                lineMove.Ply,
                lineMove.MoveNumber,
                lineMove.Side,
                $"Play the book move for {item.DisplayName}.",
                $"Strictness: {strictness}. Use SAN or UCI.",
                Math.Max(1, overview.MainLine.Count - positions.Count),
                item.RepertoireSide,
                strictness,
                null,
                null,
                lineMove.San,
                lineMove.Idea?.ShortExplanation,
                [item.Eco, "guided-study"],
                candidateMoves,
                [],
                new OpeningTrainingReference(string.Empty, lineMove.Side, "Theory", null, null, "Guided study", lineMove.Ply, null),
                item.LineKey.Value,
                null,
                null,
                overview.Coverage,
                overview.OpponentReplyProfile));

            lineMoves.Add(new OpeningTrainingMove(
                lineMove.Ply,
                lineMove.MoveNumber,
                lineMove.Side,
                lineMove.San,
                lineMove.Uci,
                OpeningTrainingMoveRole.Expected,
                true,
                lineMove.Idea?.ShortExplanation));

            if (!string.IsNullOrWhiteSpace(lineMove.Uci))
            {
                game.TryApplyUci(lineMove.Uci!, out _, out _);
            }
        }

        lines.Add(new OpeningTrainingLine(
            item.LineKey.Value,
            item.LineKey,
            item.OpeningKey,
            OpeningTrainingSourceKind.ExampleGame,
            item.Eco,
            item.DisplayName,
            item.RootFen,
            item.RootPositionKey,
            1,
            1,
            item.RepertoireSide == RepertoireSide.Black ? PlayerSide.Black : PlayerSide.White,
            "Guided study main line",
            lineMoves,
            new OpeningTrainingReference(string.Empty, PlayerSide.White, "Theory", null, null, "Guided study", 1, null),
            item.RepertoireSide));

        return new OpeningTrainingSession(
            $"guided:{item.LineKey.Value}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            normalizedPlayerKey,
            displayName,
            DateTime.UtcNow,
            OpeningTrainingStyle.Memorization,
            strictness,
            item.RepertoireSide,
            [OpeningTrainingMode.LineRecall],
            [OpeningTrainingSourceKind.ExampleGame],
            [new OpeningTrainingSourceSummary(OpeningTrainingSourceKind.ExampleGame, positions.Count, lines.Count, [item.Eco])],
            lines,
            positions);
    }

    public OpeningTrainingAttemptResult Evaluate(OpeningTrainingPosition position, string moveText)
    {
        return trainerService.EvaluateMove(position, moveText);
    }

    private IReadOnlyList<OpeningTrainingPosition> BuildWeakPositionsFromHistory(
        string? playerKey,
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview)
    {
        if (historyStore is null)
        {
            return [];
        }

        return historyStore.ListOpeningTrainingSessionResults(playerKey, 100)
            .SelectMany(result => result.Attempts)
            .Where(attempt => string.Equals(attempt.Eco, item.Eco, StringComparison.OrdinalIgnoreCase)
                && attempt.Score == OpeningTrainingScore.Wrong)
            .Take(5)
            .Select((attempt, index) => new OpeningTrainingPosition(
                $"history:{item.LineKey.Value}:{index}",
                item.OpeningKey,
                item.LineKey,
                attempt.BranchKey,
                attempt.PositionKey ?? item.RootPositionKey,
                OpeningTrainingMode.MistakeRepair,
                attempt.PositionSource,
                attempt.Eco,
                attempt.OpeningName,
                item.RootFen,
                index + 1,
                index + 1,
                item.RepertoireSide == RepertoireSide.Black ? PlayerSide.Black : PlayerSide.White,
                "Review a previously missed opening position.",
                attempt.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
                    ? "Last time you transposed into a known position."
                    : "Last time this position was answered incorrectly.",
                5 - index,
                item.RepertoireSide,
                OpeningTrainingStrictness.BookFlexible,
                attempt.ThemeLabel,
                attempt.SubmittedMoveText,
                attempt.ResolvedSan,
                null,
                [attempt.Eco, "history-review"],
                [],
                [],
                new OpeningTrainingReference(string.Empty, PlayerSide.White, "History", null, null, "Training history", index + 1, attempt.ThemeLabel),
                item.LineKey.Value,
                null,
                null,
                overview.Coverage,
                overview.OpponentReplyProfile))
            .ToList();
    }
}
