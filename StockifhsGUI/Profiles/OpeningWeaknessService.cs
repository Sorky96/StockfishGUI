using System.Globalization;

namespace StockifhsGUI;

public sealed class OpeningWeaknessService
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
    private readonly ProfileAnalysisDataSource analysisDataSource;
    private readonly OpeningTheoryQueryService? openingTheory;

    public OpeningWeaknessService(IAnalysisStore analysisStore)
        : this(analysisStore, new ProfileAnalysisDataSource(analysisStore))
    {
    }

    internal OpeningWeaknessService(IAnalysisStore analysisStore, ProfileAnalysisDataSource analysisDataSource)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
        this.analysisDataSource = analysisDataSource ?? throw new ArgumentNullException(nameof(analysisDataSource));
        openingTheory = OpeningTheorySourceResolver.Create(analysisStore);
    }

    public bool TryBuildReport(string playerKeyOrName, out OpeningWeaknessReport? report)
    {
        if (string.IsNullOrWhiteSpace(playerKeyOrName))
        {
            report = null;
            return false;
        }

        string normalized = NormalizePlayerKey(playerKeyOrName);
        List<OpeningSnapshot> snapshots = LoadSnapshots(null, 2000)
            .Where(snapshot => snapshot.PlayerKey == normalized
                || string.Equals(snapshot.DisplayName, playerKeyOrName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (snapshots.Count == 0)
        {
            report = null;
            return false;
        }

        report = BuildReport(snapshots);
        return true;
    }

    private List<OpeningSnapshot> LoadSnapshots(string? filterText, int limit)
    {
        ProfileAnalysisDataSet dataSet = analysisDataSource.Load(filterText, limit);

        List<OpeningSnapshot> mergedSnapshots = BuildSnapshotsFromMoves(dataSet.StoredMoves);
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

    private static List<OpeningSnapshot> BuildSnapshotsFromMoves(IReadOnlyList<StoredMoveAnalysis> storedMoves)
    {
        return storedMoves
            .GroupBy(move => new AnalysisVariantKey(move.GameFingerprint, move.AnalyzedSide, move.Depth, move.MultiPv, move.MoveTimeMs))
            .Select(CreateSnapshotFromMoves)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static List<OpeningSnapshot> BuildSnapshotsFromResults(IReadOnlyList<GameAnalysisResult> results)
    {
        return results
            .Select(CreateSnapshotFromResult)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static OpeningSnapshot? CreateSnapshotFromMoves(IGrouping<AnalysisVariantKey, StoredMoveAnalysis> group)
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

        return new OpeningSnapshot(
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

    private static OpeningSnapshot? CreateSnapshotFromResult(GameAnalysisResult result)
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

        return new OpeningSnapshot(
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

    private OpeningWeaknessReport BuildReport(IReadOnlyList<OpeningSnapshot> snapshots)
    {
        string playerKey = snapshots[0].PlayerKey;
        string displayName = snapshots
            .GroupBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .First();

        IReadOnlyList<OpeningWeaknessEntry> weakOpenings = snapshots
            .GroupBy(snapshot => snapshot.Eco, StringComparer.OrdinalIgnoreCase)
            .Select(BuildOpeningEntry)
            .OrderBy(item => GetCategoryOrder(item.Category))
            .ThenByDescending(item => item.Count)
            .ThenByDescending(item => item.FirstRecurringMistakeCount)
            .ThenByDescending(item => item.AverageOpeningCentipawnLoss ?? 0)
            .ThenBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<OpeningMistakeSequenceStat> recurringSequences = snapshots
            .Select(BuildSequenceCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.Key, StringComparer.Ordinal)
            .Select(group => new OpeningMistakeSequenceStat(
                group.Key,
                group.First().Labels,
                group.Count(),
                TryAverage(group.Select(item => item.FirstPly)),
                group.GroupBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(item => item.Count())
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Key)
                    .FirstOrDefault()))
            .Where(item => item.Count >= 2)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.AverageFirstPly ?? int.MaxValue)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(8)
            .ToList();

        return new OpeningWeaknessReport(
            playerKey,
            displayName,
            snapshots.Count,
            snapshots.Count,
            TryAverage(snapshots.SelectMany(snapshot => snapshot.OpeningMoves).Select(move => move.CentipawnLoss)),
            weakOpenings,
            recurringSequences);
    }

    private OpeningWeaknessEntry BuildOpeningEntry(IGrouping<string, OpeningSnapshot> group)
    {
        List<OpeningSnapshot> snapshots = group.ToList();
        string eco = NormalizeEco(group.Key);
        string openingName = snapshots
            .GroupBy(snapshot => snapshot.OpeningName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        List<OpeningIssue> issues = snapshots
            .SelectMany(snapshot => BuildIssues(snapshot).Select(issue => new OpeningIssue(snapshot, issue)))
            .ToList();

        string? firstRecurringMistakeType = issues
            .GroupBy(item => item.Move.MistakeLabel ?? "unclassified", StringComparer.Ordinal)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Min(issue => issue.Move.Ply))
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key)
            .FirstOrDefault();

        int firstRecurringMistakeCount = string.IsNullOrWhiteSpace(firstRecurringMistakeType)
            ? 0
            : issues.Count(item => string.Equals(item.Move.MistakeLabel ?? "unclassified", firstRecurringMistakeType, StringComparison.Ordinal));
        int? averageOpeningCentipawnLoss = TryAverage(snapshots.SelectMany(snapshot => snapshot.OpeningMoves).Select(move => move.CentipawnLoss));
        ProfileProgressDirection trendDirection = BuildOpeningTrend(snapshots);
        OpeningWeaknessCategory category = ClassifyOpening(
            snapshots.Count,
            averageOpeningCentipawnLoss,
            firstRecurringMistakeCount,
            trendDirection);
        string categoryReason = BuildCategoryReason(category, snapshots.Count, averageOpeningCentipawnLoss, firstRecurringMistakeCount, trendDirection);

        IReadOnlyList<OpeningMistakeSequenceStat> sequences = snapshots
            .Select(BuildSequenceCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.Key, StringComparer.Ordinal)
            .Select(grouped => new OpeningMistakeSequenceStat(
                grouped.Key,
                grouped.First().Labels,
                grouped.Count(),
                TryAverage(grouped.Select(item => item.FirstPly)),
                eco))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.AverageFirstPly ?? int.MaxValue)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        IReadOnlyList<OpeningExampleGame> exampleGames = issues
            .GroupBy(item => item.Snapshot.GameFingerprint, StringComparer.Ordinal)
            .Select(grouped => grouped
                .OrderByDescending(item => item.Move.CentipawnLoss ?? 0)
                .ThenBy(item => item.Move.Ply)
                .First())
            .OrderByDescending(item => item.Move.CentipawnLoss ?? 0)
            .ThenBy(item => item.Move.Ply)
            .Take(3)
            .Select(item => new OpeningExampleGame(
                item.Snapshot.GameFingerprint,
                item.Snapshot.Side,
                item.Snapshot.OpponentName,
                item.Snapshot.DateText,
                item.Snapshot.Result,
                eco,
                OpeningCatalog.Describe(eco),
                item.Move.Ply,
                item.Move.San,
                item.Move.MistakeLabel,
                item.Move.CentipawnLoss))
            .ToList();

        IReadOnlyList<OpeningMoveRecommendation> betterMoves = issues
            .OrderByDescending(item => item.Move.CentipawnLoss ?? 0)
            .ThenBy(item => item.Move.Ply)
            .GroupBy(item => $"{item.Snapshot.GameFingerprint}|{item.Move.Ply}", StringComparer.Ordinal)
            .Select(grouped => grouped.First())
            .Select(item => TryCreateOpeningMoveRecommendation(item, eco))
            .Where(item => item is not null)
            .Select(item => item!)
            .Take(3)
            .ToList();

        return new OpeningWeaknessEntry(
            eco,
            openingName,
            OpeningCatalog.Describe(eco),
            snapshots.Count,
            averageOpeningCentipawnLoss,
            firstRecurringMistakeType,
            firstRecurringMistakeCount,
            category,
            trendDirection,
            categoryReason,
            sequences,
            exampleGames,
            betterMoves);
    }

    private static IReadOnlyList<StoredMoveAnalysis> BuildIssues(OpeningSnapshot snapshot)
    {
        return snapshot.OpeningMoves
            .Where(IsOpeningIssue)
            .OrderBy(move => move.Ply)
            .ToList();
    }

    private static SequenceCandidate? BuildSequenceCandidate(OpeningSnapshot snapshot)
    {
        List<string> labels = BuildSequenceLabels(snapshot);
        if (labels.Count == 0)
        {
            return null;
        }

        return new SequenceCandidate(
            string.Join(" -> ", labels),
            labels,
            snapshot.Eco,
            snapshot.OpeningMoves
                .Where(IsOpeningIssue)
                .Select(move => (int?)move.Ply)
                .FirstOrDefault());
    }

    private static List<string> BuildSequenceLabels(OpeningSnapshot snapshot)
    {
        List<string> labels = [];

        foreach (StoredMoveAnalysis move in snapshot.OpeningMoves.Where(IsOpeningIssue).OrderBy(move => move.Ply))
        {
            string label = move.MistakeLabel ?? QualityToLabel(move.Quality);
            if (labels.Count > 0 && string.Equals(labels[^1], label, StringComparison.Ordinal))
            {
                continue;
            }

            labels.Add(label);
            if (labels.Count == 3)
            {
                break;
            }
        }

        return labels;
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

    private static string QualityToLabel(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => "blunder",
            MoveQualityBucket.Mistake => "mistake",
            MoveQualityBucket.Inaccuracy => "inaccuracy",
            _ => "opening_issue"
        };
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

    private static int? TryAverage(IEnumerable<int?> values)
    {
        List<int> knownValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return knownValues.Count == 0
            ? null
            : (int)Math.Round(knownValues.Average());
    }

    private static ProfileProgressDirection BuildOpeningTrend(IReadOnlyList<OpeningSnapshot> snapshots)
    {
        List<OpeningSnapshot> ordered = snapshots
            .OrderBy(snapshot => ParseGameDate(snapshot.DateText) ?? snapshot.AnalysisUpdatedUtc)
            .ThenBy(snapshot => snapshot.GameFingerprint, StringComparer.Ordinal)
            .ToList();

        int window = Math.Min(3, ordered.Count / 2);
        if (window < 2)
        {
            return ProfileProgressDirection.InsufficientData;
        }

        List<OpeningSnapshot> previous = ordered
            .Skip(Math.Max(0, ordered.Count - (window * 2)))
            .Take(window)
            .ToList();
        List<OpeningSnapshot> recent = ordered
            .TakeLast(window)
            .ToList();

        int? previousAverageCpl = TryAverage(previous.SelectMany(snapshot => snapshot.OpeningMoves).Select(move => move.CentipawnLoss));
        int? recentAverageCpl = TryAverage(recent.SelectMany(snapshot => snapshot.OpeningMoves).Select(move => move.CentipawnLoss));
        double previousIssueRate = previous.Count == 0 ? 0.0 : (double)previous.Count(snapshot => BuildIssues(snapshot).Count > 0) / previous.Count;
        double recentIssueRate = recent.Count == 0 ? 0.0 : (double)recent.Count(snapshot => BuildIssues(snapshot).Count > 0) / recent.Count;

        int cplDelta = (recentAverageCpl ?? 0) - (previousAverageCpl ?? 0);
        double issueRateDelta = recentIssueRate - previousIssueRate;

        if (recentAverageCpl.HasValue && previousAverageCpl.HasValue && (cplDelta >= 25 || (cplDelta >= 15 && issueRateDelta >= 0.4)))
        {
            return ProfileProgressDirection.Regressing;
        }

        if (recentAverageCpl.HasValue && previousAverageCpl.HasValue && (cplDelta <= -25 || (cplDelta <= -15 && issueRateDelta <= -0.4)))
        {
            return ProfileProgressDirection.Improving;
        }

        return ProfileProgressDirection.Stable;
    }

    private static OpeningWeaknessCategory ClassifyOpening(
        int frequency,
        int? averageOpeningCentipawnLoss,
        int firstRecurringMistakeCount,
        ProfileProgressDirection trendDirection)
    {
        int cpl = averageOpeningCentipawnLoss ?? 0;

        if ((frequency >= 2 && (cpl >= 90 || firstRecurringMistakeCount >= 2))
            || (trendDirection == ProfileProgressDirection.Regressing && cpl >= 70)
            || (frequency == 1 && cpl >= 140))
        {
            return OpeningWeaknessCategory.FixNow;
        }

        if ((frequency >= 2 && (cpl >= 50 || firstRecurringMistakeCount > 0))
            || cpl >= 70
            || firstRecurringMistakeCount > 0
            || trendDirection == ProfileProgressDirection.Regressing)
        {
            return OpeningWeaknessCategory.ReviewLater;
        }

        return OpeningWeaknessCategory.Stable;
    }

    private static string BuildCategoryReason(
        OpeningWeaknessCategory category,
        int frequency,
        int? averageOpeningCentipawnLoss,
        int firstRecurringMistakeCount,
        ProfileProgressDirection trendDirection)
    {
        string cpl = averageOpeningCentipawnLoss?.ToString() ?? "n/a";
        string trend = trendDirection == ProfileProgressDirection.InsufficientData
            ? "trend unavailable"
            : $"trend {trendDirection.ToString().ToLowerInvariant()}";

        return category switch
        {
            OpeningWeaknessCategory.FixNow => $"Opening to fix now: frequency {frequency}, avg opening CPL {cpl}, first recurring mistake count {firstRecurringMistakeCount}, {trend}.",
            OpeningWeaknessCategory.ReviewLater => $"Opening to review later: frequency {frequency}, avg opening CPL {cpl}, first recurring mistake count {firstRecurringMistakeCount}, {trend}.",
            _ => $"Opening stable: frequency {frequency}, avg opening CPL {cpl}, first recurring mistake count {firstRecurringMistakeCount}, {trend}."
        };
    }

    private static int GetCategoryOrder(OpeningWeaknessCategory category)
    {
        return category switch
        {
            OpeningWeaknessCategory.FixNow => 0,
            OpeningWeaknessCategory.ReviewLater => 1,
            OpeningWeaknessCategory.Stable => 2,
            _ => 3
        };
    }

    private static DateTime? ParseGameDate(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy.MM.dd",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM",
            "yyyy-MM",
            "yyyy/MM"
        ];

        return DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : null;
    }

    private OpeningMoveRecommendation? TryCreateOpeningMoveRecommendation(OpeningIssue issue, string eco)
    {
        string? betterMove = TryFormatTheoryMove(issue.Move.FenBefore);
        if (string.IsNullOrWhiteSpace(betterMove))
        {
            return null;
        }

        return new OpeningMoveRecommendation(
            issue.Snapshot.GameFingerprint,
            issue.Snapshot.Side,
            eco,
            issue.Move.Ply,
            issue.Move.MoveNumber,
            issue.Move.San,
            betterMove,
            issue.Move.MistakeLabel,
            issue.Move.CentipawnLoss,
            issue.Move.FenBefore);
    }

    private string? TryFormatTheoryMove(string fenBefore)
    {
        if (openingTheory is null || string.IsNullOrWhiteSpace(fenBefore))
        {
            return null;
        }

        OpeningTheoryMove? theoryMove = openingTheory.GetMainMoveForFen(fenBefore)
            ?? openingTheory.GetTopMovesForFen(fenBefore, limit: 1, playableOnly: false).FirstOrDefault();
        if (theoryMove is null || string.IsNullOrWhiteSpace(theoryMove.MoveUci))
        {
            return null;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(theoryMove.MoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return theoryMove.MoveUci;
        }

        return ChessMoveDisplayHelper.FormatSanAndUci(appliedMove.San, appliedMove.Uci);
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

    private sealed record OpeningSnapshot(
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

    private sealed record OpeningIssue(
        OpeningSnapshot Snapshot,
        StoredMoveAnalysis Move);

    private sealed record SequenceCandidate(
        string Key,
        IReadOnlyList<string> Labels,
        string Eco,
        int? FirstPly);
}
