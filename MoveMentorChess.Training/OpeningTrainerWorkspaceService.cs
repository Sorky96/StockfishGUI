namespace MoveMentorChess.Training;

public sealed class OpeningTrainerWorkspaceService
{
    private readonly IAnalysisStore analysisStore;
    private readonly IClock clock;
    private readonly OpeningTheoryQueryService? openingTheory;
    private readonly OpeningTrainerService trainerService;
    private readonly OpeningTrainingRecommendationService recommendationService;
    private readonly OpeningTrainingPriorityService priorityService = new();
    private readonly OpeningTrainingCoachingService coachingService = new();
    private readonly OpeningTrainingNextActionService nextActionService = new();
    private readonly OpeningTrainingResultPlanService resultPlanService = new();
    private readonly PlayerOpeningPlanService playerOpeningPlanService = new();
    private readonly SpecialTrainingModeService specialModeService = new();
    private readonly OpeningUnderstandingService understandingService = new();
    private readonly OpeningTrainingTelemetryService telemetryService;
    private readonly IOpeningTrainingHistoryStore? historyStore;

    public OpeningTrainerWorkspaceService(IAnalysisStore analysisStore)
        : this(analysisStore, SystemClock.Instance)
    {
    }

    public OpeningTrainerWorkspaceService(IAnalysisStore analysisStore, IClock clock)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        openingTheory = OpeningTheorySourceResolver.Create(analysisStore);
        trainerService = new OpeningTrainerService(analysisStore, clock);
        recommendationService = new OpeningTrainingRecommendationService(clock);
        historyStore = analysisStore as IOpeningTrainingHistoryStore;
        telemetryService = new OpeningTrainingTelemetryService(analysisStore as IOpeningTrainingTelemetryStore, clock);
    }

    public DateTime UtcNow => clock.UtcNow;

    public IReadOnlyList<OpeningLineCatalogItem> ListOpeningLines(string? filterText, RepertoireSide side, int limit = 100)
    {
        return openingTheory?.ListOpeningLines(filterText, side == RepertoireSide.Both ? null : side, limit) ?? [];
    }

    public void TrackTelemetry(
        string eventName,
        string? playerKey = null,
        OpeningLineCatalogItem? opening = null,
        OpeningTrainingSession? session = null,
        string? recommendationId = null,
        SpecialTrainingModeKind? specialMode = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        telemetryService.Track(eventName, playerKey, opening, session, recommendationId, specialMode, properties);
    }

    public IReadOnlyList<OpeningTrainingTelemetryEvent> GetTelemetrySnapshot()
    {
        return telemetryService.Snapshot();
    }

    public IReadOnlyList<SpecialTrainingModeDefinition> ListSpecialTrainingModes()
    {
        return specialModeService.ListDefinitions();
    }

    public TrainingRecommendationCard? GetRecommendationForToday(string? playerKey, RepertoireSide side, int limit = 120)
    {
        IReadOnlyList<OpeningLineCatalogItem> availableLines = ListOpeningLines(null, side, limit);
        IReadOnlyList<OpeningReviewItem> reviewItems = historyStore?.ListOpeningReviewItems(playerKey, 2000) ?? [];
        IReadOnlyList<OpeningTrainingSessionResult> sessionResults = historyStore?.ListOpeningTrainingSessionResults(playerKey, 200) ?? [];
        IReadOnlyList<OpeningTrainingScheduledAction> dueActions = historyStore?.ListDueOpeningTrainingScheduledActions(playerKey, UtcNow, 50) ?? [];

        return recommendationService.Recommend(playerKey, availableLines, reviewItems, sessionResults, dueActions);
    }

    public PlayerOpeningPlan GetPlayerOpeningPlan(string? playerKey, RepertoireSide side, int limit = 120)
    {
        IReadOnlyList<OpeningLineCatalogItem> availableLines = ListOpeningLines(null, side, limit);
        IReadOnlyList<OpeningReviewItem> reviewItems = historyStore?.ListOpeningReviewItems(playerKey, 2000) ?? [];
        IReadOnlyList<OpeningTrainingSessionResult> sessionResults = historyStore?.ListOpeningTrainingSessionResults(playerKey, 200) ?? [];
        IReadOnlyList<OpeningTrainingScheduledAction> dueActions = historyStore?.ListDueOpeningTrainingScheduledActions(playerKey, UtcNow, 50) ?? [];
        TrainingRecommendationCard? recommendation = recommendationService.Recommend(playerKey, availableLines, reviewItems, sessionResults, dueActions);

        return playerOpeningPlanService.BuildPlan(playerKey, recommendation, availableLines, reviewItems, sessionResults, dueActions);
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
        IReadOnlyList<OpeningTrainingSessionResult> sessionResults = historyStore?.ListOpeningTrainingSessionResults(playerKey, 200) ?? [];
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

        IReadOnlyList<OpeningTrainingPosition> weakPositions = BuildWeakPositionsFromHistory(playerKey, item, baseOverview, sessionResults);
        overview = baseOverview with
        {
            Coverage = coverage,
            Priorities = priorityService.BuildPriorities(baseOverview with
            {
                Coverage = coverage,
                WeakPositions = weakPositions
            }, reviewItems, sessionResults),
            WeakPositions = weakPositions
        };
        return true;
    }

    public IReadOnlyList<OpeningUnderstandingCard> BuildUnderstandingCards(
        OpeningTrainerOverview overview,
        OpeningLineCatalogItem item)
        => understandingService.BuildCards(overview, item);

    public OpeningTrainingSession BuildGuidedStudySession(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        string? playerKey,
        OpeningTrainingStrictness strictness)
        => BuildGuidedStudySession(item, overview, playerKey, strictness, null);

    public OpeningTrainingSession BuildGuidedStudySession(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        string? playerKey,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode)
        => BuildGuidedStudySession(item, overview, playerKey, strictness, specialMode, null);

    public OpeningTrainingSession BuildGuidedStudySession(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        string? playerKey,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode,
        OpeningTrainingSessionTarget? target)
    {
        List<OpeningTrainingPosition> positions = [];
        List<OpeningTrainingLine> lines = [];
        OpeningTrainingStrictness effectiveStrictness = specialMode?.Strictness ?? strictness;
        int maxPositions = specialMode?.MaxPositions ?? int.MaxValue;
        string normalizedPlayerKey = string.IsNullOrWhiteSpace(playerKey) ? "theory" : playerKey.Trim().ToLowerInvariant();
        string displayName = string.IsNullOrWhiteSpace(playerKey) ? "Theory study" : playerKey.Trim();
        ChessGame game = new();
        string startFen = ResolveLineStartFen(overview, item.RootFen);
        if (!game.TryLoadFen(startFen, out _))
        {
            game.Reset();
        }

        PlayerSide? studySide = ResolveStudySide(item.RepertoireSide);
        IReadOnlyList<OpeningTrainingPosition> targetedPositions = BuildTargetedPositions(item, overview, effectiveStrictness, specialMode, target);
        foreach (OpeningTrainingPosition targetedPosition in targetedPositions)
        {
            if (positions.Count >= maxPositions)
            {
                break;
            }

            positions.Add(targetedPosition);
        }

        if (targetedPositions.Count == 0)
        {
            OpeningTrainingPosition? planSelection = BuildPlanSelectionPosition(item, overview, effectiveStrictness, specialMode);
            if (planSelection is not null && positions.Count < maxPositions)
            {
                positions.Add(planSelection);
            }

            foreach (OpeningTrainingPosition weakPosition in SelectSpecialWeakPositions(overview, specialMode))
            {
                if (positions.Count >= maxPositions)
                {
                    break;
                }

                OpeningTrainingPosition modePosition = weakPosition with
                {
                    Strictness = effectiveStrictness,
                    Tags = weakPosition.Tags.Concat([GetSpecialModeTag(specialMode)]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    CoachHints = coachingService.BuildHints(weakPosition)
                };
                positions.Add(modePosition);
            }
        }

        List<OpeningTrainingMove> lineMoves = [];
        foreach (OpeningLineMove lineMove in overview.MainLine)
        {
            if (positions.Count >= maxPositions)
            {
                break;
            }

            string currentFen = AlignGameToLinePosition(game, lineMove.FromPositionKey);
            IReadOnlyList<OpeningTheoryMove> theoryMoves = openingTheory?.GetTopMovesForPositionKey(lineMove.FromPositionKey.Value, 5) ?? [];
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = theoryMoves
                .Select((move, index) =>
                {
                    bool isLineMove = IsLineMove(move, lineMove);
                    return new OpeningTrainingMoveOption(
                        move.MoveSan,
                        move.MoveUci,
                        isLineMove ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Alternative,
                        isLineMove,
                        isLineMove ? "Main repertoire move from local opening book." : "Playable side move from local opening book.",
                        isLineMove ? OpeningLineRecallReferenceKind.ReferenceLine : OpeningLineRecallReferenceKind.BetterMove,
                        OpeningTrainingMoveSourceKind.OpeningBook,
                        move.Idea,
                        move.ToOpeningPositionKey);
                })
                .ToList();

            if (ShouldPromptForSide(lineMove.Side, studySide))
            {
                OpeningTrainingPosition position = new(
                    $"guided:{item.LineKey.Value}:{lineMove.Ply}",
                    item.OpeningKey,
                    item.LineKey,
                    null,
                    lineMove.FromPositionKey,
                    OpeningTrainingMode.LineRecall,
                    OpeningTrainingSourceKind.ExampleGame,
                    item.Eco,
                    item.DisplayName,
                    currentFen,
                    lineMove.Ply,
                    lineMove.MoveNumber,
                    lineMove.Side,
                    $"Play your {lineMove.Side} repertoire move for {item.DisplayName}.",
                    specialMode is null
                        ? "Opponent moves are replayed automatically. Accepted: main repertoire move and sound theory alternatives."
                        : $"{specialMode.Title}. Opponent moves are replayed automatically. Accepted: main repertoire move and sound theory alternatives.",
                    Math.Max(1, overview.MainLine.Count - positions.Count),
                    item.RepertoireSide,
                    effectiveStrictness,
                    null,
                    null,
                    lineMove.San,
                    lineMove.Idea?.ShortExplanation,
                    BuildSessionTags(item.Eco, specialMode, target, targetedPositions.Count == 0 ? null : "target-fallback:false"),
                    candidateMoves,
                    [],
                    new OpeningTrainingReference(string.Empty, lineMove.Side, "Theory", null, null, "Guided study", lineMove.Ply, null),
                    item.LineKey.Value,
                    null,
                    null,
                    overview.Coverage,
                    overview.OpponentReplyProfile);
                positions.Add(position with
                {
                    CoachHints = coachingService.BuildHints(position)
                });
            }

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

        DateTime createdUtc = UtcNow;
        return new OpeningTrainingSession(
            $"guided:{item.LineKey.Value}:{createdUtc:yyyyMMddHHmmss}",
            normalizedPlayerKey,
            displayName,
            createdUtc,
            OpeningTrainingStyle.Memorization,
            effectiveStrictness,
            item.RepertoireSide,
            positions.Select(position => position.Mode).Distinct().ToList(),
            positions.Select(position => position.SourceKind).Distinct().ToList(),
            positions
                .GroupBy(position => position.SourceKind)
                .Select(group => new OpeningTrainingSourceSummary(group.Key, group.Count(), lines.Count, [item.Eco]))
                .ToList(),
            lines,
            positions);
    }

    public OpeningTrainingAttemptResult Evaluate(OpeningTrainingPosition position, string moveText)
    {
        OpeningTrainingAttemptResult result = trainerService.EvaluateMove(position, moveText);
        return coachingService.AddCoaching(position, result);
    }

    public OpeningTrainingSession RebuildContinuationAfterAcceptedMove(
        OpeningTrainingSession session,
        int completedPositionIndex,
        OpeningTrainingPosition completedPosition,
        OpeningTrainingAttemptResult result)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(completedPosition);
        ArgumentNullException.ThrowIfNull(result);

        if (openingTheory is null
            || result.Score == OpeningTrainingScore.Wrong
            || result.ResolvedPosition is null
            || completedPosition.AnswerKind != OpeningTrainingAnswerKind.Move
            || completedPositionIndex < 0
            || completedPositionIndex >= session.Positions.Count)
        {
            return session;
        }

        int remainingCount = session.Positions.Count - completedPositionIndex - 1;
        if (remainingCount <= 0)
        {
            return session;
        }

        IReadOnlyList<OpeningTrainingPosition> continuation = BuildContinuationFromFen(
            completedPosition,
            result.ResolvedPosition,
            remainingCount,
            session.RepertoireSide);
        if (continuation.Count == 0)
        {
            return session;
        }

        List<OpeningTrainingPosition> positions = session.Positions
            .Take(completedPositionIndex + 1)
            .Concat(continuation)
            .ToList();

        return session with
        {
            Positions = positions,
            SupportedModes = positions.Select(position => position.Mode).Distinct().ToList(),
            IncludedSources = positions.Select(position => position.SourceKind).Distinct().ToList(),
            SourceSummaries = positions
                .GroupBy(position => position.SourceKind)
                .Select(group => new OpeningTrainingSourceSummary(
                    group.Key,
                    group.Count(),
                    session.Lines.Count,
                    group.Select(position => position.Eco)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToList()
        };
    }

    public OpeningTrainingSessionResult SaveSessionResult(
        OpeningTrainingSession session,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts,
        OpeningTrainingSessionOutcome outcome,
        string? startSource = null,
        string? recommendationId = null,
        int hintCount = 0,
        int? timeToFirstMoveSeconds = null,
        DateTime? abandonedUtc = null,
        IReadOnlyList<string>? completedNextActionIds = null)
    {
        return trainerService.SaveSessionResult(
            session,
            attempts,
            outcome,
            completedUtc: UtcNow,
            startSource: startSource,
            recommendationId: recommendationId,
            hintCount: hintCount,
            timeToFirstMoveSeconds: timeToFirstMoveSeconds,
            abandonedUtc: abandonedUtc,
            completedNextActionIds: completedNextActionIds);
    }

    public IReadOnlyList<TrainingCoachHint> BuildCoachHints(OpeningTrainingPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return position.CoachHints is { Count: > 0 }
            ? position.CoachHints
            : coachingService.BuildHints(position);
    }

    public IReadOnlyList<TrainingNextAction> BuildNextActions(TrainingSessionOutcomeSummary summary)
    {
        return nextActionService.BuildNextActions(summary);
    }

    public TrainingResultLearningPlan BuildLearningPlan(
        TrainingSessionOutcomeSummary summary,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts,
        IReadOnlyList<TrainingNextAction> nextActions)
    {
        return resultPlanService.BuildPlan(summary, attempts, nextActions);
    }

    public IReadOnlyList<OpeningTrainingScheduledAction> SaveScheduledActions(
        OpeningTrainingSessionResult sessionResult,
        IReadOnlyList<TrainingNextAction> nextActions)
    {
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(nextActions);

        IReadOnlyList<OpeningTrainingScheduledAction> actions = nextActionService.BuildScheduledActions(
            sessionResult.PlayerKey,
            sessionResult,
            nextActions,
            UtcNow);
        if (actions.Count > 0)
        {
            historyStore?.SaveOpeningTrainingScheduledActions(sessionResult.PlayerKey, actions);
        }

        return actions;
    }

    public void MarkScheduledActionCompleted(string playerKey, string actionId, DateTime completedUtc)
    {
        if (string.IsNullOrWhiteSpace(playerKey) || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        historyStore?.MarkOpeningTrainingScheduledActionCompleted(playerKey, actionId, completedUtc);
    }

    public void MarkScheduledActionCompleted(string playerKey, string actionId)
    {
        MarkScheduledActionCompleted(playerKey, actionId, UtcNow);
    }

    public OpeningTrainingSessionOptions BuildSpecialModeOptions(SpecialTrainingModeDefinition definition)
    {
        return specialModeService.BuildOptions(definition);
    }

    private static IReadOnlyList<OpeningTrainingPosition> SelectSpecialWeakPositions(
        OpeningTrainerOverview overview,
        SpecialTrainingModeDefinition? specialMode)
    {
        if (specialMode?.PrioritizeWeakPositions != true)
        {
            return [];
        }

        return overview.WeakPositions
            .OrderByDescending(position => position.Priority)
            .Take(specialMode.MaxPositions)
            .ToList();
    }

    private static string GetSpecialModeTag(SpecialTrainingModeDefinition? specialMode)
    {
        return specialMode is null
            ? "guided-study"
            : $"special-mode:{specialMode.Kind}";
    }

    private IReadOnlyList<OpeningTrainingPosition> BuildTargetedPositions(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode,
        OpeningTrainingSessionTarget? target)
    {
        if (target is null || !target.LineKey.Equals(item.LineKey))
        {
            return [];
        }

        return target.Action switch
        {
            TrainingPriorityAction.RepairThisPosition => BuildTargetedWeakPosition(overview, strictness, specialMode, target),
            TrainingPriorityAction.TrainThisBranch or TrainingPriorityAction.ReviewOpponentReply => BuildTargetedBranchPosition(item, overview, strictness, specialMode, target),
            _ => []
        };
    }

    private OpeningTrainingPosition? BuildPlanSelectionPosition(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode)
    {
        if (specialMode?.PreferredModes.Contains(OpeningTrainingMode.PlanSelection) != true)
        {
            return null;
        }

        OpeningLineMove? move = overview.MainLine.FirstOrDefault(lineMove => lineMove.Idea is not null)
            ?? overview.MainLine.FirstOrDefault();
        if (move is null)
        {
            return null;
        }

        string correctPlan = move.Idea?.ShortExplanation
            ?? "Keep development connected to the center.";
        IReadOnlyList<OpeningTrainingAnswerOption> options =
        [
            new OpeningTrainingAnswerOption(
                "plan:correct",
                correctPlan,
                true,
                "That plan matches the book idea for this position."),
            new OpeningTrainingAnswerOption(
                "plan:side-pawn",
                "Launch a flank pawn before completing development.",
                false,
                "That usually delays the opening plan unless there is a concrete tactic."),
            new OpeningTrainingAnswerOption(
                "plan:queen",
                "Bring the queen out early and look for one-move threats.",
                false,
                "The trainer is asking for the underlying opening plan, not an early queen sortie.")
        ];

        OpeningTrainingPosition position = new(
            $"plan-selection:{item.LineKey.Value}:{move.Ply}",
            item.OpeningKey,
            item.LineKey,
            null,
            move.FromPositionKey,
            OpeningTrainingMode.PlanSelection,
            OpeningTrainingSourceKind.ExampleGame,
            item.Eco,
            item.DisplayName,
            item.RootFen,
            move.Ply,
            move.MoveNumber,
            move.Side,
            $"Choose the plan behind the next book move in {item.DisplayName}.",
            specialMode is null
                ? "Select the plan that explains the next repertoire move."
                : $"{specialMode.Title}. Select the plan that explains the next repertoire move.",
            Math.Max(100, overview.MainLine.Count),
            item.RepertoireSide,
            strictness,
            "plan-selection",
            null,
            move.San,
            move.Idea?.ShortExplanation,
            BuildSessionTags(item.Eco, specialMode, null).Concat(["answer-kind:single-choice"]).ToList(),
            [],
            [],
            new OpeningTrainingReference(string.Empty, move.Side, "Theory", null, null, "Plan selection", move.Ply, null),
            item.LineKey.Value,
            null,
            null,
            overview.Coverage,
            overview.OpponentReplyProfile,
            null,
            OpeningTrainingAnswerKind.SingleChoice,
            options);

        return position with
        {
            CoachHints = coachingService.BuildHints(position)
        };
    }

    private IReadOnlyList<OpeningTrainingPosition> BuildContinuationFromFen(
        OpeningTrainingPosition template,
        OpeningPositionIdentity reachedPosition,
        int maxPositions,
        RepertoireSide repertoireSide)
    {
        List<OpeningTrainingPosition> positions = [];
        string currentFen = reachedPosition.Fen;
        OpeningPositionKey currentPositionKey = reachedPosition.PositionKey;
        int currentPly = reachedPosition.Ply;
        int currentMoveNumber = reachedPosition.MoveNumber;
        PlayerSide sideToMove = reachedPosition.SideToMove;
        PlayerSide? studySide = ResolveStudySide(repertoireSide);
        int safetyLimit = Math.Max(maxPositions + 4, maxPositions * 4);

        for (int index = 0; positions.Count < maxPositions && index < safetyLimit; index++)
        {
            IReadOnlyList<OpeningTheoryMove> theoryMoves = openingTheory?.GetTopMovesForFen(currentFen, 5) ?? [];
            if (theoryMoves.Count == 0)
            {
                break;
            }

            OpeningTheoryMove preferred = theoryMoves[0];
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = theoryMoves
                .Select((move, moveIndex) => new OpeningTrainingMoveOption(
                    move.MoveSan,
                    move.MoveUci,
                    moveIndex == 0 ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Alternative,
                    moveIndex == 0,
                    moveIndex == 0 ? "Main continuation for the line you reached." : "Playable continuation from the reached branch.",
                    moveIndex == 0 ? OpeningLineRecallReferenceKind.ReferenceLine : OpeningLineRecallReferenceKind.BetterMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    move.Idea,
                    move.ToOpeningPositionKey))
                .ToList();

            if (ShouldPromptForSide(sideToMove, studySide))
            {
                OpeningTrainingPosition position = new(
                    $"continuation:{template.OpeningLineKey.Value}:{currentPositionKey.Value}:{index}",
                    template.OpeningKey,
                    template.OpeningLineKey,
                    template.OpeningBranchKey,
                    currentPositionKey,
                    OpeningTrainingMode.LineRecall,
                    template.SourceKind,
                    template.Eco,
                    template.OpeningName,
                    currentFen,
                    currentPly,
                    currentMoveNumber,
                    sideToMove,
                    $"Continue with your {sideToMove} repertoire move in {template.OpeningName}.",
                    "The continuation has been rebuilt from the move you actually played; opponent moves are replayed automatically.",
                    Math.Max(1, maxPositions - positions.Count),
                    template.RepertoireSide,
                    template.Strictness,
                    template.ThemeLabel,
                    null,
                    preferred.MoveSan,
                    preferred.Idea?.ShortExplanation,
                    template.Tags.Concat(["branch-continuation"]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    candidateMoves,
                    [],
                    new OpeningTrainingReference(
                        template.Reference.GameFingerprint,
                        sideToMove,
                        template.Reference.OpponentName,
                        template.Reference.DateText,
                        template.Reference.Result,
                        "Reached branch continuation",
                        currentPly,
                        template.Reference.MistakeLabel),
                    template.LineId,
                    null,
                    "Continuation chosen from the move you played.",
                    template.CoverageSummary,
                    template.OpponentReplyProfile);
                positions.Add(position with
                {
                    CoachHints = coachingService.BuildHints(position)
                });
            }

            if (string.IsNullOrWhiteSpace(preferred.MoveUci))
            {
                break;
            }

            ChessGame game = new();
            if (!game.TryLoadFen(currentFen, out _)
                || !game.TryApplyUci(preferred.MoveUci, out AppliedMoveInfo? applied, out _)
                || applied is null)
            {
                break;
            }

            currentFen = applied.FenAfter;
            currentPositionKey = OpeningPositionKeyBuilder.BuildKey(currentFen);
            currentPly++;
            currentMoveNumber = sideToMove == PlayerSide.Black ? currentMoveNumber + 1 : currentMoveNumber;
            sideToMove = sideToMove == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;
        }

        return positions;
    }

    private static PlayerSide? ResolveStudySide(RepertoireSide repertoireSide)
    {
        return repertoireSide switch
        {
            RepertoireSide.White => PlayerSide.White,
            RepertoireSide.Black => PlayerSide.Black,
            _ => null
        };
    }

    private static bool ShouldPromptForSide(PlayerSide sideToMove, PlayerSide? studySide)
        => studySide is null || sideToMove == studySide.Value;

    private string ResolveLineStartFen(OpeningTrainerOverview overview, string fallbackFen)
    {
        OpeningPositionKey? firstPositionKey = overview.MainLine.FirstOrDefault()?.FromPositionKey;
        if (firstPositionKey is not null
            && openingTheory?.TryGetPositionByKey(firstPositionKey.Value.Value, out OpeningTheoryPosition? position) == true
            && position is not null)
        {
            return position.Fen;
        }

        return fallbackFen;
    }

    private string AlignGameToLinePosition(ChessGame game, OpeningPositionKey positionKey)
    {
        string currentFen = game.GetFen();
        if (OpeningPositionKeyBuilder.BuildKey(currentFen).Equals(positionKey))
        {
            return currentFen;
        }

        if (openingTheory?.TryGetPositionByKey(positionKey.Value, out OpeningTheoryPosition? position) == true
            && position is not null
            && game.TryLoadFen(position.Fen, out _))
        {
            return position.Fen;
        }

        return currentFen;
    }

    private static bool IsLineMove(OpeningTheoryMove move, OpeningLineMove lineMove)
        => (!string.IsNullOrWhiteSpace(move.MoveUci)
                && !string.IsNullOrWhiteSpace(lineMove.Uci)
                && string.Equals(move.MoveUci, lineMove.Uci, StringComparison.OrdinalIgnoreCase))
            || string.Equals(NormalizeMoveText(move.MoveSan), NormalizeMoveText(lineMove.San), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMoveText(string? moveText)
        => string.IsNullOrWhiteSpace(moveText)
            ? string.Empty
            : moveText.Trim().TrimEnd('+', '#', '!', '?');

    private IReadOnlyList<OpeningTrainingPosition> BuildTargetedWeakPosition(
        OpeningTrainerOverview overview,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode,
        OpeningTrainingSessionTarget target)
    {
        OpeningTrainingPosition? weakPosition = overview.WeakPositions.FirstOrDefault(position =>
                target.PositionKey.HasValue
                && position.OpeningPositionKey.Equals(target.PositionKey.Value))
            ?? overview.WeakPositions.FirstOrDefault(position =>
                string.Equals(position.PositionId, target.SourceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals($"position:{position.PositionId}", target.SourceId, StringComparison.OrdinalIgnoreCase));

        if (weakPosition is null)
        {
            return [];
        }

        OpeningTrainingPosition targeted = weakPosition with
        {
            Strictness = strictness,
            Priority = Math.Max(weakPosition.Priority, 100),
            Tags = BuildSessionTags(weakPosition.Eco, specialMode, target),
            CoachHints = coachingService.BuildHints(weakPosition)
        };
        return [targeted];
    }

    private IReadOnlyList<OpeningTrainingPosition> BuildTargetedBranchPosition(
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        OpeningTrainingStrictness strictness,
        SpecialTrainingModeDefinition? specialMode,
        OpeningTrainingSessionTarget target)
    {
        OpeningTrainingBranch? branch = overview.CommonBranches.FirstOrDefault(candidate =>
                target.BranchKey.HasValue
                && candidate.BranchKey.Equals(target.BranchKey.Value))
            ?? overview.CommonBranches.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(target.OpponentMoveUci)
                && string.Equals(candidate.OpponentMoveUci, target.OpponentMoveUci, StringComparison.OrdinalIgnoreCase))
            ?? overview.CommonBranches.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(target.OpponentMove)
                && string.Equals(candidate.OpponentMove, target.OpponentMove, StringComparison.OrdinalIgnoreCase));

        if (branch is null)
        {
            return [];
        }

        IReadOnlyList<OpeningTrainingBranch> branches = [branch];
        IReadOnlyList<OpeningTrainingMoveOption> candidateMoves =
        [
            new OpeningTrainingMoveOption(
                branch.OpponentMove,
                branch.OpponentMoveUci,
                OpeningTrainingMoveRole.Alternative,
                true,
                branch.SourceSummary,
                OpeningLineRecallReferenceKind.ReferenceLine,
                OpeningTrainingMoveSourceKind.OpeningBook,
                branch.RecommendedResponse?.Idea,
                branch.ResultingPositionKey)
        ];

        OpeningTrainingPosition position = new(
            $"target:{target.Action}:{branch.BranchKey.Value}",
            item.OpeningKey,
            item.LineKey,
            branch.BranchKey,
            branch.ResultingPositionKey ?? item.RootPositionKey,
            OpeningTrainingMode.BranchAwareness,
            OpeningTrainingSourceKind.OpeningWeakness,
            item.Eco,
            item.DisplayName,
            item.RootFen,
            1,
            1,
            item.RepertoireSide == RepertoireSide.Black ? PlayerSide.White : PlayerSide.Black,
            $"Review the selected opponent reply: {branch.OpponentMove}.",
            branch.RecommendedResponse is null
                ? "Recognize this branch from the local opening book."
                : $"Recognize this branch, then remember the prepared response: {branch.RecommendedResponse.DisplayText}.",
            Math.Max(100, branch.Frequency * 10),
            item.RepertoireSide,
            strictness,
            target.Action.ToString(),
            branch.OpponentMove,
            branch.RecommendedResponse?.DisplayText,
            branch.RecommendedResponse?.Note,
            BuildSessionTags(item.Eco, specialMode, target),
            candidateMoves,
            branch.Continuation,
            new OpeningTrainingReference(string.Empty, PlayerSide.White, "Priority", null, null, "Targeted priority", 1, target.Action.ToString()),
            item.LineKey.Value,
            branches,
            branch.SourceSummary,
            new OpeningCoverageSummary(
                1,
                0,
                1,
                1,
                0,
                0,
                0,
                0),
            new OpponentReplyProfile(
                item.LineKey,
                item.RepertoireSide,
                [new OpponentMoveFrequency(
                    branch.OpponentMove,
                    branch.OpponentMoveUci,
                    branch.Frequency,
                    branch.Frequency,
                    0,
                    0,
                    false,
                    OpponentMoveFrequencySourceKind.BookFrequency,
                    branch.SourceSummary)],
                $"Targeted reply: {branch.OpponentMove}."));

        return [position with
        {
            CoachHints = coachingService.BuildHints(position)
        }];
    }

    private static IReadOnlyList<string> BuildSessionTags(
        string eco,
        SpecialTrainingModeDefinition? specialMode,
        OpeningTrainingSessionTarget? target,
        string? extraTag = null)
    {
        List<string> tags = [eco, "guided-study"];
        if (specialMode is not null)
        {
            tags.Add(GetSpecialModeTag(specialMode));
        }

        if (target is not null)
        {
            tags.Add($"target:{target.Action}");
            tags.Add($"target-source:{target.SourceId}");
        }

        if (!string.IsNullOrWhiteSpace(extraTag))
        {
            tags.Add(extraTag);
        }

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<OpeningTrainingPosition> BuildWeakPositionsFromHistory(
        string? playerKey,
        OpeningLineCatalogItem item,
        OpeningTrainerOverview overview,
        IReadOnlyList<OpeningTrainingSessionResult>? sessionResults = null)
    {
        IReadOnlyList<OpeningTrainingSessionResult> results = sessionResults
            ?? historyStore?.ListOpeningTrainingSessionResults(playerKey, 100)
            ?? [];
        if (results.Count == 0)
        {
            return [];
        }

        return results
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
