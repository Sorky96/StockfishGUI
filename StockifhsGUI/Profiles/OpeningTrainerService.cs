using System.Globalization;

namespace StockifhsGUI;

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

    private readonly IAnalysisStore analysisStore;

    public OpeningTrainerService(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
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
        if (!new OpeningWeaknessService(analysisStore).TryBuildReport(playerKeyOrName, out OpeningWeaknessReport? weaknessReport)
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
        Dictionary<string, List<StoredMoveAnalysis>> fenReferenceIndex = BuildFenReferenceIndex(snapshots);

        Dictionary<SnapshotKey, OpeningTrainerSnapshot> snapshotIndex = snapshots
            .ToDictionary(snapshot => new SnapshotKey(snapshot.GameFingerprint, snapshot.Side));
        Dictionary<string, OpeningTrainingLine> linesById = new(StringComparer.Ordinal);
        List<OpeningTrainingPosition> positions = [];

        foreach (OpeningTrainingSourceKind source in effectiveOptions.Sources!)
        {
            IReadOnlyList<OpeningTrainingPosition> built = source switch
            {
                OpeningTrainingSourceKind.ExampleGame => BuildExampleGamePositions(weaknessReport, snapshotIndex, linesById, fenReferenceIndex, effectiveOptions),
                OpeningTrainingSourceKind.OpeningWeakness => BuildOpeningWeaknessPositions(weaknessReport, snapshotIndex, savedReplays, linesById, effectiveOptions),
                OpeningTrainingSourceKind.FirstOpeningMistake => BuildFirstMistakePositions(snapshots, weaknessReport, linesById, effectiveOptions),
                _ => []
            };

            positions.AddRange(built);
        }

        positions = positions
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

        session = new OpeningTrainingSession(
            $"opening-trainer:{normalizedPlayerKey}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            normalizedPlayerKey,
            displayName,
            DateTime.UtcNow,
            positions.Select(position => position.Mode).Distinct().ToList(),
            positions.Select(position => position.SourceKind).Distinct().ToList(),
            sourceSummaries,
            lines,
            positions);
        return positions.Count > 0;
    }

    public OpeningLineRecallAttemptResult EvaluateLineRecallMove(OpeningTrainingPosition position, string submittedMoveText)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Mode != OpeningTrainingMode.LineRecall)
        {
            throw new ArgumentException("Line recall evaluation is available only for line recall positions.", nameof(position));
        }

        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences = position.CandidateMoves
            .Where(option => option.IsPreferred)
            .ToList();
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences = position.CandidateMoves
            .Where(option => !option.IsPreferred)
            .ToList();

        if (!TryResolveMoveInput(position.Fen, submittedMoveText, out AppliedMoveInfo? resolvedMove, out string? error)
            || resolvedMove is null)
        {
            return new OpeningLineRecallAttemptResult(
                position.PositionId,
                submittedMoveText,
                null,
                null,
                OpeningLineRecallGrade.Wrong,
                string.IsNullOrWhiteSpace(error)
                    ? "The submitted move could not be matched to a legal move in this position."
                    : error,
                [],
                preferredReferences,
                playableReferences);
        }

        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences = position.CandidateMoves
            .Where(option => MovesMatch(option, resolvedMove))
            .ToList();

        OpeningLineRecallGrade grade;
        string summary;
        if (matchingReferences.Any(option => option.IsPreferred))
        {
            grade = OpeningLineRecallGrade.Correct;
            summary = $"Accepted as correct. The move matches a preferred local reference: {FormatMatchedReferences(matchingReferences.Where(option => option.IsPreferred).ToList())}.";
        }
        else if (matchingReferences.Count > 0)
        {
            grade = OpeningLineRecallGrade.Playable;
            summary = $"Accepted as playable. The move appears in local references, but it is not the strongest preferred continuation here: {FormatMatchedReferences(matchingReferences)}.";
        }
        else
        {
            grade = OpeningLineRecallGrade.Wrong;
            summary = preferredReferences.Count == 0
                ? "Marked as wrong because the move does not match any local reference move for this position."
                : $"Marked as wrong because the move does not match the preferred local references: {FormatMatchedReferences(preferredReferences)}.";
        }

        return new OpeningLineRecallAttemptResult(
            position.PositionId,
            submittedMoveText,
            resolvedMove.San,
            resolvedMove.Uci,
            grade,
            summary,
            matchingReferences,
            preferredReferences,
            playableReferences);
    }

    public OpeningMistakeRepairAttemptResult EvaluateMistakeRepairMove(OpeningTrainingPosition position, string submittedMoveText)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Mode != OpeningTrainingMode.MistakeRepair)
        {
            throw new ArgumentException("Mistake repair evaluation is available only for mistake repair positions.", nameof(position));
        }

        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences = position.CandidateMoves
            .Where(option => option.IsPreferred)
            .ToList();
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences = position.CandidateMoves
            .Where(option => !option.IsPreferred && option.Role == OpeningTrainingMoveRole.Repair)
            .ToList();

        string betterMoveSummary = BuildBetterMoveSummary(position, preferredReferences);
        string whyBetter = BuildWhyBetterSummary(position, preferredReferences, playableReferences);

        if (!TryResolveMoveInput(position.Fen, submittedMoveText, out AppliedMoveInfo? resolvedMove, out string? error)
            || resolvedMove is null)
        {
            return new OpeningMistakeRepairAttemptResult(
                position.PositionId,
                submittedMoveText,
                null,
                null,
                OpeningMistakeRepairGrade.Wrong,
                string.IsNullOrWhiteSpace(error)
                    ? "The submitted move could not be matched to a legal move in this position."
                    : error,
                betterMoveSummary,
                whyBetter,
                [],
                preferredReferences,
                playableReferences);
        }

        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences = position.CandidateMoves
            .Where(option => MovesMatch(option, resolvedMove))
            .ToList();

        OpeningMistakeRepairGrade grade;
        string summary;
        if (matchingReferences.Any(option => option.IsPreferred))
        {
            grade = OpeningMistakeRepairGrade.Correct;
            summary = $"Correct repair. {betterMoveSummary} {whyBetter}";
        }
        else if (matchingReferences.Any(option => option.Role == OpeningTrainingMoveRole.Repair))
        {
            grade = OpeningMistakeRepairGrade.Playable;
            summary = $"Playable repair. {betterMoveSummary} {whyBetter}";
        }
        else
        {
            grade = OpeningMistakeRepairGrade.Wrong;
            summary = $"Wrong repair. {betterMoveSummary} {whyBetter}";
        }

        return new OpeningMistakeRepairAttemptResult(
            position.PositionId,
            submittedMoveText,
            resolvedMove.San,
            resolvedMove.Uci,
            grade,
            summary,
            betterMoveSummary,
            whyBetter,
            matchingReferences,
            preferredReferences,
            playableReferences);
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
            Math.Clamp(options?.MaxContinuationMoves ?? 6, 1, 12));
    }

    private List<OpeningTrainerSnapshot> LoadSnapshots(string? filterText, int limit)
    {
        IReadOnlyList<StoredMoveAnalysis> storedMoves = analysisStore.ListMoveAnalyses(filterText, Math.Clamp(limit * 64, 500, 50000));
        IReadOnlyList<GameAnalysisResult> results = analysisStore.ListResults(filterText, Math.Max(limit * 8, 200));

        List<OpeningTrainerSnapshot> mergedSnapshots = BuildSnapshotsFromMoves(storedMoves);
        mergedSnapshots.AddRange(BuildSnapshotsFromResults(results));

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

    private static List<OpeningTrainerSnapshot> BuildSnapshotsFromMoves(IReadOnlyList<StoredMoveAnalysis> storedMoves)
    {
        return storedMoves
            .GroupBy(move => new AnalysisVariantKey(move.GameFingerprint, move.AnalyzedSide, move.Depth, move.MultiPv, move.MoveTimeMs))
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
            .Where(move => move.Phase == GamePhase.Opening)
            .OrderBy(move => move.Ply)
            .ToList();
        if (openingMoves.Count == 0)
        {
            return null;
        }

        StoredMoveAnalysis first = openingMoves[0];
        string? playerName = first.AnalyzedSide == PlayerSide.White
            ? first.WhitePlayer
            : first.BlackPlayer;
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        return new OpeningTrainerSnapshot(
            first.GameFingerprint,
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            first.AnalyzedSide,
            GetOpponentName(first),
            first.DateText,
            first.Result,
            NormalizeEco(first.Eco),
            OpeningCatalog.GetName(first.Eco),
            first.Depth,
            first.MultiPv,
            first.MoveTimeMs,
            first.AnalysisUpdatedUtc,
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

        IReadOnlyList<StoredMoveAnalysis> openingMoves = result.MoveAnalyses
            .Where(move => move.Replay.Phase == GamePhase.Opening)
            .OrderBy(move => move.Replay.Ply)
            .Select(move => new StoredMoveAnalysis(
                GameFingerprint.Compute(result.Game.PgnText),
                result.AnalyzedSide,
                0,
                0,
                null,
                DateTime.MinValue,
                result.Game.WhitePlayer,
                result.Game.BlackPlayer,
                result.Game.DateText,
                result.Game.Result,
                result.Game.Eco,
                result.Game.Site,
                move.Replay.Ply,
                move.Replay.MoveNumber,
                move.Replay.San,
                move.Replay.Uci,
                move.Replay.FenBefore,
                move.Replay.FenAfter,
                move.Replay.Phase,
                move.EvalBeforeCp,
                move.EvalAfterCp,
                move.BestMateIn,
                move.PlayedMateIn,
                move.CentipawnLoss,
                move.Quality,
                move.MaterialDeltaCp,
                move.BeforeAnalysis.BestMoveUci,
                move.MistakeTag?.Label,
                move.MistakeTag?.Confidence,
                move.MistakeTag?.Evidence ?? [],
                move.Explanation?.ShortText,
                move.Explanation?.DetailedText,
                move.Explanation?.TrainingHint,
                result.HighlightedMistakes.SelectMany(item => item.Moves).Any(item => item.Replay.Ply == move.Replay.Ply)))
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
        IReadOnlyDictionary<string, List<StoredMoveAnalysis>> fenReferenceIndex,
        OpeningTrainingSessionOptions options)
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
            linesById[lineId] = CreateLine(
                lineId,
                OpeningTrainingSourceKind.ExampleGame,
                snapshot,
                anchorMove,
                "Recall the stable line from your own example game.",
                lineMoves,
                firstIssue);

            positions.Add(new OpeningTrainingPosition(
                $"example:{snapshot.GameFingerprint}:{anchorMove.Ply}",
                OpeningTrainingMode.LineRecall,
                OpeningTrainingSourceKind.ExampleGame,
                snapshot.Eco,
                OpeningCatalog.Describe(snapshot.Eco),
                anchorMove.FenBefore,
                anchorMove.Ply,
                anchorMove.MoveNumber,
                snapshot.Side,
                "Recall the move you want to have available from this example line.",
                "Use your own game as the reference line. Name the move and then replay the next few opening moves.",
                (example.FirstMistakeCentipawnLoss ?? 0) + 25,
                firstIssue?.Move.MistakeLabel,
                anchorMove.San,
                firstIssue is null ? null : FormatMove(firstIssue.Move.FenBefore, firstIssue.Move.BestMoveUci),
                firstIssue is null ? null : BuildRepairReason(firstIssue.Move),
                BuildTags(snapshot.Eco, firstIssue?.Move.MistakeLabel, "example-game", "line-recall"),
                BuildLineRecallOptions(anchorMove, fenReferenceIndex),
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
        OpeningTrainingSessionOptions options)
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
                OpeningTrainingMode.BranchAwareness,
                OpeningTrainingSourceKind.OpeningWeakness,
                entry.Eco,
                entry.OpeningDisplayName,
                root.RootFen,
                root.AnchorPly + 1,
                root.AnchorMove.MoveNumber,
                Opponent(root.Snapshot.Side),
                "Review the typical opponent replies in this opening and keep one stable local reaction ready.",
                "Use only local evidence: example games, recurring mistake patterns, and saved continuations from your own games.",
                root.Priority,
                root.ThemeLabel,
                root.AnchorMove.San,
                root.PrimaryRecommendedResponse?.DisplayText,
                root.BranchSelectionSummary,
                BuildTags(entry.Eco, root.ThemeLabel, "opening-weakness", "branch-awareness"),
                root.CandidateMoves,
                root.PrimaryContinuation,
                CreateReference(root.Snapshot, "Opening weakness", issue),
                lineId,
                root.Branches,
                root.BranchSelectionSummary));
        }

        return positions;
    }

    private static IReadOnlyList<OpeningTrainingPosition> BuildFirstMistakePositions(
        IReadOnlyList<OpeningTrainerSnapshot> snapshots,
        OpeningWeaknessReport weaknessReport,
        IDictionary<string, OpeningTrainingLine> linesById,
        OpeningTrainingSessionOptions options)
    {
        Dictionary<string, List<OpeningMoveRecommendation>> repairIndex = BuildRepairIndex(weaknessReport);
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
                OpeningTrainingMode.MistakeRepair,
                OpeningTrainingSourceKind.FirstOpeningMistake,
                snapshot.Eco,
                OpeningCatalog.Describe(snapshot.Eco),
                issue.Move.FenBefore,
                issue.Move.Ply,
                issue.Move.MoveNumber,
                snapshot.Side,
                "Repair the first opening mistake from this game before it repeats again.",
                "Replace the played move with the stronger repair move and use the label as the study theme.",
                (issue.Move.CentipawnLoss ?? 0) + 100,
                issue.Move.MistakeLabel,
                issue.Move.San,
                FormatMove(issue.Move.FenBefore, issue.Move.BestMoveUci),
                BuildRepairReason(issue.Move),
                BuildTags(snapshot.Eco, issue.Move.MistakeLabel, "first-opening-mistake", "mistake-repair"),
                BuildRepairOptions(issue.Move, snapshot.Eco, repairIndex),
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
            sourceKind,
            snapshot.Eco,
            OpeningCatalog.Describe(snapshot.Eco),
            anchorMove.FenBefore,
            anchorMove.Ply,
            anchorMove.MoveNumber,
            snapshot.Side,
            anchorLabel,
            lineMoves.Select((move, index) => ToTrainingMove(move, index == 0 ? OpeningTrainingMoveRole.Expected : OpeningTrainingMoveRole.Continuation, index == 0)).ToList(),
            CreateReference(snapshot, sourceKind.ToString(), issue));
    }

    private static Dictionary<string, List<StoredMoveAnalysis>> BuildFenReferenceIndex(IReadOnlyList<OpeningTrainerSnapshot> snapshots)
    {
        return snapshots
            .SelectMany(snapshot => snapshot.OpeningMoves)
            .GroupBy(move => move.FenBefore, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(move => move.Ply).ToList(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> BuildLineRecallOptions(
        StoredMoveAnalysis anchorMove,
        IReadOnlyDictionary<string, List<StoredMoveAnalysis>> fenReferenceIndex)
    {
        Dictionary<string, OpeningTrainingMoveOption> options = new(StringComparer.OrdinalIgnoreCase);

        AddOrUpgradeOption(
            options,
            new OpeningTrainingMoveOption(
                anchorMove.San,
                anchorMove.Uci,
                OpeningTrainingMoveRole.Expected,
                true,
                "Reference line from your own opening game",
                OpeningLineRecallReferenceKind.ReferenceLine));

        if (!string.IsNullOrWhiteSpace(anchorMove.BestMoveUci))
        {
            AddOrUpgradeOption(
                options,
                new OpeningTrainingMoveOption(
                    FormatMove(anchorMove.FenBefore, anchorMove.BestMoveUci),
                    anchorMove.BestMoveUci,
                    OpeningTrainingMoveRole.Expected,
                    true,
                    "Best move from saved local analysis",
                    OpeningLineRecallReferenceKind.BestMove));
        }

        if (fenReferenceIndex.TryGetValue(anchorMove.FenBefore, out List<StoredMoveAnalysis>? references))
        {
            foreach (StoredMoveAnalysis referenceMove in references)
            {
                AddOrUpgradeOption(
                    options,
                    new OpeningTrainingMoveOption(
                        referenceMove.San,
                        referenceMove.Uci,
                        OpeningTrainingMoveRole.Historical,
                        false,
                        "Seen in your local opening references",
                        OpeningLineRecallReferenceKind.HistoricalGame));
            }
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
            existing.ReferenceKind ?? candidate.ReferenceKind);
    }

    private static OpeningTrainingMove ToTrainingMove(StoredMoveAnalysis move, OpeningTrainingMoveRole role, bool isPreferred)
    {
        return new OpeningTrainingMove(
            move.Ply,
            move.MoveNumber,
            move.AnalyzedSide,
            move.San,
            move.Uci,
            role,
            isPreferred,
            move.MistakeLabel);
    }

    private static Dictionary<string, List<OpeningMoveRecommendation>> BuildRepairIndex(OpeningWeaknessReport weaknessReport)
    {
        return weaknessReport.WeakOpenings
            .SelectMany(entry => entry.ExampleBetterMoves)
            .Where(item => !string.IsNullOrWhiteSpace(item.BetterMove))
            .GroupBy(item => item.FenBefore, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CentipawnLoss ?? 0)
                    .ThenBy(item => item.Ply)
                    .ToList(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> BuildRepairOptions(
        StoredMoveAnalysis move,
        string eco,
        IReadOnlyDictionary<string, List<OpeningMoveRecommendation>> repairIndex)
    {
        Dictionary<string, OpeningTrainingMoveOption> options = new(StringComparer.OrdinalIgnoreCase)
        {
            [BuildMoveKey(move.Uci, move.San)] = new(
                move.San,
                move.Uci,
                OpeningTrainingMoveRole.Alternative,
                false,
                "Played move to replace",
                OpeningLineRecallReferenceKind.HistoricalGame)
        };

        if (!string.IsNullOrWhiteSpace(move.BestMoveUci))
        {
            AddOrUpgradeOption(
                options,
                new OpeningTrainingMoveOption(
                    FormatMove(move.FenBefore, move.BestMoveUci),
                    move.BestMoveUci,
                    OpeningTrainingMoveRole.Repair,
                    true,
                    BuildRepairReason(move),
                    OpeningLineRecallReferenceKind.BetterMove));
        }

        if (repairIndex.TryGetValue(move.FenBefore, out List<OpeningMoveRecommendation>? recommendations))
        {
            foreach (OpeningMoveRecommendation recommendation in recommendations.Where(item =>
                         item.GameFingerprint != move.GameFingerprint
                         && string.Equals(item.Eco, eco, StringComparison.OrdinalIgnoreCase)))
            {
                AppliedMoveInfo? repairMove = TryResolveStoredBetterMove(move.FenBefore, recommendation.BetterMove);

                AddOrUpgradeOption(
                    options,
                    new OpeningTrainingMoveOption(
                        repairMove is null
                            ? recommendation.BetterMove
                            : ChessMoveDisplayHelper.FormatSanAndUci(repairMove.San, repairMove.Uci),
                        repairMove?.Uci,
                        OpeningTrainingMoveRole.Repair,
                        false,
                        "Playable repair seen in your opening weakness examples",
                        OpeningLineRecallReferenceKind.BetterMove));
            }
        }

        return options.Values
            .OrderByDescending(option => option.IsPreferred)
            .ThenBy(option => option.Role)
            .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AppliedMoveInfo? TryResolveStoredBetterMove(string fen, string storedBetterMove)
    {
        if (TryResolveMoveInput(fen, storedBetterMove, out AppliedMoveInfo? resolvedMove, out _)
            && resolvedMove is not null)
        {
            return resolvedMove;
        }

        int separatorIndex = storedBetterMove.LastIndexOf("(", StringComparison.Ordinal);
        if (separatorIndex < 0 || !storedBetterMove.EndsWith(")", StringComparison.Ordinal))
        {
            return null;
        }

        string uci = storedBetterMove[(separatorIndex + 1)..^1].Trim();
        return TryResolveMoveInput(fen, uci, out resolvedMove, out _)
            && resolvedMove is not null
            ? resolvedMove
            : null;
    }

    private IReadOnlyList<SavedOpeningReplay> LoadSavedOpeningReplays(IReadOnlyList<OpeningTrainerSnapshot> snapshots)
    {
        GameReplayService replayService = new();
        List<SavedOpeningReplay> replays = [];

        foreach (OpeningTrainerSnapshot snapshot in snapshots)
        {
            if (!analysisStore.TryLoadImportedGame(snapshot.GameFingerprint, out ImportedGame? game) || game is null)
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
                OpeningLineRecallReferenceKind.HistoricalGame))
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
            sample.OpponentMove.San,
            sample.OpponentMove.Uci,
            occurrences.Count,
            BuildBranchSourceSummary(occurrences.Count, exampleCount, recurringCount),
            recommendedResponse,
            continuation,
            sourceStats);
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
                : OpeningLineRecallReferenceKind.ReferenceLine);
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

    private static string BuildBranchSelectionSummary(IReadOnlyList<OpeningTrainingBranch> branches)
    {
        if (branches.Count == 0)
        {
            return "No local opponent branches were found for this setup.";
        }

        return $"Showing {branches.Count} local opponent branch(es). Ordered by saved-game frequency, then recurring-mistake support.";
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
        int loss = Math.Max(0, move.CentipawnLoss ?? 0);
        if (move.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake)
        {
            return true;
        }

        string? label = move.MistakeLabel;
        if (label is null || !FallbackLabels.Contains(label))
        {
            return move.Quality != MoveQualityBucket.Good && loss >= SignificantMistakeThresholdCp;
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
        return move.AnalyzedSide == PlayerSide.White
            ? move.BlackPlayer ?? "Unknown opponent"
            : move.WhitePlayer ?? "Unknown opponent";
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
        if (!string.IsNullOrWhiteSpace(move.TrainingHint))
        {
            return EnsureSentence(move.TrainingHint!);
        }

        if (!string.IsNullOrWhiteSpace(move.ShortExplanation))
        {
            return EnsureSentence(move.ShortExplanation!);
        }

        return move.MistakeLabel?.Trim().ToLowerInvariant() switch
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

    private static string BuildMoveKey(string? uci, string displayText)
    {
        return !string.IsNullOrWhiteSpace(uci)
            ? $"uci:{uci.Trim().ToLowerInvariant()}"
            : $"san:{SanNotation.NormalizeSan(displayText)}";
    }

    private static string BuildBetterMoveSummary(
        OpeningTrainingPosition position,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences)
    {
        if (!string.IsNullOrWhiteSpace(position.BetterMove))
        {
            return $"Better move: {position.BetterMove}.";
        }

        if (preferredReferences.Count > 0)
        {
            return $"Better move: {FormatMatchedReferences(preferredReferences)}.";
        }

        return "Better move: no saved repair move was available.";
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
