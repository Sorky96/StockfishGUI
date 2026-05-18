using System.Globalization;

namespace MoveMentorChess.Training;

public sealed class OpeningTrainerService
{
    private const int TheoryExitThresholdCp = 70;
    private const int SignificantMistakeThresholdCp = 90;

    private static readonly HashSet<string> FallbackLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "opening_principles",
        "king_safety",
        "piece_activity",
        "material_loss"
    };

    private readonly IImportedGameStore importedGameStore;
    private readonly TrainingAnalysisDataSource analysisDataSource;
    private readonly OpeningTheoryQueryService? openingTheory;
    private readonly IOpeningTrainingHistoryStore? historyStore;
    private readonly IClock clock;

    public OpeningTrainerService(IAnalysisStore analysisStore)
        : this(analysisStore, SystemClock.Instance)
    {
    }

    public OpeningTrainerService(IAnalysisStore analysisStore, IClock clock)
        : this(
            analysisStore,
            new TrainingAnalysisDataSource(analysisStore, analysisStore),
            OpeningTheorySourceResolver.Create(analysisStore),
            analysisStore as IOpeningTrainingHistoryStore,
            clock)
    {
    }

    public OpeningTrainerService(IImportedGameStore importedGameStore)
        : this(importedGameStore, SystemClock.Instance)
    {
    }

    public OpeningTrainerService(IImportedGameStore importedGameStore, IClock clock)
        : this(
            importedGameStore,
            RequireResultStore(importedGameStore),
            RequireMoveAnalysisStore(importedGameStore),
            importedGameStore as IOpeningTheoryStore,
            importedGameStore as IOpeningTrainingHistoryStore,
            clock)
    {
    }

    public OpeningTrainerService(
        IImportedGameStore importedGameStore,
        IAnalysisResultStore resultStore,
        IStoredMoveAnalysisStore moveAnalysisStore,
        IOpeningTheoryStore? openingTheoryStore = null,
        IOpeningTrainingHistoryStore? historyStore = null)
        : this(importedGameStore, resultStore, moveAnalysisStore, openingTheoryStore, historyStore, SystemClock.Instance)
    {
    }

    public OpeningTrainerService(
        IImportedGameStore importedGameStore,
        IAnalysisResultStore resultStore,
        IStoredMoveAnalysisStore moveAnalysisStore,
        IOpeningTheoryStore? openingTheoryStore,
        IOpeningTrainingHistoryStore? historyStore,
        IClock clock)
        : this(
            importedGameStore,
            new TrainingAnalysisDataSource(moveAnalysisStore, resultStore),
            openingTheoryStore is null ? null : new OpeningTheoryQueryService(openingTheoryStore),
            historyStore,
            clock)
    {
    }

    internal OpeningTrainerService(
        IImportedGameStore importedGameStore,
        TrainingAnalysisDataSource analysisDataSource,
        IOpeningTheoryStore? openingTheoryStore = null,
        IOpeningTrainingHistoryStore? historyStore = null)
        : this(importedGameStore, analysisDataSource, openingTheoryStore, historyStore, SystemClock.Instance)
    {
    }

    internal OpeningTrainerService(
        IImportedGameStore importedGameStore,
        TrainingAnalysisDataSource analysisDataSource,
        IOpeningTheoryStore? openingTheoryStore,
        IOpeningTrainingHistoryStore? historyStore,
        IClock clock)
        : this(
            importedGameStore,
            analysisDataSource,
            openingTheoryStore is null ? null : new OpeningTheoryQueryService(openingTheoryStore),
            historyStore,
            clock)
    {
    }

    internal OpeningTrainerService(
        IImportedGameStore importedGameStore,
        TrainingAnalysisDataSource analysisDataSource,
        OpeningTheoryQueryService? openingTheory,
        IOpeningTrainingHistoryStore? historyStore = null)
        : this(importedGameStore, analysisDataSource, openingTheory, historyStore, SystemClock.Instance)
    {
    }

    internal OpeningTrainerService(
        IImportedGameStore importedGameStore,
        TrainingAnalysisDataSource analysisDataSource,
        OpeningTheoryQueryService? openingTheory,
        IOpeningTrainingHistoryStore? historyStore,
        IClock clock)
    {
        this.importedGameStore = importedGameStore ?? throw new ArgumentNullException(nameof(importedGameStore));
        this.analysisDataSource = analysisDataSource ?? throw new ArgumentNullException(nameof(analysisDataSource));
        this.openingTheory = openingTheory;
        this.historyStore = historyStore;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool TryBuildSession(string playerKeyOrName, out OpeningTrainingSession? session, OpeningTrainingSessionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(playerKeyOrName))
        {
            session = null;
            return false;
        }

        string normalizedPlayerKey = NormalizePlayerKey(playerKeyOrName);
        List<OpeningTrainerSnapshot> snapshots = LoadSnapshots(null, 2000)
            .Where(snapshot => snapshot.PlayerKey == normalizedPlayerKey
                || string.Equals(snapshot.DisplayName, playerKeyOrName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (snapshots.Count == 0)
        {
            session = null;
            return false;
        }

        OpeningTrainingSessionOptions effectiveOptions = NormalizeOptions(options);
        if (!new OpeningWeaknessService(analysisDataSource, openingTheory).TryBuildReport(playerKeyOrName, out OpeningWeaknessReport? weaknessReport)
            || weaknessReport is null)
        {
            session = null;
            return false;
        }

        string displayName = snapshots
            .GroupBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .First();
        IReadOnlyList<SavedOpeningReplay> savedReplays = LoadSavedOpeningReplays(snapshots);

        Dictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex = snapshots
            .ToDictionary(snapshot => new SnapshotKey(snapshot.GameFingerprint, snapshot.Side));
        Dictionary<string, OpeningTrainingLine> linesById = new(StringComparer.Ordinal);
        List<OpeningTrainingPosition> positions = [];

        foreach (OpeningTrainingSourceKind source in effectiveOptions.Sources!)
        {
            IReadOnlyList<OpeningTrainingPosition> built = source switch
            {
                OpeningTrainingSourceKind.ExampleGame => BuildExampleGamePositions(weaknessReport, snapshotIndex, linesById, effectiveOptions, openingTheory),
                OpeningTrainingSourceKind.OpeningWeakness => BuildOpeningWeaknessPositions(weaknessReport, snapshotIndex, savedReplays, linesById, effectiveOptions, openingTheory),
                OpeningTrainingSourceKind.FirstOpeningMistake => BuildFirstMistakePositions(snapshots, linesById, effectiveOptions, openingTheory),
                _ => []
            };

            positions.AddRange(built);
        }

        if (effectiveOptions.TargetOpenings is { Count: > 0 } targetOpenings)
        {
            HashSet<string> targetEco = targetOpenings
                .Select(NormalizeEco)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            positions = positions
                .Where(position => targetEco.Contains(NormalizeEco(position.Eco)))
                .ToList();
        }

        positions = positions
            .Where(position => effectiveOptions.Modes!.Contains(position.Mode))
            .OrderByDescending(position => position.Priority)
            .ThenBy(position => position.Ply)
            .ThenBy(position => position.OpeningName, StringComparer.OrdinalIgnoreCase)
            .Take(effectiveOptions.MaxPositions)
            .ToList();

        HashSet<string> usedLineIds = positions
            .Select(position => position.LineId)
            .Where(lineId => !string.IsNullOrWhiteSpace(lineId))
            .Select(lineId => lineId!)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<OpeningTrainingLine> lines = linesById.Values
            .Where(line => usedLineIds.Contains(line.LineId))
            .OrderBy(line => line.SourceKind)
            .ThenBy(line => line.OpeningName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.AnchorPly)
            .ToList();
        IReadOnlyList<OpeningTrainingSourceSummary> sourceSummaries = BuildSourceSummaries(positions, lines);

        DateTime createdUtc = clock.UtcNow;
        session = new OpeningTrainingSession(
            $"opening-trainer:{normalizedPlayerKey}:{createdUtc:yyyyMMddHHmmss}",
            normalizedPlayerKey,
            displayName,
            createdUtc,
            effectiveOptions.TrainingStyle,
            effectiveOptions.Strictness,
            effectiveOptions.SelectedSide,
            positions.Select(position => position.Mode).Distinct().ToList(),
            positions.Select(position => position.SourceKind).Distinct().ToList(),
            sourceSummaries,
            lines,
            positions);
        return positions.Count > 0;
    }

    public OpeningTrainingAttemptResult EvaluateMove(OpeningTrainingPosition position, string submittedMoveText)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.AnswerKind != OpeningTrainingAnswerKind.Move)
        {
            return EvaluateAnswer(position, submittedMoveText);
        }

        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences = GetPreferredReferences(position);
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences = GetPlayableReferences(position, preferredReferences);
        IReadOnlyList<OpeningTrainingMoveOption> expectedMoves = preferredReferences
            .Concat(playableReferences)
            .DistinctBy(option => BuildMoveKey(option.Uci, option.DisplayText))
            .ToList();

        if (!TryResolveMoveInput(position.Fen, submittedMoveText, out AppliedMoveInfo? resolvedMove, out string? error)
            || resolvedMove is null)
        {
            string invalidMoveExplanation = string.IsNullOrWhiteSpace(error)
                ? "The submitted move could not be matched to a legal move in this position."
                : error;

            return new OpeningTrainingAttemptResult(
                position.PositionId,
                position.Mode,
                position.SourceKind,
                OpeningTrainingAttemptStatus.Normal,
                submittedMoveText,
                null,
                null,
                expectedMoves,
                OpeningTrainingScore.Wrong,
                invalidMoveExplanation,
                [],
                preferredReferences,
                playableReferences);
        }

        OpeningPositionKey resolvedPositionKey = OpeningPositionKeyBuilder.BuildKey(resolvedMove.FenAfter);
        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences = position.CandidateMoves
            .Where(option => MovesMatch(option, resolvedMove))
            .ToList();
        IReadOnlyList<OpeningTrainingMoveOption> transposedReferences = position.CandidateMoves
            .Where(option => option.ToPositionKey.HasValue
                && option.ToPositionKey.Value.Equals(resolvedPositionKey))
            .ToList();
        if (matchingReferences.Count == 0 && transposedReferences.Count > 0)
        {
            matchingReferences = transposedReferences;
        }

        bool matchedPreferred = preferredReferences.Any(reference => MovesMatch(reference, resolvedMove))
            || matchingReferences.Any(match =>
                preferredReferences.Any(reference => MoveOptionsMatch(reference, match)));
        bool matchedPlayable = playableReferences.Any(reference => MovesMatch(reference, resolvedMove))
            || matchingReferences.Any(match =>
                playableReferences.Any(reference => MoveOptionsMatch(reference, match)));
        bool transposedToKnownPosition = transposedReferences.Count > 0 && !position.CandidateMoves.Any(option => MovesMatch(option, resolvedMove));
        OpeningTrainingScore score = DetermineScore(position.Strictness, matchedPreferred, matchedPlayable, transposedToKnownPosition);
        OpeningTrainingAttemptStatus status = transposedToKnownPosition
            ? OpeningTrainingAttemptStatus.TransposedToKnownPosition
            : OpeningTrainingAttemptStatus.Normal;
        OpeningMoveIdea? whyThisMove = matchingReferences
            .Select(option => option.Idea)
            .FirstOrDefault(idea => idea is not null)
            ?? preferredReferences.Select(option => option.Idea).FirstOrDefault(idea => idea is not null);
        string explanation = BuildAttemptExplanation(position, score, matchingReferences, preferredReferences, playableReferences);
        if (status == OpeningTrainingAttemptStatus.TransposedToKnownPosition)
        {
            explanation = $"{explanation} The move reached a known theory position by transposition.";
        }

        return new OpeningTrainingAttemptResult(
            position.PositionId,
            position.Mode,
            position.SourceKind,
            status,
            submittedMoveText,
            resolvedMove.San,
            resolvedMove.Uci,
            expectedMoves,
            score,
            explanation,
            matchingReferences,
            preferredReferences,
            playableReferences,
            new OpeningPositionIdentity(
                resolvedPositionKey,
                resolvedMove.FenAfter,
                OpeningPositionKeyBuilder.NormalizeFen(resolvedMove.FenAfter),
                position.Ply + 1,
                position.MoveNumber,
                Opponent(position.SideToMove),
                status == OpeningTrainingAttemptStatus.TransposedToKnownPosition),
            whyThisMove);
    }

    public OpeningTrainingAttemptResult EvaluateAnswer(OpeningTrainingPosition position, string answerId)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (position.AnswerKind == OpeningTrainingAnswerKind.Move)
        {
            return EvaluateMove(position, answerId);
        }

        OpeningTrainingAnswerOption? selected = position.AnswerOptions?
            .FirstOrDefault(option => string.Equals(option.Id, answerId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Text, answerId, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<OpeningTrainingMoveOption> expected = position.AnswerOptions?
            .Where(option => option.IsCorrect)
            .Select(option => new OpeningTrainingMoveOption(
                option.Text,
                option.Id,
                OpeningTrainingMoveRole.Expected,
                true,
                option.Explanation,
                OpeningLineRecallReferenceKind.ReferenceLine,
                OpeningTrainingMoveSourceKind.OpeningBook,
                null,
                null))
            .ToList()
            ?? [];

        if (selected is null)
        {
            return new OpeningTrainingAttemptResult(
                position.PositionId,
                position.Mode,
                position.SourceKind,
                OpeningTrainingAttemptStatus.Normal,
                answerId,
                null,
                answerId,
                expected,
                OpeningTrainingScore.Wrong,
                "The selected answer option is not available for this position.",
                [],
                expected,
                []);
        }

        OpeningTrainingScore score = selected.IsCorrect
            ? OpeningTrainingScore.Correct
            : OpeningTrainingScore.Wrong;
        string explanation = selected.Explanation
            ?? (selected.IsCorrect ? "Correct plan." : "That plan does not fit this position.");
        OpeningTrainingMoveOption selectedReference = new(
            selected.Text,
            selected.Id,
            OpeningTrainingMoveRole.Expected,
            selected.IsCorrect,
            selected.Explanation,
            OpeningLineRecallReferenceKind.ReferenceLine,
            OpeningTrainingMoveSourceKind.OpeningBook,
            null,
            null);

        return new OpeningTrainingAttemptResult(
            position.PositionId,
            position.Mode,
            position.SourceKind,
            OpeningTrainingAttemptStatus.Normal,
            selected.Text,
            selected.Text,
            selected.Id,
            expected,
            score,
            explanation,
            selected.IsCorrect ? [selectedReference] : [],
            expected,
            []);
    }

    public OpeningLineRecallAttemptResult EvaluateLineRecallMove(OpeningTrainingPosition position, string submittedMoveText)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Mode != OpeningTrainingMode.LineRecall)
        {
            throw new ArgumentException("Line recall evaluation is available only for line recall positions.", nameof(position));
        }

        OpeningTrainingAttemptResult result = EvaluateMove(position, submittedMoveText);

        return new OpeningLineRecallAttemptResult(
            result.PositionId,
            result.SubmittedMoveText,
            result.ResolvedSan,
            result.ResolvedUci,
            ToLineRecallGrade(result.Score),
            result.ShortExplanation,
            result.MatchingReferences,
            result.PreferredReferences,
            result.PlayableReferences);
    }

    public OpeningMistakeRepairAttemptResult EvaluateMistakeRepairMove(OpeningTrainingPosition position, string submittedMoveText)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Mode != OpeningTrainingMode.MistakeRepair)
        {
            throw new ArgumentException("Mistake repair evaluation is available only for mistake repair positions.", nameof(position));
        }

        OpeningTrainingAttemptResult result = EvaluateMove(position, submittedMoveText);
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences = result.PreferredReferences;
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences = result.PlayableReferences;
        string betterMoveSummary = BuildBetterMoveSummary(position, preferredReferences);
        string whyBetter = BuildWhyBetterSummary(position, preferredReferences, playableReferences);

        return new OpeningMistakeRepairAttemptResult(
            result.PositionId,
            result.SubmittedMoveText,
            result.ResolvedSan,
            result.ResolvedUci,
            ToMistakeRepairGrade(result.Score),
            result.ShortExplanation,
            betterMoveSummary,
            whyBetter,
            result.MatchingReferences,
            result.PreferredReferences,
            result.PlayableReferences);
    }

    public OpeningTrainingSessionResult BuildSessionResult(
        OpeningTrainingSession session,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts,
        OpeningTrainingSessionOutcome outcome = OpeningTrainingSessionOutcome.Completed,
        DateTime? completedUtc = null,
        string? startSource = null,
        string? recommendationId = null,
        int hintCount = 0,
        int? timeToFirstMoveSeconds = null,
        DateTime? abandonedUtc = null,
        IReadOnlyList<string>? completedNextActionIds = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attempts);

        Dictionary<string, OpeningTrainingPosition> positionsById = session.Positions
            .ToDictionary(position => position.PositionId, StringComparer.Ordinal);
        DateTime finishedUtc = completedUtc?.ToUniversalTime() ?? clock.UtcNow;
        IReadOnlyList<OpeningTrainingRecordedAttempt> recordedAttempts = attempts
            .Select(attempt =>
            {
                positionsById.TryGetValue(attempt.PositionId, out OpeningTrainingPosition? position);
                return new OpeningTrainingRecordedAttempt(
                    attempt.PositionId,
                    attempt.Mode,
                    attempt.PositionSource,
                    attempt.Status,
                    position?.Eco ?? string.Empty,
                    position?.OpeningName ?? string.Empty,
                    position?.ThemeLabel,
                    attempt.SubmittedMoveText,
                    attempt.ResolvedSan,
                    attempt.ResolvedUci,
                    attempt.Score,
                    finishedUtc,
                    position?.OpeningBranchKey,
                    position?.OpeningPositionKey,
                    position?.OpeningKey,
                    position?.OpeningLineKey);
            })
            .ToList();
        IReadOnlyList<OpeningReviewItem> reviewItems = BuildReviewItems(recordedAttempts, finishedUtc);

        return new OpeningTrainingSessionResult(
            session.SessionId,
            session.PlayerKey,
            session.DisplayName,
            session.CreatedUtc,
            finishedUtc,
            outcome,
            session.TrainingStyle,
            session.Strictness,
            session.Positions.Count,
            recordedAttempts.Count,
            recordedAttempts.Count(attempt => attempt.Score == OpeningTrainingScore.Correct),
            recordedAttempts.Count(attempt => attempt.Score == OpeningTrainingScore.Playable),
            recordedAttempts.Count(attempt => attempt.Score == OpeningTrainingScore.Wrong),
            session.Positions
                .Select(position => position.Eco)
                .Concat(recordedAttempts.Select(attempt => attempt.Eco))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            session.Positions
                .Select(position => position.ThemeLabel)
                .Concat(recordedAttempts.Select(attempt => attempt.ThemeLabel))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            recordedAttempts,
            reviewItems,
            startSource,
            recommendationId,
            hintCount,
            timeToFirstMoveSeconds,
            abandonedUtc?.ToUniversalTime(),
            completedNextActionIds);
    }

    public OpeningTrainingSessionResult SaveSessionResult(
        OpeningTrainingSession session,
        IReadOnlyList<OpeningTrainingAttemptResult> attempts,
        OpeningTrainingSessionOutcome outcome = OpeningTrainingSessionOutcome.Completed,
        DateTime? completedUtc = null,
        string? startSource = null,
        string? recommendationId = null,
        int hintCount = 0,
        int? timeToFirstMoveSeconds = null,
        DateTime? abandonedUtc = null,
        IReadOnlyList<string>? completedNextActionIds = null)
    {
        OpeningTrainingSessionResult result = BuildSessionResult(
            session,
            attempts,
            outcome,
            completedUtc,
            startSource,
            recommendationId,
            hintCount,
            timeToFirstMoveSeconds,
            abandonedUtc,
            completedNextActionIds);
        if (historyStore is null)
        {
            throw new InvalidOperationException("The analysis store does not support opening training history.");
        }

        historyStore.SaveOpeningTrainingSessionResult(result);
        if (result.ReviewItems is { Count: > 0 })
        {
            historyStore.SaveOpeningReviewItems(session.PlayerKey, result.ReviewItems);
        }

        return result;
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> GetPreferredReferences(OpeningTrainingPosition position)
    {
        if (position.Mode != OpeningTrainingMode.BranchAwareness)
        {
            return position.CandidateMoves
                .Where(option => option.IsPreferred)
                .ToList();
        }

        OpeningTrainingBranch? primaryBranch = position.Branches?
            .OrderByDescending(branch => branch.Frequency)
            .ThenBy(branch => branch.OpponentMove, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (primaryBranch is null)
        {
            return [];
        }

        OpeningTrainingMoveOption? primaryOption = position.CandidateMoves.FirstOrDefault(option =>
            option.Role == OpeningTrainingMoveRole.Alternative
            && MoveOptionMatchesTextAndUci(option, primaryBranch.OpponentMove, primaryBranch.OpponentMoveUci));

        return primaryOption is null ? [] : [primaryOption];
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> GetPlayableReferences(
        OpeningTrainingPosition position,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences)
    {
        IEnumerable<OpeningTrainingMoveOption> playable = position.Mode switch
        {
            OpeningTrainingMode.MistakeRepair => position.CandidateMoves
                .Where(option => !option.IsPreferred && option.Role == OpeningTrainingMoveRole.Repair),
            OpeningTrainingMode.BranchAwareness => position.CandidateMoves
                .Where(option => option.Role == OpeningTrainingMoveRole.Alternative),
            _ => position.CandidateMoves.Where(option => !option.IsPreferred)
        };

        return playable
            .Where(option => !preferredReferences.Any(reference => MoveOptionsMatch(reference, option)))
            .ToList();
    }

    private static string BuildAttemptExplanation(
        OpeningTrainingPosition position,
        OpeningTrainingScore score,
        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences,
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences)
    {
        return position.Mode switch
        {
            OpeningTrainingMode.MistakeRepair => BuildMistakeRepairAttemptExplanation(position, score, preferredReferences, playableReferences),
            OpeningTrainingMode.BranchAwareness => BuildBranchAwarenessAttemptExplanation(score, matchingReferences, preferredReferences, playableReferences),
            _ => BuildLineRecallAttemptExplanation(score, matchingReferences, preferredReferences)
        };
    }

    private static string BuildLineRecallAttemptExplanation(
        OpeningTrainingScore score,
        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences)
    {
        return score switch
        {
            OpeningTrainingScore.Correct => $"Accepted as correct. The move matches a preferred imported-theory reference: {FormatMatchedReferences(matchingReferences.Where(option => option.IsPreferred).ToList())}.",
            OpeningTrainingScore.Playable => $"Accepted as playable. The move appears in imported opening theory, but it is not the strongest preferred continuation here: {FormatMatchedReferences(matchingReferences)}.",
            _ => preferredReferences.Count == 0
                ? "Marked as wrong because the move does not match any imported-theory move for this position."
                : $"Marked as wrong because the move does not match the preferred imported-theory references: {FormatMatchedReferences(preferredReferences)}."
        };
    }

    private static string BuildMistakeRepairAttemptExplanation(
        OpeningTrainingPosition position,
        OpeningTrainingScore score,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences,
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences)
    {
        string betterMoveSummary = BuildBetterMoveSummary(position, preferredReferences);
        string whyBetter = BuildWhyBetterSummary(position, preferredReferences, playableReferences);

        return score switch
        {
            OpeningTrainingScore.Correct => $"Correct repair. {betterMoveSummary} {whyBetter}",
            OpeningTrainingScore.Playable => $"Playable repair. {betterMoveSummary} {whyBetter}",
            _ => $"Wrong repair. {betterMoveSummary} {whyBetter}"
        };
    }

    private static string BuildBranchAwarenessAttemptExplanation(
        OpeningTrainingScore score,
        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences,
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences)
    {
        return score switch
        {
            OpeningTrainingScore.Correct => $"Correct branch. This is the highest-priority imported-theory opponent reply: {FormatMatchedReferences(preferredReferences)}.",
            OpeningTrainingScore.Playable => $"Playable branch. This opponent reply appears in imported theory: {FormatMatchedReferences(matchingReferences)}. Primary branch: {FormatMatchedReferences(preferredReferences)}.",
            _ => preferredReferences.Count == 0 && playableReferences.Count == 0
                ? "Marked as wrong because no imported-theory opponent branches are available for this position."
                : $"Marked as wrong because the move does not match the imported-theory opponent branches: {FormatMatchedReferences(preferredReferences.Concat(playableReferences).ToList())}."
        };
    }

    private static OpeningLineRecallGrade ToLineRecallGrade(OpeningTrainingScore score)
    {
        return score switch
        {
            OpeningTrainingScore.Correct => OpeningLineRecallGrade.Correct,
            OpeningTrainingScore.Playable => OpeningLineRecallGrade.Playable,
            _ => OpeningLineRecallGrade.Wrong
        };
    }

    private static OpeningMistakeRepairGrade ToMistakeRepairGrade(OpeningTrainingScore score)
    {
        return score switch
        {
            OpeningTrainingScore.Correct => OpeningMistakeRepairGrade.Correct,
            OpeningTrainingScore.Playable => OpeningMistakeRepairGrade.Playable,
            _ => OpeningMistakeRepairGrade.Wrong
        };
    }

    private static OpeningTrainingSessionOptions NormalizeOptions(OpeningTrainingSessionOptions? options)
    {
        IReadOnlyList<OpeningTrainingMode> modes = (options?.Modes is { Count: > 0 } ? options.Modes : Enum.GetValues<OpeningTrainingMode>())
            .Distinct()
            .ToList();
        IReadOnlyList<OpeningTrainingSourceKind> sources = (options?.Sources is { Count: > 0 } ? options.Sources : Enum.GetValues<OpeningTrainingSourceKind>())
            .Distinct()
            .ToList();

        return new OpeningTrainingSessionOptions(
            modes,
            sources,
            Math.Max(1, options?.MaxPositions ?? 18),
            Math.Max(1, options?.MaxPositionsPerSource ?? 6),
            Math.Clamp(options?.MaxContinuationMoves ?? 6, 1, 12),
            options?.TargetOpenings?
                .Where(opening => !string.IsNullOrWhiteSpace(opening))
                .Select(NormalizeEco)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            options?.SelectedOpeningKeys,
            options?.SelectedLineKeys,
            options?.SelectedSide ?? RepertoireSide.Both,
            options?.TrainingStyle ?? OpeningTrainingStyle.Mixed,
            options?.Strictness ?? OpeningTrainingStrictness.BookFlexible,
            Math.Max(2, options?.MaxDepth ?? 12),
            options?.IncludeSideVariations ?? true,
            options?.PrioritizeOpponentFrequency ?? false,
            options?.IncludeTranspositions ?? true,
            options?.SpecialMode,
            options?.TimeLimitMinutes);
    }

    private List<OpeningTrainerSnapshot> LoadSnapshots(string? filterText, int limit)
    {
        TrainingAnalysisDataSet dataSet = analysisDataSource.Load(filterText, limit);

        List<OpeningTrainerSnapshot> mergedSnapshots = BuildSnapshotsFromMoves(dataSet.StoredMoves);
        mergedSnapshots.AddRange(BuildSnapshotsFromResults(dataSet.Results));

        return mergedSnapshots
            .GroupBy(snapshot => new SnapshotSelectionKey(snapshot.GameFingerprint, snapshot.Side))
            .Select(group => group
                .OrderByDescending(snapshot => snapshot.AnalysisUpdatedUtc)
                .ThenByDescending(snapshot => snapshot.Depth)
                .ThenByDescending(snapshot => snapshot.MultiPv)
                .ThenByDescending(snapshot => snapshot.MoveTimeMs ?? -1)
                .First())
            .Take(limit)
            .ToList();
    }

    private static IAnalysisResultStore RequireResultStore(object store)
    {
        return store as IAnalysisResultStore
            ?? throw new ArgumentException("The store must also provide analysis results.", nameof(store));
    }

    private static IStoredMoveAnalysisStore RequireMoveAnalysisStore(object store)
    {
        return store as IStoredMoveAnalysisStore
            ?? throw new ArgumentException("The store must also provide stored move analyses.", nameof(store));
    }

    private static List<OpeningTrainerSnapshot> BuildSnapshotsFromMoves(IReadOnlyList<StoredMoveAnalysis> storedMoves)
    {
        return storedMoves
            .GroupBy(move => new AnalysisVariantKey(
                move.Game.GameFingerprint,
                move.Analysis.AnalyzedSide,
                move.Analysis.Depth,
                move.Analysis.MultiPv,
                move.Analysis.MoveTimeMs))
            .Select(CreateSnapshotFromMoves)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static List<OpeningTrainerSnapshot> BuildSnapshotsFromResults(IReadOnlyList<GameAnalysisResult> results)
    {
        return results
            .Select(CreateSnapshotFromResult)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static OpeningTrainerSnapshot? CreateSnapshotFromMoves(IGrouping<AnalysisVariantKey, StoredMoveAnalysis> group)
    {
        List<StoredMoveAnalysis> openingMoves = group
            .Where(move => move.Move.Phase == GamePhase.Opening)
            .OrderBy(move => move.Move.Ply)
            .ToList();
        if (openingMoves.Count == 0)
        {
            return null;
        }

        StoredMoveAnalysis first = openingMoves[0];
        StoredGameContext game = first.Game;
        StoredAnalysisRunContext analysis = first.Analysis;
        string? playerName = analysis.AnalyzedSide == PlayerSide.White
            ? game.WhitePlayer
            : game.BlackPlayer;
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        return new OpeningTrainerSnapshot(
            game.GameFingerprint,
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            analysis.AnalyzedSide,
            GetOpponentName(first),
            game.DateText,
            game.Result,
            NormalizeEco(game.Eco),
            OpeningCatalog.GetName(game.Eco),
            analysis.Depth,
            analysis.MultiPv,
            analysis.MoveTimeMs,
            analysis.AnalysisUpdatedUtc,
            openingMoves);
    }

    private static OpeningTrainerSnapshot? CreateSnapshotFromResult(GameAnalysisResult result)
    {
        string? playerName = result.AnalyzedSide == PlayerSide.White
            ? result.Game.WhitePlayer
            : result.Game.BlackPlayer;
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        IReadOnlyList<StoredMoveAnalysis> openingMoves = StoredMoveAnalysisMapper
            .FromAnalysisResult(
                new GameAnalysisCacheKey(GameFingerprint.Compute(result.Game.PgnText), result.AnalyzedSide, 0, 0, null),
                result,
                DateTime.MinValue)
            .Where(move => move.Move.Phase == GamePhase.Opening)
            .OrderBy(move => move.Move.Ply)
            .ToList();

        if (openingMoves.Count == 0)
        {
            return null;
        }

        return new OpeningTrainerSnapshot(
            GameFingerprint.Compute(result.Game.PgnText),
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            result.AnalyzedSide,
            GetOpponentName(result.Game, result.AnalyzedSide),
            result.Game.DateText,
            result.Game.Result,
            NormalizeEco(result.Game.Eco),
            OpeningCatalog.GetName(result.Game.Eco),
            0,
            0,
            null,
            DateTime.MinValue,
            openingMoves);
    }

    private static IReadOnlyList<OpeningTrainingPosition> BuildExampleGamePositions(
        OpeningWeaknessReport weaknessReport,
        IReadOnlyDictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex,
        IDictionary<string, OpeningTrainingLine> linesById,
        OpeningTrainingSessionOptions options,
        OpeningTheoryQueryService? openingTheory)
    {
        List<(OpeningWeaknessEntry Entry, OpeningExampleGame Example)> examples = weaknessReport.WeakOpenings
            .SelectMany(entry => entry.ExampleGames.Select(example => (Entry: entry, Example: example)))
            .OrderByDescending(item => item.Example.FirstMistakeCentipawnLoss ?? 0)
            .ThenBy(item => item.Example.FirstMistakePly ?? int.MaxValue)
            .Take(options.MaxPositionsPerSource)
            .ToList();
        List<OpeningTrainingPosition> positions = [];

        foreach ((OpeningWeaknessEntry entry, OpeningExampleGame example) in examples)
        {
            SnapshotKey key = new(example.GameFingerprint, example.Side);
            if (!snapshotIndex.TryGetValue(key, out OpeningTrainerSnapshot? snapshot))
            {
                continue;
            }

            OpeningIssue? firstIssue = FindFirstIssue(snapshot, example.FirstMistakePly);
            StoredMoveAnalysis anchorMove = snapshot.OpeningMoves
                .Where(move => move.Ply < (firstIssue?.Move.Ply ?? int.MaxValue) && !IsOpeningIssue(move))
                .LastOrDefault()
                ?? snapshot.OpeningMoves.Where(move => move.Ply < (firstIssue?.Move.Ply ?? int.MaxValue)).LastOrDefault()
                ?? snapshot.OpeningMoves.First();

            IReadOnlyList<StoredMoveAnalysis> lineMoves = snapshot.OpeningMoves
                .Where(move => move.Ply >= anchorMove.Ply)
                .Take(options.MaxContinuationMoves)
                .ToList();
            string lineId = BuildLineId(OpeningTrainingSourceKind.ExampleGame, snapshot.GameFingerprint, anchorMove.Ply);
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = BuildLineRecallOptions(anchorMove, openingTheory);
            if (!candidateMoves.Any(option => option.IsPreferred))
            {
                continue;
            }

            linesById[lineId] = CreateLine(
                lineId,
                OpeningTrainingSourceKind.ExampleGame,
                snapshot,
                anchorMove,
                "Recall the stable line from imported opening theory around this example game.",
                lineMoves,
                firstIssue);

            positions.Add(new OpeningTrainingPosition(
                $"example:{snapshot.GameFingerprint}:{anchorMove.Ply}",
                BuildOpeningKey(snapshot.Eco, snapshot.OpeningName),
                BuildOpeningLineKey(snapshot.Eco, snapshot.OpeningName, lineId),
                null,
                OpeningPositionKeyBuilder.BuildKey(anchorMove.FenBefore),
                OpeningTrainingMode.LineRecall,
                OpeningTrainingSourceKind.ExampleGame,
                snapshot.Eco,
                OpeningCatalog.Describe(snapshot.Eco),
                anchorMove.FenBefore,
                anchorMove.Ply,
                anchorMove.MoveNumber,
                snapshot.Side,
                "Recall the move that imported opening theory recommends in this example position.",
                "Use the imported opening book as the source of truth, then replay the surrounding example line for context.",
                (example.FirstMistakeCentipawnLoss ?? 0) + 25,
                ToRepertoireSide(snapshot.Side),
                options.Strictness,
                firstIssue?.Move.MistakeLabel,
                anchorMove.San,
                GetPreferredTheoryMoveDisplay(candidateMoves),
                firstIssue is null ? null : BuildRepairReason(firstIssue.Move),
                BuildTags(snapshot.Eco, firstIssue?.Move.MistakeLabel, "example-game", "line-recall"),
                candidateMoves,
                lineMoves.Select((move, index) => ToTrainingMove(move, index == 0 ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Continuation, index == 0)).ToList(),
                CreateReference(snapshot, "Example game", firstIssue),
                lineId));
        }

        return positions;
    }

    private static IReadOnlyList<OpeningTrainingPosition> BuildOpeningWeaknessPositions(
        OpeningWeaknessReport weaknessReport,
        IReadOnlyDictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex,
        IReadOnlyList<SavedOpeningReplay> savedReplays,
        IDictionary<string, OpeningTrainingLine> linesById,
        OpeningTrainingSessionOptions options,
        OpeningTheoryQueryService? openingTheory)
    {
        List<(OpeningWeaknessEntry Entry, BranchRoot root)> roots = weaknessReport.WeakOpenings
            .SelectMany(entry => BuildBranchRoots(entry, snapshotIndex, savedReplays))
            .OrderByDescending(item => item.root.Priority)
            .ThenBy(item => item.root.AnchorPly)
            .Take(options.MaxPositionsPerSource)
            .ToList();
        List<OpeningTrainingPosition> positions = [];

        foreach ((OpeningWeaknessEntry entry, BranchRoot root) in roots)
        {
            IReadOnlyList<OpeningTrainingBranch> theoryBranches = BuildTheoryBranches(root.RootFen, openingTheory);
            if (theoryBranches.Count == 0)
            {
                continue;
            }

            OpeningTrainingMoveOption? primaryRecommendedResponse = theoryBranches
                .Select(branch => branch.RecommendedResponse)
                .FirstOrDefault(option => option is not null);
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = theoryBranches
                .Select(branch => new OpeningTrainingMoveOption(
                    branch.OpponentMove,
                    branch.OpponentMoveUci,
                    OpeningTrainingMoveRole.Alternative,
                    false,
                    branch.SourceSummary,
                    OpeningLineRecallReferenceKind.ReferenceLine,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    branch.RecommendedResponse?.Idea,
                    branch.ResultingPositionKey))
                .ToList();
            string branchSelectionSummary = BuildTheoryBranchSelectionSummary(theoryBranches);
            IReadOnlyList<OpeningTrainingMove> primaryContinuation = theoryBranches[0].Continuation;
            OpeningIssue? issue = root.FirstIssue;
            string lineId = BuildLineId(OpeningTrainingSourceKind.OpeningWeakness, root.Snapshot.GameFingerprint, root.AnchorPly);
            linesById[lineId] = CreateLine(
                lineId,
                OpeningTrainingSourceKind.OpeningWeakness,
                root.Snapshot,
                root.AnchorMove,
                "Review the opponent branches that show up most often after your chosen setup move.",
                root.SampleLine,
                issue);

            positions.Add(new OpeningTrainingPosition(
                $"weakness:{root.Snapshot.GameFingerprint}:{root.AnchorPly}",
                BuildOpeningKey(entry.Eco, entry.OpeningName),
                BuildOpeningLineKey(entry.Eco, entry.OpeningName, lineId),
                null,
                OpeningPositionKeyBuilder.BuildKey(root.RootFen),
                OpeningTrainingMode.BranchAwareness,
                OpeningTrainingSourceKind.OpeningWeakness,
                entry.Eco,
                entry.OpeningDisplayName,
                root.RootFen,
                root.AnchorPly + 1,
                root.AnchorMove.MoveNumber,
                Opponent(root.Snapshot.Side),
                "Review the typical opponent replies from imported opening theory and keep one theory-backed reaction ready.",
                "Use the imported opening tree as the source of truth for the opponent branches in this position.",
                root.Priority,
                ToRepertoireSide(root.Snapshot.Side),
                options.Strictness,
                root.ThemeLabel,
                root.AnchorMove.San,
                primaryRecommendedResponse?.DisplayText,
                primaryRecommendedResponse?.Note,
                BuildTags(entry.Eco, root.ThemeLabel, "opening-weakness", "branch-awareness"),
                candidateMoves,
                primaryContinuation,
                CreateReference(root.Snapshot, "Opening weakness", issue),
                lineId,
                theoryBranches,
                branchSelectionSummary,
                BuildCoverageSummary(theoryBranches),
                BuildOpponentReplyProfile(BuildOpeningLineKey(entry.Eco, entry.OpeningName, lineId), ToRepertoireSide(root.Snapshot.Side), theoryBranches)));
        }

        return positions;
    }

    private static IReadOnlyList<OpeningTrainingPosition> BuildFirstMistakePositions(
        IReadOnlyList<OpeningTrainerSnapshot> snapshots,
        IDictionary<string, OpeningTrainingLine> linesById,
        OpeningTrainingSessionOptions options,
        OpeningTheoryQueryService? openingTheory)
    {
        List<(OpeningTrainerSnapshot Snapshot, OpeningIssue Issue)> issues = snapshots
            .Select(snapshot => (Snapshot: snapshot, Issue: FindFirstIssue(snapshot, null)))
            .Where(item => item.Issue is not null)
            .Select(item => (Snapshot: item.Snapshot, Issue: item.Issue!))
            .OrderByDescending(item => item.Issue.Move.CentipawnLoss ?? 0)
            .ThenBy(item => item.Issue.Move.Ply)
            .Take(options.MaxPositionsPerSource)
            .ToList();
        List<OpeningTrainingPosition> positions = [];

        foreach ((OpeningTrainerSnapshot snapshot, OpeningIssue issue) in issues)
        {
            string lineId = BuildLineId(OpeningTrainingSourceKind.FirstOpeningMistake, snapshot.GameFingerprint, issue.Move.Ply);
            IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = BuildRepairOptions(issue.Move, openingTheory);
            if (!candidateMoves.Any(option => option.Role == OpeningTrainingMoveRole.Repair))
            {
                continue;
            }

            linesById[lineId] = CreateLine(
                lineId,
                OpeningTrainingSourceKind.FirstOpeningMistake,
                snapshot,
                issue.Move,
                "Repair the first opening mistake from this game.",
                snapshot.OpeningMoves.Where(move => move.Ply >= Math.Max(1, issue.Move.Ply - 1)).Take(options.MaxContinuationMoves).ToList(),
                issue);

            positions.Add(new OpeningTrainingPosition(
                $"first-mistake:{snapshot.GameFingerprint}:{issue.Move.Ply}",
                BuildOpeningKey(snapshot.Eco, snapshot.OpeningName),
                BuildOpeningLineKey(snapshot.Eco, snapshot.OpeningName, lineId),
                null,
                OpeningPositionKeyBuilder.BuildKey(issue.Move.FenBefore),
                OpeningTrainingMode.MistakeRepair,
                OpeningTrainingSourceKind.FirstOpeningMistake,
                snapshot.Eco,
                OpeningCatalog.Describe(snapshot.Eco),
                issue.Move.FenBefore,
                issue.Move.Ply,
                issue.Move.MoveNumber,
                snapshot.Side,
                "Repair the first opening mistake from this game before it repeats again.",
                "Replace the played move with the stronger repair move from imported opening theory and use the label as the study theme.",
                (issue.Move.CentipawnLoss ?? 0) + 100,
                ToRepertoireSide(snapshot.Side),
                options.Strictness,
                issue.Move.MistakeLabel,
                issue.Move.San,
                GetPreferredTheoryMoveDisplay(candidateMoves),
                BuildRepairReason(issue.Move),
                BuildTags(snapshot.Eco, issue.Move.MistakeLabel, "first-opening-mistake", "mistake-repair"),
                candidateMoves,
                [ToTrainingMove(issue.Move, OpeningTrainingMoveRole.Alternative, false)],
                CreateReference(snapshot, "First opening mistake", issue),
                lineId));
        }

        return positions;
    }

    private static OpeningTrainingLine CreateLine(
        string lineId,
        OpeningTrainingSourceKind sourceKind,
        OpeningTrainerSnapshot snapshot,
        StoredMoveAnalysis anchorMove,
        string anchorLabel,
        IReadOnlyList<StoredMoveAnalysis> lineMoves,
        OpeningIssue? issue)
    {
        return new OpeningTrainingLine(
            lineId,
            BuildOpeningLineKey(snapshot.Eco, snapshot.OpeningName, lineId),
            BuildOpeningKey(snapshot.Eco, snapshot.OpeningName),
            sourceKind,
            snapshot.Eco,
            OpeningCatalog.Describe(snapshot.Eco),
            anchorMove.FenBefore,
            OpeningPositionKeyBuilder.BuildKey(anchorMove.FenBefore),
            anchorMove.Ply,
            anchorMove.MoveNumber,
            snapshot.Side,
            anchorLabel,
            lineMoves.Select((move, index) => ToTrainingMove(move, index == 0 ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Continuation, index == 0)).ToList(),
            CreateReference(snapshot, sourceKind.ToString(), issue),
            ToRepertoireSide(snapshot.Side));
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> BuildLineRecallOptions(
        StoredMoveAnalysis anchorMove,
        OpeningTheoryQueryService? openingTheory)
    {
        IReadOnlyList<OpeningTheoryMove> theoryMoves = GetTheoryMoves(openingTheory, anchorMove.FenBefore);
        if (theoryMoves.Count == 0)
        {
            return [];
        }

        Dictionary<string, OpeningTrainingMoveOption> options = new(StringComparer.OrdinalIgnoreCase);
        bool hasMainMove = theoryMoves.Any(move => move.IsMainMove);

        foreach ((OpeningTheoryMove move, int index) in theoryMoves.Select((move, index) => (move, index)))
        {
            bool isPreferred = move.IsMainMove || (!hasMainMove && index == 0);
            AddOrUpgradeOption(
                options,
                new OpeningTrainingMoveOption(
                    move.MoveSan,
                    move.MoveUci,
                    isPreferred ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Alternative,
                    isPreferred,
                    isPreferred
                        ? "Main move from imported opening theory"
                        : "Playable move from imported opening theory",
                    isPreferred
                        ? OpeningLineRecallReferenceKind.ReferenceLine
                        : OpeningLineRecallReferenceKind.BetterMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    move.Idea,
                    move.ToOpeningPositionKey));
        }

        return options.Values
            .OrderByDescending(option => option.IsPreferred)
            .ThenBy(option => option.Role)
            .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddOrUpgradeOption(
        IDictionary<string, OpeningTrainingMoveOption> options,
        OpeningTrainingMoveOption candidate)
    {
        string key = BuildMoveKey(candidate.Uci, candidate.DisplayText);
        if (!options.TryGetValue(key, out OpeningTrainingMoveOption? existing))
        {
            options[key] = candidate;
            return;
        }

        options[key] = new OpeningTrainingMoveOption(
            existing.DisplayText.Length >= candidate.DisplayText.Length ? existing.DisplayText : candidate.DisplayText,
            existing.Uci ?? candidate.Uci,
            existing.Role <= candidate.Role ? existing.Role : candidate.Role,
            existing.IsPreferred || candidate.IsPreferred,
            existing.Note ?? candidate.Note,
            existing.ReferenceKind ?? candidate.ReferenceKind,
            existing.SourceKind == OpeningTrainingMoveSourceKind.UserGame
                ? candidate.SourceKind
                : existing.SourceKind,
            existing.Idea ?? candidate.Idea,
            existing.ToPositionKey ?? candidate.ToPositionKey);
    }

    private static OpeningTrainingMove ToTrainingMove(StoredMoveAnalysis move, OpeningTrainingMoveRole role, bool isPreferred)
    {
        StoredMoveContext played = move.Move;

        return new OpeningTrainingMove(
            played.Ply,
            played.MoveNumber,
            move.Analysis.AnalyzedSide,
            played.San,
            played.Uci,
            role,
            isPreferred,
            move.Advice.MistakeLabel);
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> BuildRepairOptions(
        StoredMoveAnalysis move,
        OpeningTheoryQueryService? openingTheory)
    {
        StoredMoveContext played = move.Move;
        IReadOnlyList<OpeningTheoryMove> theoryMoves = GetTheoryMoves(openingTheory, played.FenBefore);
        if (theoryMoves.Count == 0)
        {
            return [];
        }

        Dictionary<string, OpeningTrainingMoveOption> options = new(StringComparer.OrdinalIgnoreCase)
        {
            [BuildMoveKey(played.Uci, played.San)] = new(
                played.San,
                played.Uci,
                OpeningTrainingMoveRole.Alternative,
                false,
                "Played move to replace",
                OpeningLineRecallReferenceKind.HistoricalGame,
                OpeningTrainingMoveSourceKind.UserGame)
        };
        bool hasMainMove = theoryMoves.Any(item => item.IsMainMove);

        foreach ((OpeningTheoryMove theoryMove, int index) in theoryMoves.Select((item, index) => (item, index)))
        {
            bool isPreferred = theoryMove.IsMainMove || (!hasMainMove && index == 0);
            AddOrUpgradeOption(
                options,
                new OpeningTrainingMoveOption(
                    theoryMove.MoveSan,
                    theoryMove.MoveUci,
                    OpeningTrainingMoveRole.Repair,
                    isPreferred,
                    isPreferred
                        ? "Best repair from imported opening theory"
                        : "Playable repair from imported opening theory",
                    OpeningLineRecallReferenceKind.BetterMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    theoryMove.Idea,
                    theoryMove.ToOpeningPositionKey));
        }

        return options.Values
            .OrderByDescending(option => option.IsPreferred)
            .ThenBy(option => option.Role)
            .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<SavedOpeningReplay> LoadSavedOpeningReplays(IReadOnlyList<OpeningTrainerSnapshot> snapshots)
    {
        GameReplayService replayService = new();
        List<SavedOpeningReplay> replays = [];

        foreach (OpeningTrainerSnapshot snapshot in snapshots)
        {
            if (!importedGameStore.TryLoadImportedGame(snapshot.GameFingerprint, out ImportedGame? game) || game is null)
            {
                continue;
            }

            IReadOnlyList<ReplayPly> replay = replayService.Replay(game);
            if (replay.Count == 0)
            {
                continue;
            }

            replays.Add(new SavedOpeningReplay(snapshot, game, replay));
        }

        return replays;
    }

    private static IReadOnlyList<(OpeningWeaknessEntry Entry, BranchRoot root)> BuildBranchRoots(
        OpeningWeaknessEntry entry,
        IReadOnlyDictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex,
        IReadOnlyList<SavedOpeningReplay> savedReplays)
    {
        HashSet<string> exampleGameFingerprints = entry.ExampleGames
            .Select(example => example.GameFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> recurringLabels = entry.RecurringMistakeSequences
            .SelectMany(sequence => sequence.Labels)
            .Append(entry.FirstRecurringMistakeType)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<BranchOccurrence> occurrences = savedReplays
            .Where(replay => string.Equals(replay.Snapshot.Eco, entry.Eco, StringComparison.OrdinalIgnoreCase))
            .SelectMany(replay => BuildBranchOccurrences(replay, snapshotIndex, exampleGameFingerprints, recurringLabels))
            .ToList();

        return occurrences
            .GroupBy(occurrence => occurrence.RootFen, StringComparer.Ordinal)
            .Select(group => (Entry: entry, root: CreateBranchRoot(entry, group.ToList())))
            .Where(item => item.root.Branches.Count > 0)
            .ToList();
    }

    private static IReadOnlyList<BranchOccurrence> BuildBranchOccurrences(
        SavedOpeningReplay replay,
        IReadOnlyDictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex,
        IReadOnlySet<string> exampleGameFingerprints,
        IReadOnlySet<string> recurringLabels)
    {
        List<BranchOccurrence> occurrences = [];
        IReadOnlyList<ReplayPly> openingPlies = replay.Replay
            .Where(ply => ply.Phase == GamePhase.Opening)
            .OrderBy(ply => ply.Ply)
            .ToList();
        Dictionary<int, ReplayPly> byPly = openingPlies.ToDictionary(ply => ply.Ply);
        snapshotIndex.TryGetValue(new SnapshotKey(replay.Snapshot.GameFingerprint, replay.Snapshot.Side), out OpeningTrainerSnapshot? analyzedSnapshot);
        List<StoredMoveAnalysis> laterMoves = analyzedSnapshot?.OpeningMoves.OrderBy(move => move.Ply).ToList() ?? [];

        foreach (ReplayPly anchorMove in openingPlies.Where(ply => ply.Side == replay.Snapshot.Side))
        {
            if (!byPly.TryGetValue(anchorMove.Ply + 1, out ReplayPly? opponentMove)
                || opponentMove.Side == replay.Snapshot.Side)
            {
                continue;
            }

            ReplayPly? playerReply = byPly.TryGetValue(opponentMove.Ply + 1, out ReplayPly? nextReply) && nextReply.Side == replay.Snapshot.Side
                ? nextReply
                : null;

            StoredMoveAnalysis? responseAnalysis = laterMoves
                .FirstOrDefault(move => move.Ply == opponentMove.Ply + 1);
            StoredMoveAnalysis? firstIssue = laterMoves
                .Where(move => move.Ply > opponentMove.Ply && IsOpeningIssue(move))
                .OrderBy(move => move.Ply)
                .FirstOrDefault();
            bool matchesRecurring = firstIssue is not null
                && recurringLabels.Contains(firstIssue.MistakeLabel ?? string.Empty);

            occurrences.Add(new BranchOccurrence(
                replay.Snapshot,
                anchorMove,
                opponentMove,
                playerReply,
                anchorMove.FenAfter,
                exampleGameFingerprints.Contains(replay.Snapshot.GameFingerprint),
                matchesRecurring,
                responseAnalysis,
                firstIssue));
        }

        return occurrences;
    }

    private static BranchRoot CreateBranchRoot(OpeningWeaknessEntry entry, IReadOnlyList<BranchOccurrence> occurrences)
    {
        BranchOccurrence sample = occurrences
            .OrderByDescending(item => item.IsExampleGame)
            .ThenByDescending(item => item.PlayerIssue?.CentipawnLoss ?? 0)
            .ThenBy(item => item.AnchorMove.Ply)
            .First();
        IReadOnlyList<OpeningTrainingBranch> branches = occurrences
            .GroupBy(item => BuildMoveKey(item.OpponentMove.Uci, item.OpponentMove.San), StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateBranch(group.ToList()))
            .OrderByDescending(branch => branch.Frequency)
            .ThenByDescending(branch => branch.SourceStats.FirstOrDefault(item => item.SourceKind == OpeningTrainingBranchSourceKind.RecurringMistake)?.Count ?? 0)
            .ThenBy(branch => branch.OpponentMove, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        OpeningTrainingMoveOption? primaryRecommendedResponse = branches
            .Select(branch => branch.RecommendedResponse)
            .FirstOrDefault(option => option is not null);
        IReadOnlyList<OpeningTrainingMoveOption> candidateMoves = branches
            .Select(branch => new OpeningTrainingMoveOption(
                branch.OpponentMove,
                branch.OpponentMoveUci,
                OpeningTrainingMoveRole.Alternative,
                false,
                branch.SourceSummary,
                OpeningLineRecallReferenceKind.HistoricalGame,
                OpeningTrainingMoveSourceKind.UserGame,
                branch.RecommendedResponse?.Idea,
                branch.ResultingPositionKey))
            .Concat(branches
                .Where(branch => branch.RecommendedResponse is not null)
                .Select(branch => branch.RecommendedResponse!))
            .ToList();
        IReadOnlyList<OpeningTrainingMove> primaryContinuation = branches.Count > 0
            ? branches[0].Continuation
            : [];
        string branchSelectionSummary = BuildBranchSelectionSummary(branches);
        int priority = occurrences.Count * 25
            + occurrences.Count(item => item.IsExampleGame) * 10
            + occurrences.Count(item => item.MatchesRecurring) * 12
            + branches.Count * 8;

        return new BranchRoot(
            sample.Snapshot,
            sample.Snapshot.OpeningMoves.FirstOrDefault(move => move.Ply == sample.AnchorMove.Ply) ?? sample.Snapshot.OpeningMoves.First(),
            sample.AnchorMove.FenAfter,
            sample.AnchorMove.Ply,
            sample.AnchorMove.San,
            sample.PlayerIssue?.MistakeLabel ?? entry.FirstRecurringMistakeType,
            sample.PlayerIssue is null ? null : new OpeningIssue(sample.Snapshot, sample.PlayerIssue),
            sample.PlayerIssue?.MistakeLabel ?? entry.FirstRecurringMistakeType,
            priority,
            sample.Snapshot.OpeningMoves.Where(move => move.Ply >= sample.AnchorMove.Ply).Take(4).ToList(),
            branches,
            candidateMoves,
            primaryRecommendedResponse,
            primaryContinuation,
            branchSelectionSummary);
    }

    private static OpeningTrainingBranch CreateBranch(IReadOnlyList<BranchOccurrence> occurrences)
    {
        BranchOccurrence sample = occurrences
            .OrderByDescending(item => item.IsExampleGame)
            .ThenByDescending(item => item.MatchesRecurring)
            .ThenBy(item => item.OpponentMove.Ply)
            .First();
        int exampleCount = occurrences.Count(item => item.IsExampleGame);
        int recurringCount = occurrences.Count(item => item.MatchesRecurring);
        OpeningTrainingMoveOption? recommendedResponse = BuildRecommendedResponse(occurrences);
        List<OpeningTrainingMove> continuation =
        [
            new OpeningTrainingMove(
                sample.OpponentMove.Ply,
                sample.OpponentMove.MoveNumber,
                sample.OpponentMove.Side,
                sample.OpponentMove.San,
                sample.OpponentMove.Uci,
                OpeningTrainingMoveRole.Continuation,
                false)
        ];
        if (recommendedResponse is not null)
        {
            continuation.Add(new OpeningTrainingMove(
                sample.OpponentMove.Ply + 1,
                sample.OpponentMove.Side == PlayerSide.White ? sample.OpponentMove.MoveNumber + 1 : sample.OpponentMove.MoveNumber,
                Opponent(sample.OpponentMove.Side),
                recommendedResponse.DisplayText,
                recommendedResponse.Uci,
                OpeningTrainingMoveRole.Expected,
                true,
                recommendedResponse.Note));
        }

        List<OpeningTrainingBranchSourceStat> sourceStats =
        [
            new(OpeningTrainingBranchSourceKind.SavedContinuation, occurrences.Count)
        ];
        if (exampleCount > 0)
        {
            sourceStats.Add(new OpeningTrainingBranchSourceStat(OpeningTrainingBranchSourceKind.ExampleGame, exampleCount));
        }

        if (recurringCount > 0)
        {
            sourceStats.Add(new OpeningTrainingBranchSourceStat(OpeningTrainingBranchSourceKind.RecurringMistake, recurringCount));
        }

        return new OpeningTrainingBranch(
            new OpeningBranchKey($"{sample.Snapshot.Eco}:{sample.AnchorMove.Ply}:{sample.OpponentMove.Uci ?? sample.OpponentMove.San}"),
            sample.OpponentMove.San,
            sample.OpponentMove.Uci,
            occurrences.Count,
            BuildBranchSourceSummary(occurrences.Count, exampleCount, recurringCount),
            recommendedResponse,
            continuation,
            sourceStats,
            OpeningPositionKeyBuilder.BuildKey(sample.OpponentMove.FenAfter));
    }

    private static OpeningTrainingMoveOption? BuildRecommendedResponse(IReadOnlyList<BranchOccurrence> occurrences)
    {
        Dictionary<string, ReplyOptionAccumulator> options = new(StringComparer.OrdinalIgnoreCase);

        foreach (BranchOccurrence occurrence in occurrences)
        {
            if (occurrence.PlayerResponseAnalysis is not null && !string.IsNullOrWhiteSpace(occurrence.PlayerResponseAnalysis.BestMoveUci))
            {
                string responseDisplay = FormatMove(occurrence.OpponentMove.FenAfter, occurrence.PlayerResponseAnalysis.BestMoveUci);
                string key = BuildMoveKey(occurrence.PlayerResponseAnalysis.BestMoveUci, responseDisplay);
                if (!options.TryGetValue(key, out ReplyOptionAccumulator? accumulator))
                {
                    accumulator = new ReplyOptionAccumulator(responseDisplay, occurrence.PlayerResponseAnalysis.BestMoveUci);
                    options[key] = accumulator;
                }

                accumulator.BestMoveCount++;
                if (occurrence.IsExampleGame)
                {
                    accumulator.ExampleCount++;
                }

                if (occurrence.MatchesRecurring)
                {
                    accumulator.RecurringCount++;
                }
            }

            if (occurrence.PlayerReply is null)
            {
                continue;
            }

            string replyKey = BuildMoveKey(occurrence.PlayerReply.Uci, occurrence.PlayerReply.San);
            if (!options.TryGetValue(replyKey, out ReplyOptionAccumulator? replyAccumulator))
            {
                replyAccumulator = new ReplyOptionAccumulator(occurrence.PlayerReply.San, occurrence.PlayerReply.Uci);
                options[replyKey] = replyAccumulator;
            }

            replyAccumulator.PlayedCount++;
            if (occurrence.IsExampleGame)
            {
                replyAccumulator.ExampleCount++;
            }

            if (occurrence.MatchesRecurring)
            {
                replyAccumulator.RecurringCount++;
            }
        }

        ReplyOptionAccumulator? best = options.Values
            .OrderByDescending(option => option.BestMoveCount)
            .ThenByDescending(option => option.PlayedCount)
            .ThenByDescending(option => option.ExampleCount)
            .ThenByDescending(option => option.RecurringCount)
            .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        return new OpeningTrainingMoveOption(
            best.DisplayText,
            best.Uci,
            OpeningTrainingMoveRole.Expected,
            true,
            BuildRecommendedResponseNote(best),
            best.BestMoveCount > 0
                ? OpeningLineRecallReferenceKind.BestMove
                : OpeningLineRecallReferenceKind.ReferenceLine,
            best.BestMoveCount > 0
                ? OpeningTrainingMoveSourceKind.EngineBestMove
                : OpeningTrainingMoveSourceKind.UserGame,
            OpeningMoveIdeaHeuristics.Build(best.DisplayText, best.BestMoveCount > 0));
    }

    private static string BuildRecommendedResponseNote(ReplyOptionAccumulator option)
    {
        List<string> parts = [];
        if (option.BestMoveCount > 0)
        {
            parts.Add($"matched saved best-move analysis in {option.BestMoveCount} game(s)");
        }

        if (option.PlayedCount > 0)
        {
            parts.Add($"played as the local follow-up in {option.PlayedCount} game(s)");
        }

        if (option.RecurringCount > 0)
        {
            parts.Add($"appears inside {option.RecurringCount} recurring-mistake branch(es)");
        }

        return parts.Count == 0
            ? "Stable local reaction"
            : $"Recommended because it {string.Join(", and ", parts)}.";
    }

    private static string BuildBranchSourceSummary(int savedCount, int exampleCount, int recurringCount)
    {
        List<string> parts = [$"saved continuations: {savedCount}"];
        if (exampleCount > 0)
        {
            parts.Add($"example games: {exampleCount}");
        }

        if (recurringCount > 0)
        {
            parts.Add($"recurring-mistake links: {recurringCount}");
        }

        return string.Join(" | ", parts);
    }

    private static string BuildTheoryBranchSelectionSummary(IReadOnlyList<OpeningTrainingBranch> branches)
    {
        if (branches.Count == 0)
        {
            return "No imported theory branches were found for this setup.";
        }

        return $"Showing {branches.Count} imported opponent branch(es). Ordered by imported-game frequency.";
    }

    private static string BuildBranchSelectionSummary(IReadOnlyList<OpeningTrainingBranch> branches)
    {
        if (branches.Count == 0)
        {
            return "No local opponent branches were found for this setup.";
        }

        return $"Showing {branches.Count} local opponent branch(es). Ordered by saved-game frequency, then recurring-mistake support.";
    }

    private static IReadOnlyList<OpeningTheoryMove> GetTheoryMoves(
        OpeningTheoryQueryService? openingTheory,
        string fen,
        int limit = 6)
    {
        if (openingTheory is null || string.IsNullOrWhiteSpace(fen))
        {
            return [];
        }

        IReadOnlyList<OpeningTheoryMove> playableMoves = openingTheory.GetPlayableMovesForFen(fen, limit);
        return playableMoves.Count > 0
            ? playableMoves
            : openingTheory.GetTopMovesForFen(fen, limit);
    }

    private static IReadOnlyList<OpeningTrainingBranch> BuildTheoryBranches(
        string rootFen,
        OpeningTheoryQueryService? openingTheory)
    {
        IReadOnlyList<OpeningTheoryMove> opponentMoves = GetTheoryMoves(openingTheory, rootFen, limit: 3);
        if (opponentMoves.Count == 0)
        {
            return [];
        }

        bool hasMainMove = opponentMoves.Any(move => move.IsMainMove);

        return opponentMoves
            .Select((move, index) =>
            {
                IReadOnlyList<OpeningTheoryMove> replyMoves = GetTheoryMoves(openingTheory, move.ToFen, limit: 1);
                OpeningTheoryMove? reply = replyMoves.FirstOrDefault();
                OpeningTrainingMoveOption? recommendedResponse = reply is null
                    ? null
                    : new OpeningTrainingMoveOption(
                        reply.MoveSan,
                        reply.MoveUci,
                        OpeningTrainingMoveRole.Expected,
                        reply.IsMainMove || (!replyMoves.Any(item => item.IsMainMove) && replyMoves.Count == 1),
                        "Recommended response from imported opening theory",
                        reply.IsMainMove
                            ? OpeningLineRecallReferenceKind.ReferenceLine
                            : OpeningLineRecallReferenceKind.BetterMove,
                        OpeningTrainingMoveSourceKind.OpeningBook,
                        reply.Idea,
                        reply.ToOpeningPositionKey);
                List<OpeningTrainingMove> continuation =
                [
                    new OpeningTrainingMove(
                        0,
                        0,
                        PlayerSide.White,
                        move.MoveSan,
                        move.MoveUci,
                        OpeningTrainingMoveRole.Continuation,
                        false)
                ];
                if (recommendedResponse is not null)
                {
                    continuation.Add(new OpeningTrainingMove(
                        0,
                        0,
                        PlayerSide.Black,
                        recommendedResponse.DisplayText,
                        recommendedResponse.Uci,
                        OpeningTrainingMoveRole.Expected,
                        true,
                        recommendedResponse.Note));
                }

                bool isPreferred = move.IsMainMove || (!hasMainMove && index == 0);
                return new OpeningTrainingBranch(
                    new OpeningBranchKey($"theory:{move.MoveUci}:{move.ToPositionKey}"),
                    move.MoveSan,
                    move.MoveUci,
                    Math.Max(1, move.DistinctGameCount),
                    isPreferred
                        ? $"Main imported branch | games: {Math.Max(1, move.DistinctGameCount)} | occurrences: {Math.Max(1, move.OccurrenceCount)}"
                        : $"Imported branch | games: {Math.Max(1, move.DistinctGameCount)} | occurrences: {Math.Max(1, move.OccurrenceCount)}",
                    recommendedResponse,
                    continuation,
                    [],
                    move.ToOpeningPositionKey);
            })
            .ToList();
    }

    private static OpeningIssue? FindFirstIssue(OpeningTrainerSnapshot snapshot, int? preferredPly)
    {
        if (preferredPly.HasValue)
        {
            StoredMoveAnalysis? exact = snapshot.OpeningMoves.FirstOrDefault(move => move.Ply == preferredPly.Value);
            if (exact is not null && IsOpeningIssue(exact))
            {
                return new OpeningIssue(snapshot, exact);
            }
        }

        StoredMoveAnalysis? first = snapshot.OpeningMoves
            .Where(IsOpeningIssue)
            .OrderBy(move => move.Ply)
            .FirstOrDefault();

        return first is null ? null : new OpeningIssue(snapshot, first);
    }

    private static IReadOnlyList<OpeningTrainingSourceSummary> BuildSourceSummaries(
        IReadOnlyList<OpeningTrainingPosition> positions,
        IReadOnlyList<OpeningTrainingLine> lines)
    {
        Dictionary<OpeningTrainingSourceKind, int> lineCounts = lines
            .GroupBy(line => line.SourceKind)
            .ToDictionary(group => group.Key, group => group.Count());

        return positions
            .GroupBy(position => position.SourceKind)
            .Select(group => new OpeningTrainingSourceSummary(
                group.Key,
                group.Count(),
                lineCounts.TryGetValue(group.Key, out int count) ? count : 0,
                group.Select(position => position.Eco)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(summary => summary.SourceKind)
            .ToList();
    }

    private static bool IsOpeningIssue(StoredMoveAnalysis move)
    {
        StoredMoveContext played = move.Move;
        string? label = move.Advice.MistakeLabel;
        int loss = Math.Max(0, played.CentipawnLoss ?? 0);
        if (played.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake)
        {
            return true;
        }

        if (label is null || !FallbackLabels.Contains(label))
        {
            return played.Quality.IsProblem() && loss >= SignificantMistakeThresholdCp;
        }

        if (label.Equals("opening_principles", StringComparison.OrdinalIgnoreCase))
        {
            return loss >= TheoryExitThresholdCp;
        }

        return loss >= SignificantMistakeThresholdCp;
    }

    private static string NormalizePlayerKey(string playerName)
    {
        return playerName.Trim().ToLowerInvariant();
    }

    private static string NormalizeEco(string? eco)
    {
        return string.IsNullOrWhiteSpace(eco) ? "Unknown" : eco.Trim().ToUpperInvariant();
    }

    private static string GetOpponentName(StoredMoveAnalysis move)
    {
        return move.Analysis.AnalyzedSide == PlayerSide.White
            ? move.Game.BlackPlayer ?? "Unknown opponent"
            : move.Game.WhitePlayer ?? "Unknown opponent";
    }

    private static string GetOpponentName(ImportedGame game, PlayerSide analyzedSide)
    {
        return analyzedSide == PlayerSide.White
            ? game.BlackPlayer ?? "Unknown opponent"
            : game.WhitePlayer ?? "Unknown opponent";
    }

    private static string FormatMove(string fenBefore, string? bestMoveUci)
    {
        if (string.IsNullOrWhiteSpace(bestMoveUci))
        {
            return "Unknown";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(bestMoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return bestMoveUci;
        }

        return ChessMoveDisplayHelper.FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static string BuildRepairReason(StoredMoveAnalysis move)
    {
        StoredMoveAdviceContext advice = move.Advice;
        if (!string.IsNullOrWhiteSpace(advice.TrainingHint))
        {
            return EnsureSentence(advice.TrainingHint!);
        }

        if (!string.IsNullOrWhiteSpace(advice.ShortExplanation))
        {
            return EnsureSentence(advice.ShortExplanation!);
        }

        return advice.MistakeLabel?.Trim().ToLowerInvariant() switch
        {
            "opening_principles" => "It follows opening principles more closely and improves development.",
            "king_safety" => "It keeps the king safer and avoids creating early weaknesses.",
            "piece_activity" => "It improves piece activity and coordinates the position better.",
            "material_loss" => "It avoids the material concession that followed in the game.",
            _ => "It fits the position better than the move played in the game."
        };
    }

    private static bool TryResolveMoveInput(string fen, string submittedMoveText, out AppliedMoveInfo? appliedMove, out string? error)
    {
        appliedMove = null;
        error = null;

        if (string.IsNullOrWhiteSpace(submittedMoveText))
        {
            error = "Move cannot be empty.";
            return false;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fen, out error))
        {
            return false;
        }

        if (game.TryApplyUci(submittedMoveText.Trim(), out appliedMove, out error) && appliedMove is not null)
        {
            return true;
        }

        ChessGame sanGame = new();
        if (!sanGame.TryLoadFen(fen, out error))
        {
            return false;
        }

        try
        {
            appliedMove = sanGame.ApplySanWithResult(submittedMoveText.Trim());
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            appliedMove = null;
            return false;
        }
    }

    private static bool MovesMatch(OpeningTrainingMoveOption option, AppliedMoveInfo resolvedMove)
    {
        if (!string.IsNullOrWhiteSpace(option.Uci)
            && string.Equals(option.Uci, resolvedMove.Uci, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            SanNotation.NormalizeSan(option.DisplayText),
            SanNotation.NormalizeSan(resolvedMove.San),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MoveOptionsMatch(OpeningTrainingMoveOption left, OpeningTrainingMoveOption right)
    {
        if (!string.IsNullOrWhiteSpace(left.Uci)
            && !string.IsNullOrWhiteSpace(right.Uci)
            && string.Equals(left.Uci, right.Uci, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            SanNotation.NormalizeSan(left.DisplayText),
            SanNotation.NormalizeSan(right.DisplayText),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MoveOptionMatchesTextAndUci(OpeningTrainingMoveOption option, string displayText, string? uci)
    {
        if (!string.IsNullOrWhiteSpace(option.Uci)
            && !string.IsNullOrWhiteSpace(uci)
            && string.Equals(option.Uci, uci, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            SanNotation.NormalizeSan(option.DisplayText),
            SanNotation.NormalizeSan(displayText),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMoveKey(string? uci, string displayText)
    {
        return !string.IsNullOrWhiteSpace(uci)
            ? $"uci:{uci.Trim().ToLowerInvariant()}"
            : $"san:{SanNotation.NormalizeSan(displayText)}";
    }

    private static OpeningTrainingScore DetermineScore(
        OpeningTrainingStrictness strictness,
        bool matchedPreferred,
        bool matchedPlayable,
        bool transposedToKnownPosition)
    {
        return strictness switch
        {
            OpeningTrainingStrictness.StrictRepertoire => matchedPreferred ? OpeningTrainingScore.Correct : OpeningTrainingScore.Wrong,
            OpeningTrainingStrictness.BookFlexible => matchedPreferred
                ? OpeningTrainingScore.Correct
                : matchedPlayable || transposedToKnownPosition
                    ? OpeningTrainingScore.Playable
                    : OpeningTrainingScore.Wrong,
            OpeningTrainingStrictness.EngineTolerant => matchedPreferred
                ? OpeningTrainingScore.Correct
                : matchedPlayable || transposedToKnownPosition
                    ? OpeningTrainingScore.Playable
                    : OpeningTrainingScore.Wrong,
            OpeningTrainingStrictness.Exploration => matchedPreferred
                ? OpeningTrainingScore.Correct
                : OpeningTrainingScore.Playable,
            _ => matchedPreferred ? OpeningTrainingScore.Correct : matchedPlayable ? OpeningTrainingScore.Playable : OpeningTrainingScore.Wrong
        };
    }

    private static OpeningCoverageSummary BuildCoverageSummary(IReadOnlyList<OpeningTrainingBranch> branches)
    {
        int totalBranches = branches.Count;
        return new OpeningCoverageSummary(
            totalBranches,
            0,
            totalBranches,
            totalBranches,
            0,
            0,
            0,
            0);
    }

    private static OpponentReplyProfile BuildOpponentReplyProfile(
        OpeningLineKey lineKey,
        RepertoireSide side,
        IReadOnlyList<OpeningTrainingBranch> branches)
    {
        return new OpponentReplyProfile(
            lineKey,
            side,
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
                ? "No opponent branches available."
                : $"Prepared {branches.Count} opponent branch(es) from theory.");
    }

    private static IReadOnlyList<OpeningReviewItem> BuildReviewItems(
        IReadOnlyList<OpeningTrainingRecordedAttempt> attempts,
        DateTime finishedUtc)
    {
        return attempts
            .Where(attempt => attempt.BranchKey.HasValue && attempt.PositionKey.HasValue)
            .Select(attempt =>
            {
                int reviewDays = attempt.Score switch
                {
                    OpeningTrainingScore.Wrong => 0,
                    OpeningTrainingScore.Playable => 2,
                    _ => 4
                };
                int correctStreak = attempt.Score == OpeningTrainingScore.Correct ? 1 : 0;
                if (correctStreak >= 3)
                {
                    reviewDays = 10;
                }

                return new OpeningReviewItem(
                    attempt.BranchKey!.Value,
                    attempt.PositionKey!.Value,
                    finishedUtc,
                    finishedUtc.AddDays(reviewDays),
                    attempt.Score == OpeningTrainingScore.Wrong ? 1.3 : attempt.Score == OpeningTrainingScore.Playable ? 1.8 : 2.2,
                    correctStreak,
                    attempt.Score == OpeningTrainingScore.Wrong ? 1 : 0,
                    1,
                    attempt.OpeningKey,
                    attempt.OpeningLineKey);
            })
            .ToList();
    }

    private static OpeningKey BuildOpeningKey(string eco, string openingName)
    {
        string name = string.IsNullOrWhiteSpace(openingName) ? OpeningCatalog.GetName(eco) : openingName;
        return new OpeningKey($"{NormalizeEco(eco)}:{name}");
    }

    private static OpeningLineKey BuildOpeningLineKey(string eco, string openingName, string lineId)
    {
        string name = string.IsNullOrWhiteSpace(openingName) ? OpeningCatalog.GetName(eco) : openingName;
        return new OpeningLineKey($"{NormalizeEco(eco)}:{name}:{lineId}");
    }

    private static RepertoireSide ToRepertoireSide(PlayerSide side)
    {
        return side == PlayerSide.White ? RepertoireSide.White : RepertoireSide.Black;
    }

    private static string BuildBetterMoveSummary(
        OpeningTrainingPosition position,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences)
    {
        string? preferredTheoryMove = GetPreferredTheoryMoveDisplay(preferredReferences);
        if (!string.IsNullOrWhiteSpace(preferredTheoryMove))
        {
            return $"Better move: {preferredTheoryMove}.";
        }

        if (!string.IsNullOrWhiteSpace(position.BetterMove))
        {
            return $"Better move: {position.BetterMove}.";
        }

        return "Better move: no saved repair move was available.";
    }

    private static string? GetPreferredTheoryMoveDisplay(IReadOnlyList<OpeningTrainingMoveOption> options)
    {
        return options
            .FirstOrDefault(option => option.IsPreferred)?.DisplayText
            ?? options.FirstOrDefault()?.DisplayText;
    }

    private static string BuildWhyBetterSummary(
        OpeningTrainingPosition position,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences,
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences)
    {
        if (!string.IsNullOrWhiteSpace(position.BetterMoveReason))
        {
            return $"Why: {EnsureSentence(position.BetterMoveReason!)}";
        }

        string? note = preferredReferences
            .Concat(playableReferences)
            .Select(option => option.Note)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(note)
            ? "Why: it was the healthier opening choice in this position."
            : $"Why: {EnsureSentence(note!)}";
    }

    private static string EnsureSentence(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        char last = trimmed[^1];
        return last is '.' or '!' or '?'
            ? trimmed
            : $"{trimmed}.";
    }

    private static string FormatMatchedReferences(IReadOnlyList<OpeningTrainingMoveOption> options)
    {
        return string.Join(", ", options.Select(option => option.DisplayText).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildTags(string eco, string? label, params string[] tags)
    {
        return tags
            .Append(eco)
            .Append(label)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildLineId(OpeningTrainingSourceKind sourceKind, string gameFingerprint, int ply)
    {
        return $"{sourceKind}:{gameFingerprint}:{ply}";
    }

    private static PlayerSide Opponent(PlayerSide side)
    {
        return side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;
    }

    private static OpeningTrainingReference CreateReference(OpeningTrainerSnapshot snapshot, string sourceLabel, OpeningIssue? issue)
    {
        return new OpeningTrainingReference(
            snapshot.GameFingerprint,
            snapshot.Side,
            snapshot.OpponentName,
            snapshot.DateText,
            snapshot.Result,
            sourceLabel,
            issue?.Move.Ply,
            issue?.Move.MistakeLabel);
    }

    private readonly record struct AnalysisVariantKey(
        string GameFingerprint,
        PlayerSide Side,
        int Depth,
        int MultiPv,
        int? MoveTimeMs);

    private readonly record struct SnapshotSelectionKey(
        string GameFingerprint,
        PlayerSide Side);

    private readonly record struct SnapshotKey(
        string GameFingerprint,
        PlayerSide Side);

    private sealed record OpeningTrainerSnapshot(
        string GameFingerprint,
        string PlayerKey,
        string DisplayName,
        PlayerSide Side,
        string OpponentName,
        string? DateText,
        string? Result,
        string Eco,
        string OpeningName,
        int Depth,
        int MultiPv,
        int? MoveTimeMs,
        DateTime AnalysisUpdatedUtc,
        IReadOnlyList<StoredMoveAnalysis> OpeningMoves);

    private sealed record SavedOpeningReplay(
        OpeningTrainerSnapshot Snapshot,
        ImportedGame Game,
        IReadOnlyList<ReplayPly> Replay);

    private sealed record BranchOccurrence(
        OpeningTrainerSnapshot Snapshot,
        ReplayPly AnchorMove,
        ReplayPly OpponentMove,
        ReplayPly? PlayerReply,
        string RootFen,
        bool IsExampleGame,
        bool MatchesRecurring,
        StoredMoveAnalysis? PlayerResponseAnalysis,
        StoredMoveAnalysis? PlayerIssue);

    private sealed record BranchRoot(
        OpeningTrainerSnapshot Snapshot,
        StoredMoveAnalysis AnchorMove,
        string RootFen,
        int AnchorPly,
        string AnchorSan,
        string? MistakeLabel,
        OpeningIssue? FirstIssue,
        string? ThemeLabel,
        int Priority,
        IReadOnlyList<StoredMoveAnalysis> SampleLine,
        IReadOnlyList<OpeningTrainingBranch> Branches,
        IReadOnlyList<OpeningTrainingMoveOption> CandidateMoves,
        OpeningTrainingMoveOption? PrimaryRecommendedResponse,
        IReadOnlyList<OpeningTrainingMove> PrimaryContinuation,
        string BranchSelectionSummary);

    private sealed class ReplyOptionAccumulator
    {
        public ReplyOptionAccumulator(string displayText, string? uci)
        {
            DisplayText = displayText;
            Uci = uci;
        }

        public string DisplayText { get; }

        public string? Uci { get; }

        public int BestMoveCount { get; set; }

        public int PlayedCount { get; set; }

        public int ExampleCount { get; set; }

        public int RecurringCount { get; set; }
    }

    private sealed record OpeningIssue(
        OpeningTrainerSnapshot Snapshot,
        StoredMoveAnalysis Move);
}
