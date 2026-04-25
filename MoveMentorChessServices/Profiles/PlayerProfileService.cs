using System.Globalization;

namespace MoveMentorChessServices;

public sealed partial class PlayerProfileService
{
    private readonly IAnalysisStore analysisStore;
    private readonly ProfileAnalysisDataSource analysisDataSource;

    public PlayerProfileService(IAnalysisStore analysisStore)
        : this(analysisStore, new ProfileAnalysisDataSource(analysisStore))
    {
    }

    internal PlayerProfileService(IAnalysisStore analysisStore, ProfileAnalysisDataSource analysisDataSource)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
        this.analysisDataSource = analysisDataSource ?? throw new ArgumentNullException(nameof(analysisDataSource));
    }

    public IReadOnlyList<PlayerProfileSummary> ListProfiles(string? filterText = null, int limit = 100)
    {
        List<PlayerProfileSnapshot> snapshots = LoadSnapshots(filterText, Math.Max(limit * 8, 200));
        return snapshots
            .GroupBy(snapshot => snapshot.PlayerKey)
            .Select(BuildSummary)
            .OrderByDescending(summary => summary.GamesAnalyzed)
            .ThenByDescending(summary => summary.HighlightedMistakes)
            .ThenBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public ProfileDataAvailability GetDataAvailability(string? filterText = null)
    {
        int importedGames = analysisStore.ListImportedGames(filterText, 1).Count;
        int analyzedProfiles = ListProfiles(filterText, 1).Count;
        int openingTreePositions = analysisStore is IOpeningTreeStore openingTreeStore
            ? openingTreeStore.GetOpeningTreeSummary().NodeCount
            : 0;

        return new ProfileDataAvailability(importedGames, analyzedProfiles, openingTreePositions);
    }

    public bool TryBuildProfile(string playerKeyOrName, out PlayerProfileReport? report)
    {
        if (string.IsNullOrWhiteSpace(playerKeyOrName))
        {
            report = null;
            return false;
        }

        string normalized = NormalizePlayerKey(playerKeyOrName);
        List<PlayerProfileSnapshot> snapshots = LoadSnapshots(null, 2000)
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

    public bool TryBuildOpeningWeaknessReport(string playerKeyOrName, out OpeningWeaknessReport? report)
    {
        return new OpeningWeaknessService(analysisStore, analysisDataSource).TryBuildReport(playerKeyOrName, out report);
    }

    public bool TryBuildOpeningTrainingSession(
        string playerKeyOrName,
        out OpeningTrainingSession? session,
        OpeningTrainingSessionOptions? options = null)
    {
        return new OpeningTrainerService(analysisStore, analysisDataSource).TryBuildSession(playerKeyOrName, out session, options);
    }

    private List<PlayerProfileSnapshot> LoadSnapshots(string? filterText, int limit)
    {
        ProfileAnalysisDataSet dataSet = analysisDataSource.Load(filterText, limit);

        List<PlayerProfileSnapshot> mergedSnapshots = BuildSnapshotsFromMoves(dataSet.StoredMoves);
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

    private static List<PlayerProfileSnapshot> BuildSnapshotsFromMoves(IReadOnlyList<StoredMoveAnalysis> storedMoves)
    {
        return storedMoves
            .GroupBy(move => new AnalysisVariantKey(move.GameFingerprint, move.AnalyzedSide, move.Depth, move.MultiPv, move.MoveTimeMs))
            .Select(CreateSnapshotFromMoves)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static List<PlayerProfileSnapshot> BuildSnapshotsFromResults(IReadOnlyList<GameAnalysisResult> results)
    {
        return results
            .Select(CreateSnapshotFromResult)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToList();
    }

    private static PlayerProfileSummary BuildSummary(IGrouping<string, PlayerProfileSnapshot> group)
    {
        string displayName = group
            .GroupBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<string> topLabels = group
            .SelectMany(GetHighlightedGroups)
            .GroupBy(item => item.Label)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Key)
            .ToList();

        int? averageCpl = TryAverage(group.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss));

        return new PlayerProfileSummary(
            group.Key,
            displayName,
            group.Count(),
            group.Sum(snapshot => GetHighlightedGroups(snapshot).Count),
            averageCpl,
            topLabels);
    }

    private static IReadOnlyList<PriorityLabelStat> BuildPriorityLabels(
        IReadOnlyList<ProfileLabelStat> topLabels,
        IReadOnlyList<ProfileCostlyLabelStat> costliestLabels,
        IReadOnlyList<HighlightedGroup> highlightedGroups,
        IReadOnlyList<StoredMoveAnalysis> mistakeMoves)
    {
        Dictionary<string, ProfileLabelStat> frequentByLabel = topLabels
            .ToDictionary(item => item.Label, StringComparer.Ordinal);
        Dictionary<string, ProfileCostlyLabelStat> costlyByLabel = costliestLabels
            .ToDictionary(item => item.Label, StringComparer.Ordinal);
        Dictionary<string, int> highlightedCounts = highlightedGroups
            .GroupBy(group => group.Label, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return mistakeMoves
            .GroupBy(move => move.MistakeLabel!, StringComparer.Ordinal)
            .Select(group =>
            {
                int count = group.Count();
                int totalCpl = group.Sum(move => Math.Max(0, move.CentipawnLoss ?? 0));
                int? averageCpl = TryAverage(group.Select(move => move.CentipawnLoss));
                double averageConfidence = group
                    .Where(move => move.MistakeConfidence.HasValue)
                    .Select(move => move.MistakeConfidence!.Value)
                    .DefaultIfEmpty(0.0)
                    .Average();
                int frequencyBoost = frequentByLabel.TryGetValue(group.Key, out ProfileLabelStat? frequent)
                    ? frequent.Count * 80
                    : count * 40;
                int costlyBoost = costlyByLabel.TryGetValue(group.Key, out ProfileCostlyLabelStat? costly)
                    ? costly.TotalCentipawnLoss
                    : totalCpl;
                int highlightBoost = highlightedCounts.TryGetValue(group.Key, out int highlightedCount)
                    ? highlightedCount * 90
                    : 0;
                int priorityScore = frequencyBoost + costlyBoost + highlightBoost + (int)Math.Round(averageConfidence * 40);

                return new PriorityLabelStat(group.Key, count, totalCpl, averageCpl, averageConfidence, priorityScore);
            })
            .OrderByDescending(item => item.PriorityScore)
            .ThenByDescending(item => item.TotalCentipawnLoss)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(3)
            .ToList();
    }

    private static ProfileProgressSignal BuildProgressSignal(IReadOnlyList<PlayerProfileSnapshot> snapshots)
    {
        List<PlayerProfileSnapshot> ordered = snapshots
            .OrderBy(snapshot => snapshot.GameDate ?? snapshot.AnalysisUpdatedUtc)
            .ThenBy(snapshot => snapshot.GameFingerprint, StringComparer.Ordinal)
            .ToList();

        int window = Math.Min(5, ordered.Count / 2);
        if (window < 2)
        {
            return new ProfileProgressSignal(
                ProfileProgressDirection.InsufficientData,
                "Not enough dated games yet to compare recent form against earlier results.",
                null,
                null);
        }

        List<PlayerProfileSnapshot> previous = ordered
            .Skip(Math.Max(0, ordered.Count - (window * 2)))
            .Take(window)
            .ToList();
        List<PlayerProfileSnapshot> recent = ordered
            .TakeLast(window)
            .ToList();

        ProfileProgressPeriod previousPeriod = BuildProgressPeriod(previous, "Earlier sample");
        ProfileProgressPeriod recentPeriod = BuildProgressPeriod(recent, "Recent sample");

        int cplDelta = (recentPeriod.AverageCentipawnLoss ?? 0) - (previousPeriod.AverageCentipawnLoss ?? 0);
        double highlightDelta = recentPeriod.HighlightedMistakesPerGame - previousPeriod.HighlightedMistakesPerGame;

        if ((previousPeriod.AverageCentipawnLoss is null || recentPeriod.AverageCentipawnLoss is null) && previous.Count < 2)
        {
            return new ProfileProgressSignal(
                ProfileProgressDirection.InsufficientData,
                "Not enough reliable data to measure progress yet.",
                recentPeriod,
                previousPeriod);
        }

        ProfileProgressDirection direction;
        string summary;
        if (cplDelta <= -35 || (cplDelta <= -25 && highlightDelta <= -0.15))
        {
            direction = ProfileProgressDirection.Improving;
            summary = $"Recent games are cleaner: average CPL improved by {Math.Abs(cplDelta)} and highlighted mistakes per game also dropped.";
        }
        else if (cplDelta >= 35 || (cplDelta >= 25 && highlightDelta >= 0.15))
        {
            direction = ProfileProgressDirection.Regressing;
            summary = $"Recent games are rougher: average CPL rose by {cplDelta} and the number of highlighted mistakes per game increased.";
        }
        else
        {
            direction = ProfileProgressDirection.Stable;
            summary = "Recent results are broadly stable versus the earlier sample, with no strong improvement or regression signal yet.";
        }

        return new ProfileProgressSignal(direction, summary, recentPeriod, previousPeriod);
    }

    private static IReadOnlyList<ProfileLabelTrend> BuildLabelTrends(IReadOnlyList<PlayerProfileSnapshot> snapshots)
    {
        List<PlayerProfileSnapshot> ordered = snapshots
            .OrderBy(snapshot => snapshot.GameDate ?? snapshot.AnalysisUpdatedUtc)
            .ThenBy(snapshot => snapshot.GameFingerprint, StringComparer.Ordinal)
            .ToList();

        int window = Math.Min(5, ordered.Count / 2);
        if (window < 2)
        {
            return [];
        }

        List<PlayerProfileSnapshot> previous = ordered
            .Skip(Math.Max(0, ordered.Count - (window * 2)))
            .Take(window)
            .ToList();
        List<PlayerProfileSnapshot> recent = ordered
            .TakeLast(window)
            .ToList();

        return snapshots
            .SelectMany(snapshot => snapshot.Moves)
            .Where(move => move.Quality != MoveQualityBucket.Good && !string.IsNullOrWhiteSpace(move.MistakeLabel))
            .Select(move => move.MistakeLabel!)
            .Distinct(StringComparer.Ordinal)
            .Select(label => BuildLabelTrend(label, previous, recent))
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static ProfileLabelTrend BuildLabelTrend(
        string label,
        IReadOnlyList<PlayerProfileSnapshot> previousSnapshots,
        IReadOnlyList<PlayerProfileSnapshot> recentSnapshots)
    {
        List<StoredMoveAnalysis> previousMoves = GetMovesForLabel(previousSnapshots, label);
        List<StoredMoveAnalysis> recentMoves = GetMovesForLabel(recentSnapshots, label);

        int previousCount = previousMoves.Count;
        int recentCount = recentMoves.Count;
        int totalCount = previousCount + recentCount;
        int? previousAverageCpl = TryAverage(previousMoves.Select(move => move.CentipawnLoss));
        int? recentAverageCpl = TryAverage(recentMoves.Select(move => move.CentipawnLoss));

        ProfileProgressDirection direction;
        if (totalCount < 2)
        {
            direction = ProfileProgressDirection.InsufficientData;
        }
        else if (previousCount == 0 && recentCount >= 2)
        {
            direction = ProfileProgressDirection.Regressing;
        }
        else if (recentCount == 0 && previousCount >= 2)
        {
            direction = ProfileProgressDirection.Improving;
        }
        else
        {
            int countDelta = recentCount - previousCount;
            int cplDelta = (recentAverageCpl ?? 0) - (previousAverageCpl ?? 0);

            bool worseningByFrequency = countDelta >= 1;
            bool improvingByFrequency = countDelta <= -1;
            bool worseningByCost = recentAverageCpl.HasValue && previousAverageCpl.HasValue && cplDelta >= 25;
            bool improvingByCost = recentAverageCpl.HasValue && previousAverageCpl.HasValue && cplDelta <= -25;

            if (worseningByFrequency || (countDelta >= 0 && worseningByCost))
            {
                direction = ProfileProgressDirection.Regressing;
            }
            else if (improvingByFrequency || (countDelta <= 0 && improvingByCost))
            {
                direction = ProfileProgressDirection.Improving;
            }
            else
            {
                direction = ProfileProgressDirection.Stable;
            }
        }

        return new ProfileLabelTrend(
            label,
            direction,
            recentCount,
            previousCount,
            recentAverageCpl,
            previousAverageCpl);
    }

    private static List<StoredMoveAnalysis> GetMovesForLabel(IReadOnlyList<PlayerProfileSnapshot> snapshots, string label)
    {
        return snapshots
            .SelectMany(snapshot => snapshot.Moves)
            .Where(move =>
                move.Quality != MoveQualityBucket.Good
                && string.Equals(move.MistakeLabel, label, StringComparison.Ordinal))
            .ToList();
    }

    private static ProfileProgressPeriod BuildProgressPeriod(IReadOnlyList<PlayerProfileSnapshot> snapshots, string label)
    {
        int highlightedMistakes = snapshots.Sum(snapshot => GetHighlightedGroups(snapshot).Count);
        double highlightsPerGame = snapshots.Count == 0
            ? 0.0
            : Math.Round((double)highlightedMistakes / snapshots.Count, 2);

        return new ProfileProgressPeriod(
            label,
            snapshots.Count,
            TryAverage(snapshots.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss)),
            highlightsPerGame);
    }

    private PlayerProfileReport BuildReport(IReadOnlyList<PlayerProfileSnapshot> snapshots)
    {
        string playerKey = snapshots[0].PlayerKey;
        string displayName = snapshots
            .GroupBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<HighlightedGroup> highlightedGroups = snapshots
            .SelectMany(GetHighlightedGroups)
            .ToList();
        IReadOnlyList<StoredMoveAnalysis> mistakeMoves = snapshots
            .SelectMany(snapshot => snapshot.Moves)
            .Where(move => move.Quality != MoveQualityBucket.Good && !string.IsNullOrWhiteSpace(move.MistakeLabel))
            .ToList();

        IReadOnlyList<ProfileLabelStat> topLabels = highlightedGroups
            .GroupBy(item => item.Label)
            .Select(group => new ProfileLabelStat(
                group.Key,
                group.Count(),
                group.Average(item => item.AverageConfidence)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.AverageConfidence)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        IReadOnlyList<ProfileCostlyLabelStat> costliestLabels = mistakeMoves
            .GroupBy(move => move.MistakeLabel!, StringComparer.Ordinal)
            .Select(group => new ProfileCostlyLabelStat(
                group.Key,
                group.Count(),
                group.Sum(move => Math.Max(0, move.CentipawnLoss ?? 0)),
                TryAverage(group.Select(move => move.CentipawnLoss))))
            .OrderByDescending(item => item.TotalCentipawnLoss)
            .ThenByDescending(item => item.AverageCentipawnLoss ?? 0)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        IReadOnlyList<ProfilePhaseStat> mistakesByPhase = snapshots
            .SelectMany(snapshot => snapshot.Moves)
            .Where(move => move.Quality != MoveQualityBucket.Good)
            .GroupBy(move => move.Phase)
            .Select(group => new ProfilePhaseStat(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Phase)
            .ToList();

        IReadOnlyList<ProfileOpeningStat> mistakesByOpening = snapshots
            .SelectMany(snapshot => snapshot.Moves
                .Where(move => move.Quality != MoveQualityBucket.Good)
                .Select(_ => snapshot.Eco))
            .GroupBy(eco => eco, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProfileOpeningStat(group.First(), group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        IReadOnlyList<ProfileSideStat> gamesBySide = snapshots
            .GroupBy(snapshot => snapshot.Side)
            .Select(group => new ProfileSideStat(
                group.Key,
                group.Count(),
                group.Sum(snapshot => GetHighlightedGroups(snapshot).Count)))
            .OrderBy(item => item.Side)
            .ToList();

        IReadOnlyList<ProfileMonthlyTrend> monthlyTrend = snapshots
            .GroupBy(snapshot => snapshot.MonthKey ?? "Unknown")
            .Select(group => new ProfileMonthlyTrend(
                group.Key,
                group.Count(),
                group.Sum(snapshot => GetHighlightedGroups(snapshot).Count),
                TryAverage(group.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss))))
            .OrderBy(item => item.MonthKey, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<ProfileQuarterlyTrend> quarterlyTrend = snapshots
            .GroupBy(snapshot => snapshot.QuarterKey ?? "Unknown")
            .Select(group => new ProfileQuarterlyTrend(
                group.Key,
                group.Count(),
                group.Sum(snapshot => GetHighlightedGroups(snapshot).Count),
                TryAverage(group.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss))))
            .OrderBy(item => item.QuarterKey, StringComparer.Ordinal)
            .ToList();

        ProfileProgressSignal progressSignal = BuildProgressSignal(snapshots);
        IReadOnlyList<ProfileLabelTrend> labelTrends = BuildLabelTrends(snapshots);

        IReadOnlyList<ProfileMistakeExample> allExamples = BuildAllMistakeExamples(snapshots, topLabels, 9);

        WeeklyTrainingPlan emptyWeeklyPlan = new(
            $"{displayName} Weekly Training Plan",
            "Training plan is being prepared from the current profile.",
            new WeeklyTrainingBudget(
                0,
                0,
                0,
                0,
                0,
                "Budget will be calculated after training priorities are ranked."),
            []);
        PlayerProfileReport draftReport = new(
            playerKey,
            displayName,
            snapshots.Count,
            snapshots.Sum(snapshot => snapshot.Moves.Count),
            highlightedGroups.Count,
            TryAverage(snapshots.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss)),
            topLabels,
            costliestLabels,
            mistakesByPhase,
            mistakesByOpening,
            gamesBySide,
            monthlyTrend,
            quarterlyTrend,
            progressSignal,
            labelTrends,
            [],
            emptyWeeklyPlan,
            allExamples,
            new TrainingPlanReport(
                playerKey,
                displayName,
                progressSignal.Direction,
                "Training plan is being prepared from the current profile.",
                [],
                [],
                emptyWeeklyPlan));
        OpeningWeaknessReport? openingReport = new OpeningWeaknessService(analysisStore, analysisDataSource)
            .TryBuildReport(playerKey, out OpeningWeaknessReport? builtOpeningReport)
                ? builtOpeningReport
                : null;
        TrainingPlanReport trainingPlan = new TrainingPlanService().Build(draftReport, openingReport);

        return new PlayerProfileReport(
            playerKey,
            displayName,
            snapshots.Count,
            snapshots.Sum(snapshot => snapshot.Moves.Count),
            highlightedGroups.Count,
            TryAverage(snapshots.SelectMany(snapshot => snapshot.Moves).Select(move => move.CentipawnLoss)),
            topLabels,
            costliestLabels,
            mistakesByPhase,
            mistakesByOpening,
            gamesBySide,
            monthlyTrend,
            quarterlyTrend,
            progressSignal,
            labelTrends,
            trainingPlan.Recommendations,
            trainingPlan.WeeklyPlan,
            allExamples,
            trainingPlan);
    }
}

public sealed record ProfileDataAvailability(
    int ImportedGames,
    int AnalyzedProfiles,
    int OpeningTreePositions);

public sealed partial class PlayerProfileService
{
    private static PlayerProfileSnapshot? CreateSnapshotFromMoves(IGrouping<AnalysisVariantKey, StoredMoveAnalysis> group)
    {
        List<StoredMoveAnalysis> moves = group
            .OrderBy(move => move.Ply)
            .ToList();

        StoredMoveAnalysis first = moves[0];
        string? playerName = first.AnalyzedSide == PlayerSide.White
            ? first.WhitePlayer
            : first.BlackPlayer;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        return new PlayerProfileSnapshot(
            first.GameFingerprint,
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            first.AnalyzedSide,
            ParseGameDate(first.DateText),
            ParseMonthKey(first.DateText),
            ParseQuarterKey(first.DateText),
            string.IsNullOrWhiteSpace(first.Eco) ? "Unknown" : first.Eco!,
            first.Depth,
            first.MultiPv,
            first.MoveTimeMs,
            first.AnalysisUpdatedUtc,
            moves);
    }

    private static PlayerProfileSnapshot? CreateSnapshotFromResult(GameAnalysisResult result)
    {
        string? playerName = result.AnalyzedSide == PlayerSide.White
            ? result.Game.WhitePlayer
            : result.Game.BlackPlayer;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        HashSet<int> highlightedPlys = result.HighlightedMistakes
            .SelectMany(mistake => mistake.Moves)
            .Select(move => move.Replay.Ply)
            .ToHashSet();

        IReadOnlyList<StoredMoveAnalysis> moves = result.MoveAnalyses
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
                highlightedPlys.Contains(move.Replay.Ply)))
            .ToList();

        return new PlayerProfileSnapshot(
            GameFingerprint.Compute(result.Game.PgnText),
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            result.AnalyzedSide,
            ParseGameDate(result.Game.DateText),
            ParseMonthKey(result.Game.DateText),
            ParseQuarterKey(result.Game.DateText),
            string.IsNullOrWhiteSpace(result.Game.Eco) ? "Unknown" : result.Game.Eco!,
            0,
            0,
            null,
            DateTime.MinValue,
            moves);
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

    private static string NormalizePlayerKey(string playerName)
    {
        return playerName.Trim().ToLowerInvariant();
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

    private static string? ParseMonthKey(string? dateText)
    {
        DateTime? parsed = ParseGameDate(dateText);
        return parsed?.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private static string? ParseQuarterKey(string? dateText)
    {
        DateTime? parsed = ParseGameDate(dateText);
        if (parsed.HasValue)
        {
            int quarter = ((parsed.Value.Month - 1) / 3) + 1;
            return $"{parsed.Value:yyyy}-Q{quarter}";
        }

        return null;
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

    private sealed record PlayerProfileSnapshot(
        string GameFingerprint,
        string PlayerKey,
        string DisplayName,
        PlayerSide Side,
        DateTime? GameDate,
        string? MonthKey,
        string? QuarterKey,
        string Eco,
        int Depth,
        int MultiPv,
        int? MoveTimeMs,
        DateTime AnalysisUpdatedUtc,
        IReadOnlyList<StoredMoveAnalysis> Moves);

    private sealed record HighlightedGroup(
        string Label,
        double AverageConfidence,
        GamePhase? DominantPhase,
        MoveQualityBucket Quality);

    private sealed record RecommendationContext(
        GamePhase? DominantPhase,
        PlayerSide? DominantSide,
        IReadOnlyList<string> TopOpenings);

    private sealed record RecommendationOccurrence(
        PlayerSide Side,
        GamePhase? Phase,
        string Eco);

    private sealed record MistakeExampleCandidate(
        PlayerProfileSnapshot Snapshot,
        StoredMoveAnalysis Move);

    private readonly record struct ExampleClusterKey(
        GamePhase Phase,
        string Eco);

    private sealed record PriorityLabelStat(
        string Label,
        int Count,
        int TotalCentipawnLoss,
        int? AverageCentipawnLoss,
        double AverageConfidence,
        int PriorityScore);

    private sealed class ExampleClusterKeyComparer : IEqualityComparer<ExampleClusterKey>
    {
        public static ExampleClusterKeyComparer Instance { get; } = new();

        public bool Equals(ExampleClusterKey x, ExampleClusterKey y)
        {
            return x.Phase == y.Phase
                && string.Equals(x.Eco, y.Eco, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ExampleClusterKey obj)
        {
            return HashCode.Combine(obj.Phase, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Eco ?? string.Empty));
        }
    }
}

public sealed partial class PlayerProfileService
{
    private static TrainingRecommendation CreateFallbackRecommendation()
    {
        return new TrainingRecommendation(
            1,
            "General review",
            "Review Critical Moments",
            "No dominant error pattern has been identified yet, so the next best step is a short weekly cycle built around your biggest evaluation swings.",
            null,
            null,
            [],
            [
                "Stop at every large evaluation swing and explain what should have been checked first.",
                "Keep the review focused on one simple question per move."
            ],
            [
                "Replay one recent game and pause before every critical decision.",
                "Collect 5 positions that felt unclear and review them slowly."
            ]);
    }

    private static IReadOnlyList<RecommendationOccurrence> BuildRecommendationOccurrences(PlayerProfileSnapshot snapshot, string label)
    {
        List<RecommendationOccurrence> highlightedOccurrences = GetHighlightedGroups(snapshot)
            .Where(group => string.Equals(group.Label, label, StringComparison.Ordinal))
            .Select(group => new RecommendationOccurrence(snapshot.Side, group.DominantPhase, snapshot.Eco))
            .ToList();

        if (highlightedOccurrences.Any(item => item.Phase.HasValue))
        {
            return highlightedOccurrences;
        }

        List<RecommendationOccurrence> moveOccurrences = snapshot.Moves
            .Where(move => string.Equals(move.MistakeLabel ?? "unclassified", label, StringComparison.Ordinal))
            .Select(move => new RecommendationOccurrence(snapshot.Side, move.Phase, snapshot.Eco))
            .ToList();

        return moveOccurrences.Count > 0
            ? moveOccurrences
            : highlightedOccurrences;
    }

    private static IReadOnlyList<HighlightedGroup> GetHighlightedGroups(PlayerProfileSnapshot snapshot)
    {
        List<StoredMoveAnalysis> highlightedMoves = snapshot.Moves
            .Where(move => move.IsHighlighted)
            .OrderBy(move => move.Ply)
            .ToList();

        if (highlightedMoves.Count == 0)
        {
            return [];
        }

        List<HighlightedGroup> groups = [];
        List<StoredMoveAnalysis> currentGroup = [];

        foreach (StoredMoveAnalysis move in highlightedMoves)
        {
            if (currentGroup.Count == 0 || CanMergeHighlightedMoves(currentGroup[^1], move))
            {
                currentGroup.Add(move);
                continue;
            }

            groups.Add(BuildHighlightedGroup(currentGroup));
            currentGroup = [move];
        }

        if (currentGroup.Count > 0)
        {
            groups.Add(BuildHighlightedGroup(currentGroup));
        }

        return groups;
    }

    private static bool CanMergeHighlightedMoves(StoredMoveAnalysis previous, StoredMoveAnalysis current)
    {
        return string.Equals(previous.MistakeLabel ?? "unclassified", current.MistakeLabel ?? "unclassified", StringComparison.Ordinal)
            && previous.Quality == current.Quality
            && previous.MoveNumber + 1 >= current.MoveNumber
            && previous.Phase == current.Phase;
    }

    private static HighlightedGroup BuildHighlightedGroup(IReadOnlyList<StoredMoveAnalysis> moves)
    {
        StoredMoveAnalysis lead = moves
            .OrderByDescending(move => SeverityWeight(move.Quality))
            .ThenByDescending(move => move.CentipawnLoss ?? int.MinValue)
            .ThenBy(move => move.Ply)
            .First();

        double averageConfidence = moves
            .Where(move => move.MistakeConfidence.HasValue)
            .Select(move => move.MistakeConfidence!.Value)
            .DefaultIfEmpty(0.0)
            .Average();

        GamePhase? dominantPhase = moves
            .GroupBy(move => move.Phase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (GamePhase?)group.Key)
            .FirstOrDefault();

        return new HighlightedGroup(
            lead.MistakeLabel ?? "unclassified",
            averageConfidence,
            dominantPhase,
            lead.Quality);
    }

    private static int SeverityWeight(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => 4,
            MoveQualityBucket.Mistake => 3,
            MoveQualityBucket.Inaccuracy => 2,
            _ => 1
        };
    }

    private static string BuildContextSummary(RecommendationContext context)
    {
        List<string> parts = [];

        if (context.DominantPhase.HasValue)
        {
            parts.Add($"It shows up most often in {FormatPhaseText(context.DominantPhase.Value, fallback: "that phase")}");
        }

        if (context.DominantSide.HasValue)
        {
            parts.Add($"mostly when you analyze {context.DominantSide.Value}");
        }

        if (context.TopOpenings.Count > 0)
        {
            parts.Add($"and especially in {FormatOpeningsList(context.TopOpenings, "these openings")}");
        }

        return parts.Count == 0
            ? "This is one of the most repeated patterns in your saved analyses."
            : string.Join(" ", parts) + ".";
    }

    private static string BuildOpeningSummary(IReadOnlyList<string> openings)
    {
        return openings.Count == 0
            ? "Build a small custom drill set from the openings where your own mistakes recur most."
            : $"Build a mini review pack from positions coming out of {FormatOpeningsList(openings, "your most relevant openings")}.";
    }

    private static string FormatOpeningsList(IReadOnlyList<string> openings, string fallback)
    {
        if (openings.Count == 0)
        {
            return fallback;
        }

        IReadOnlyList<string> formattedOpenings = openings
            .Select(OpeningCatalog.Describe)
            .ToList();

        return formattedOpenings.Count == 1
            ? formattedOpenings[0]
            : string.Join(" and ", formattedOpenings);
    }

    private static string FormatPhaseText(GamePhase? phase, string fallback)
    {
        return phase switch
        {
            GamePhase.Opening => "the opening",
            GamePhase.Middlegame => "the middlegame",
            GamePhase.Endgame => "the endgame",
            _ => fallback
        };
    }

    private static string BuildSideSuffix(PlayerSide? side)
    {
        return side switch
        {
            PlayerSide.White => " as White",
            PlayerSide.Black => " as Black",
            _ => string.Empty
        };
    }

    private static TrainingRecommendation GetRecommendation(IReadOnlyList<TrainingRecommendation> recommendations, int index)
    {
        return recommendations[Math.Min(index, recommendations.Count - 1)];
    }

    private static string PickOrFallback(IReadOnlyList<string> values, int index, string fallback)
    {
        return values.Count > index && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : fallback;
    }

    private static string BuildOpeningTask(TrainingRecommendation recommendation)
    {
        return recommendation.RelatedOpenings.Count == 0
            ? "Review one structure from your own recent games where this theme appeared."
            : $"Review two example positions from {string.Join(" / ", recommendation.RelatedOpenings.Select(OpeningCatalog.Describe))} and connect them to this theme.";
    }

    private static string FormatRecommendationContext(TrainingRecommendation recommendation)
    {
        List<string> parts = [];

        if (recommendation.EmphasisPhase.HasValue)
        {
            parts.Add(FormatPhaseText(recommendation.EmphasisPhase.Value, recommendation.EmphasisPhase.Value.ToString()));
        }

        if (recommendation.EmphasisSide.HasValue)
        {
            parts.Add(recommendation.EmphasisSide.Value.ToString());
        }

        if (recommendation.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(", ", recommendation.RelatedOpenings.Select(OpeningCatalog.Describe)));
        }

        return parts.Count == 0
            ? "your current profile context"
            : string.Join(" | ", parts);
    }
}

public sealed partial class PlayerProfileService
{
    private static TrainingRecommendation CreateRecommendation(
        PriorityLabelStat labelStat,
        RecommendationContext context,
        IReadOnlyList<ProfileMistakeExample> examples,
        int priority)
    {
        string contextSummary = BuildContextSummary(context);
        string severitySummary = BuildSeveritySummary(labelStat);
        string openingSummary = BuildOpeningSummary(context.TopOpenings);

        return labelStat.Label switch
        {
            "hanging_piece" => new TrainingRecommendation(
                priority,
                "Board safety",
                "Protect Loose Pieces",
                $"Your profile shows repeated piece losses after moves that leave a square underprotected. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Count attackers and defenders on the destination square before every move.",
                    "After moving a piece, ask whether the opponent can capture it for free or with tempo.",
                    "Prefer moves that keep your most valuable piece defended at least once.",
                    $"Pay extra attention in {FormatPhaseText(context.DominantPhase, fallback: "the phases where this happens most")}."
                ],
                [
                    "10-15 quick 'undefended pieces' puzzles.",
                    "Slow review of your own blunders with attacker-defender counting.",
                    $"Mini checklist game: say 'safe or loose?' before each move{BuildSideSuffix(context.DominantSide)}.",
                    openingSummary
                ],
                examples),
            "missed_tactic" => new TrainingRecommendation(
                priority,
                "Tactics",
                "Checks, Captures, Threats",
                $"You are missing forcing resources often enough that tactical scanning should become the first step of your thought process in sharp positions. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "List checks, captures and threats for both sides before selecting a move.",
                    "When the position opens, calculate forcing lines before quiet improvements.",
                    "Double-check whether the opponent has one tactical reply that changes everything.",
                    $"Be especially strict in {FormatPhaseText(context.DominantPhase, fallback: "the phase where this keeps recurring")}."
                ],
                [
                    "Short CCT puzzle sets with a clock.",
                    "Two-move tactical calculation drills from your own analyzed games.",
                    "Flashcards with forks, skewers and discovered attacks.",
                    openingSummary
                ],
                examples),
            "opening_principles" => new TrainingRecommendation(
                priority,
                "Opening discipline",
                "Clean Up The Opening",
                $"The profile shows that you give away quality early by spending tempi on side ideas before finishing development and king safety. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "In the first 10 moves, ask whether each move develops, castles or fights for the center.",
                    "Delay repeated queen or rook moves unless there is a concrete tactical reason.",
                    "Avoid wing pawn moves before your minor pieces are meaningfully developed.",
                    $"Review your typical setups{BuildSideSuffix(context.DominantSide)} in {FormatOpeningsList(context.TopOpenings, "the openings where this appears most")}."
                ],
                [
                    "Review three model openings you actually play.",
                    "Annotate the first 10 moves of your own games with 'develop / center / king safety'.",
                    "Play a few training games with a rule: no wing pawn moves before minor piece development.",
                    openingSummary
                ],
                examples),
            "king_safety" => new TrainingRecommendation(
                priority,
                "King safety",
                "Safer King Decisions",
                $"Your mistakes suggest that king shelter breaks down too easily after pawn pushes or slow reactions to attacking chances. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before pushing a pawn near your king, identify which file, diagonal or square complex gets weaker.",
                    "When castled, treat every pawn move in front of the king as a concession that needs justification.",
                    "Check the opponent's forcing moves before grabbing material on the wing.",
                    $"Recheck king shelter{BuildSideSuffix(context.DominantSide)} in {FormatPhaseText(context.DominantPhase, fallback: "the critical phase")}."
                ],
                [
                    "Model-game review focused on attacking patterns against castled kings.",
                    "Puzzle set on mating nets and defensive resources.",
                    "Post-game note: which move first weakened your king?",
                    openingSummary
                ],
                examples),
            "endgame_technique" => new TrainingRecommendation(
                priority,
                "Endgames",
                "Sharpen Endgame Technique",
                $"The profile points to technical slips in reduced material, especially around king activity and the simplest conversion path. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "In endgames, compare moves by king activity before anything else.",
                    "Prefer the cleanest line with the least counterplay, not the fanciest one.",
                    "Check whether a pawn ending or piece trade helps or hurts your winning chances.",
                    $"Pay special attention when converting{BuildSideSuffix(context.DominantSide)}."
                ],
                [
                    "Basic king-and-pawn endgame drills.",
                    "Winning rook or minor-piece endgame conversion exercises.",
                    "Replay your own endgames and mark where the king should have improved first.",
                    "Create a mini set from your own late-game mistakes and replay the conversion plan."
                ],
                examples),
            "material_loss" => new TrainingRecommendation(
                priority,
                "Material discipline",
                "Material Discipline",
                $"A recurring theme is losing material in lines where the forcing continuation was not checked deeply enough. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before moving, calculate the most forcing exchange sequence to the end.",
                    "Ask which side wins material if the board is simplified immediately.",
                    "Treat every tactical capture as suspicious until the final balance is clear.",
                    $"Use extra caution{BuildSideSuffix(context.DominantSide)} in {FormatOpeningsList(context.TopOpenings, "the recurring structures")}."
                ],
                [
                    "Capture-sequence exercises focused on material balance.",
                    "Blunder-check drill: write the final material count after each forcing line.",
                    "Review games where one missed recapture changed the result.",
                    openingSummary
                ],
                examples),
            "piece_activity" => new TrainingRecommendation(
                priority,
                "Piece coordination",
                "Improve Piece Activity",
                $"You are giving away too many useful tempi on moves that reduce mobility or coordination instead of improving the worst-placed piece. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before moving, ask which of your pieces is doing the least work.",
                    "Prefer squares that improve mobility, central control or coordination with other pieces.",
                    "Avoid edge retreats unless they solve a concrete tactical problem.",
                    $"Review this especially in {FormatPhaseText(context.DominantPhase, fallback: "the phase where these drifts appear most")}."
                ],
                [
                    "Find-the-best-square exercises for knights and bishops.",
                    "Annotate middlegame plans by identifying your worst piece each turn.",
                    "Review losses for passive regrouping moves that handed over initiative.",
                    openingSummary
                ],
                examples),
            _ => new TrainingRecommendation(
                priority,
                "Critical review",
                "Review Critical Moments",
                $"Your profile still has a mixed error picture, so the best next step is disciplined review of the positions where the evaluation turned fastest. {severitySummary} {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Stop at every big swing and identify the first missed forcing reply.",
                    "Write one sentence about what should have been checked before the move.",
                    "Group similar mistakes together instead of reviewing games one by one.",
                    $"Use {FormatOpeningsList(context.TopOpenings, "your most relevant openings")} as the first review set."
                ],
                [
                    "Mistake notebook built from your own analysis list.",
                    "Short review sessions of only the largest evaluation swings.",
                    "Theme tagging of recent blunders to find a dominant pattern.",
                    openingSummary
                ],
                examples)
        };
    }

    private static IReadOnlyList<ProfileMistakeExample> BuildMistakeExamples(
        IReadOnlyList<PlayerProfileSnapshot> snapshots,
        string label,
        RecommendationContext context,
        int maxCount)
    {
        List<MistakeExampleCandidate> candidates = BuildMistakeExampleCandidates(snapshots, label);
        if (candidates.Count == 0)
        {
            return [];
        }

        List<ProfileMistakeExample> selected = [];
        HashSet<string> selectedKeys = [];

        AddRankedExample(
            selected,
            selectedKeys,
            SelectMostFrequentExample(candidates, selectedKeys),
            ProfileMistakeExampleRank.MostFrequent);
        AddRankedExample(
            selected,
            selectedKeys,
            SelectMostCostlyExample(candidates, selectedKeys),
            ProfileMistakeExampleRank.MostCostly);
        AddRankedExample(
            selected,
            selectedKeys,
            SelectMostRepresentativeExample(candidates, context, selectedKeys),
            ProfileMistakeExampleRank.MostRepresentative);

        foreach (MistakeExampleCandidate candidate in candidates
            .OrderByDescending(item => item.Move.IsHighlighted)
            .ThenByDescending(item => item.Move.CentipawnLoss ?? 0)
            .ThenByDescending(item => SeverityWeight(item.Move.Quality))
            .ThenBy(item => item.Move.Ply))
        {
            if (selected.Count >= maxCount)
            {
                break;
            }

            AddRankedExample(
                selected,
                selectedKeys,
                candidate,
                ProfileMistakeExampleRank.MostRepresentative);
        }

        return selected
            .Take(maxCount)
            .ToList();
    }

    private static IReadOnlyList<ProfileMistakeExample> BuildAllMistakeExamples(
        IReadOnlyList<PlayerProfileSnapshot> snapshots,
        IReadOnlyList<ProfileLabelStat> topLabels,
        int maxTotal)
    {
        if (topLabels.Count == 0)
        {
            return [];
        }

        int perLabel = Math.Max(1, maxTotal / topLabels.Count);
        return topLabels
            .SelectMany(label =>
            {
                RecommendationContext context = BuildRecommendationContext(snapshots, label.Label);
                return BuildMistakeExamples(snapshots, label.Label, context, perLabel);
            })
            .OrderByDescending(example => example.CentipawnLoss ?? 0)
            .Take(maxTotal)
            .ToList();
    }

    private static List<MistakeExampleCandidate> BuildMistakeExampleCandidates(
        IReadOnlyList<PlayerProfileSnapshot> snapshots,
        string label)
    {
        return snapshots
            .SelectMany(snapshot => snapshot.Moves
                .Where(move =>
                    !string.IsNullOrWhiteSpace(move.MistakeLabel)
                    && string.Equals(move.MistakeLabel, label, StringComparison.Ordinal)
                    && move.Quality is MoveQualityBucket.Mistake or MoveQualityBucket.Blunder
                    && !string.IsNullOrWhiteSpace(move.FenBefore))
                .Select(move => new MistakeExampleCandidate(snapshot, move)))
            .GroupBy(candidate => BuildExampleIdentity(candidate), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => item.Move.IsHighlighted)
                .ThenByDescending(item => item.Move.CentipawnLoss ?? 0)
                .ThenByDescending(item => SeverityWeight(item.Move.Quality))
                .ThenBy(item => item.Move.Ply)
                .First())
            .ToList();
    }

    private static MistakeExampleCandidate? SelectMostFrequentExample(
        IReadOnlyList<MistakeExampleCandidate> candidates,
        IReadOnlySet<string> excludedKeys)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .GroupBy(
                candidate => new ExampleClusterKey(candidate.Move.Phase, candidate.Snapshot.Eco),
                ExampleClusterKeyComparer.Instance)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.Move.CentipawnLoss ?? 0))
            .ThenBy(group => group.Key.Phase)
            .ThenBy(group => group.Key.Eco, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Move.IsHighlighted)
                .ThenByDescending(item => item.Move.CentipawnLoss ?? 0)
                .ThenBy(item => item.Move.Ply)
                .FirstOrDefault(item => !excludedKeys.Contains(BuildExampleIdentity(item))))
            .FirstOrDefault();
    }

    private static MistakeExampleCandidate? SelectMostCostlyExample(
        IReadOnlyList<MistakeExampleCandidate> candidates,
        IReadOnlySet<string> excludedKeys)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Move.CentipawnLoss ?? 0)
            .ThenByDescending(candidate => candidate.Move.IsHighlighted)
            .ThenByDescending(candidate => SeverityWeight(candidate.Move.Quality))
            .ThenBy(candidate => candidate.Move.Ply)
            .Where(candidate => !excludedKeys.Contains(BuildExampleIdentity(candidate)))
            .FirstOrDefault();
    }

    private static MistakeExampleCandidate? SelectMostRepresentativeExample(
        IReadOnlyList<MistakeExampleCandidate> candidates,
        RecommendationContext context,
        IReadOnlySet<string> excludedKeys)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        double averageCpl = candidates
            .Select(candidate => Math.Max(0, candidate.Move.CentipawnLoss ?? 0))
            .DefaultIfEmpty(0)
            .Average();

        return candidates
            .OrderByDescending(candidate => candidate.Move.IsHighlighted)
            .ThenBy(candidate => BuildRepresentativePenalty(candidate, context, averageCpl))
            .ThenByDescending(candidate => candidate.Move.MistakeConfidence ?? 0.0)
            .ThenByDescending(candidate => candidate.Move.CentipawnLoss ?? 0)
            .ThenBy(candidate => candidate.Move.Ply)
            .Where(candidate => !excludedKeys.Contains(BuildExampleIdentity(candidate)))
            .FirstOrDefault();
    }

    private static int BuildRepresentativePenalty(
        MistakeExampleCandidate candidate,
        RecommendationContext context,
        double averageCpl)
    {
        int penalty = Math.Abs((candidate.Move.CentipawnLoss ?? 0) - (int)Math.Round(averageCpl));

        if (context.DominantPhase.HasValue && candidate.Move.Phase != context.DominantPhase.Value)
        {
            penalty += 75;
        }

        if (context.TopOpenings.Count > 0
            && !context.TopOpenings.Any(opening => string.Equals(opening, candidate.Snapshot.Eco, StringComparison.OrdinalIgnoreCase)))
        {
            penalty += 60;
        }

        if (context.DominantSide.HasValue && candidate.Snapshot.Side != context.DominantSide.Value)
        {
            penalty += 25;
        }

        if (!candidate.Move.IsHighlighted)
        {
            penalty += 15;
        }

        return penalty;
    }

    private static void AddRankedExample(
        List<ProfileMistakeExample> selected,
        HashSet<string> selectedKeys,
        MistakeExampleCandidate? candidate,
        ProfileMistakeExampleRank rank)
    {
        if (candidate is null)
        {
            return;
        }

        string identity = BuildExampleIdentity(candidate);
        if (!selectedKeys.Add(identity))
        {
            return;
        }

        selected.Add(ToProfileMistakeExample(candidate, rank));
    }

    private static string BuildExampleIdentity(MistakeExampleCandidate candidate)
    {
        return $"{candidate.Snapshot.GameFingerprint}|{candidate.Move.Ply}|{candidate.Move.FenBefore}";
    }

    private static ProfileMistakeExample ToProfileMistakeExample(
        MistakeExampleCandidate candidate,
        ProfileMistakeExampleRank rank)
    {
        return new ProfileMistakeExample(
            candidate.Snapshot.GameFingerprint,
            candidate.Move.Ply,
            candidate.Move.MoveNumber,
            candidate.Move.AnalyzedSide,
            candidate.Move.San,
            FormatBetterMove(candidate.Move.FenBefore, candidate.Move.BestMoveUci),
            candidate.Move.MistakeLabel ?? "unclassified",
            candidate.Move.CentipawnLoss,
            candidate.Move.Quality,
            candidate.Move.Phase,
            candidate.Snapshot.Eco,
            candidate.Move.FenBefore,
            rank);
    }

    private static string FormatBetterMove(string fenBefore, string? bestMoveUci)
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

    private static string BuildSeveritySummary(PriorityLabelStat labelStat)
    {
        string averageCpl = labelStat.AverageCentipawnLoss?.ToString() ?? "n/a";
        return $"It appeared {labelStat.Count} times and cost about {labelStat.TotalCentipawnLoss} centipawns in total (avg {averageCpl}).";
    }

    private static RecommendationContext BuildRecommendationContext(IReadOnlyList<PlayerProfileSnapshot> snapshots, string label)
    {
        List<RecommendationOccurrence> occurrences = snapshots
            .SelectMany(snapshot => BuildRecommendationOccurrences(snapshot, label))
            .ToList();

        if (occurrences.Count == 0)
        {
            return new RecommendationContext(null, null, []);
        }

        GamePhase? dominantPhase = occurrences
            .Where(item => item.Phase.HasValue)
            .GroupBy(item => item.Phase!.Value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (GamePhase?)group.Key)
            .FirstOrDefault();

        PlayerSide? dominantSide = occurrences
            .GroupBy(item => item.Side)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (PlayerSide?)group.Key)
            .FirstOrDefault();

        IReadOnlyList<string> topOpenings = occurrences
            .GroupBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(group => group.Key)
            .ToList();

        return new RecommendationContext(dominantPhase, dominantSide, topOpenings);
    }

    private static WeeklyTrainingPlan BuildWeeklyPlan(string displayName, IReadOnlyList<TrainingRecommendation> recommendations)
    {
        List<TrainingRecommendation> planRecommendations = recommendations.Count == 0
            ? [CreateFallbackRecommendation()]
            : recommendations.ToList();

        TrainingRecommendation primary = GetRecommendation(planRecommendations, 0);
        TrainingRecommendation secondary = GetRecommendation(planRecommendations, 1);
        TrainingRecommendation tertiary = GetRecommendation(planRecommendations, 2);

        string summary = recommendations.Count == 0
            ? "Start with a simple review rhythm: one focused theme, one practical game session and one end-of-week recap."
            : $"Built from your top priorities: {string.Join(", ", planRecommendations.Take(3).Select(item => item.Title))}.";

        List<WeeklyTrainingDay> days =
        [
            new WeeklyTrainingDay(
                1,
                primary.Title,
                "Pattern drill",
                $"Build a repeatable board-scan habit around {primary.FocusArea.ToLowerInvariant()}.",
                35,
                TrainingPlanTopicCategory.CoreWeakness),
            new WeeklyTrainingDay(
                2,
                primary.Title,
                "Own-game review",
                $"Review your own mistakes until you can explain the trigger behind {primary.Title.ToLowerInvariant()}.",
                45,
                TrainingPlanTopicCategory.CoreWeakness),
            new WeeklyTrainingDay(
                3,
                secondary.Title,
                "Focused drill",
                $"Spot the recurring cue behind {secondary.FocusArea.ToLowerInvariant()} positions faster and more reliably.",
                40,
                TrainingPlanTopicCategory.SecondaryWeakness),
            new WeeklyTrainingDay(
                4,
                $"{primary.Title} + {secondary.Title}",
                "Integration review",
                "Recall both main themes quickly before checking concrete moves.",
                25,
                TrainingPlanTopicCategory.CoreWeakness),
            new WeeklyTrainingDay(
                5,
                primary.Title,
                "Training game",
                $"Play one slower game with extra attention on {primary.Title.ToLowerInvariant()}.",
                50,
                TrainingPlanTopicCategory.CoreWeakness),
            new WeeklyTrainingDay(
                6,
                tertiary.Title,
                "Maintenance review",
                $"Keep the supporting theme active without letting it replace the main weekly priority.",
                35,
                TrainingPlanTopicCategory.MaintenanceTopic),
            new WeeklyTrainingDay(
                7,
                "Weekly reset",
                "Reflection",
                "Choose one theme that remains primary for the next cycle.",
                20,
                TrainingPlanTopicCategory.CoreWeakness)
        ];

        WeeklyTrainingBudget budget = new(
            days.Sum(day => day.EstimatedMinutes),
            days.Where(day => day.Category == TrainingPlanTopicCategory.CoreWeakness).Sum(day => day.EstimatedMinutes),
            days.Where(day => day.Category == TrainingPlanTopicCategory.SecondaryWeakness).Sum(day => day.EstimatedMinutes),
            days.Where(day => day.Category == TrainingPlanTopicCategory.MaintenanceTopic).Sum(day => day.EstimatedMinutes),
            days.Where(day => day.WorkType is "Integration review" or "Reflection").Sum(day => day.EstimatedMinutes),
            $"About {days.Sum(day => day.EstimatedMinutes)} minutes split across core work, a secondary theme, maintenance and a short weekly review.");

        return new WeeklyTrainingPlan(
            $"{displayName} Weekly Training Plan",
            summary,
            budget,
            days);
    }
}
