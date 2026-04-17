using System.Globalization;

namespace StockifhsGUI;

public sealed class PlayerProfileService
{
    private readonly IAnalysisStore analysisStore;

    public PlayerProfileService(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
    }

    public IReadOnlyList<PlayerProfileSummary> ListProfiles(string? filterText = null, int limit = 100)
    {
        List<PlayerProfileRecord> records = LoadProfileRecords(filterText, Math.Max(limit * 8, 200));
        return records
            .GroupBy(record => record.PlayerKey)
            .Select(BuildSummary)
            .OrderByDescending(summary => summary.GamesAnalyzed)
            .ThenByDescending(summary => summary.HighlightedMistakes)
            .ThenBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public bool TryBuildProfile(string playerKeyOrName, out PlayerProfileReport? report)
    {
        if (string.IsNullOrWhiteSpace(playerKeyOrName))
        {
            report = null;
            return false;
        }

        string normalized = NormalizePlayerKey(playerKeyOrName);
        List<PlayerProfileRecord> records = LoadProfileRecords(null, 2000)
            .Where(record => record.PlayerKey == normalized
                || string.Equals(record.DisplayName, playerKeyOrName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (records.Count == 0)
        {
            report = null;
            return false;
        }

        report = BuildReport(records);
        return true;
    }

    private List<PlayerProfileRecord> LoadProfileRecords(string? filterText, int limit)
    {
        IReadOnlyList<GameAnalysisResult> results = analysisStore.ListResults(filterText, limit);
        return results
            .Select(CreateProfileRecord)
            .Where(record => record is not null)
            .Select(record => record!)
            .GroupBy(record => $"{record.GameFingerprint}|{record.Side}")
            .Select(group => group.First())
            .ToList();
    }

    private static PlayerProfileSummary BuildSummary(IGrouping<string, PlayerProfileRecord> group)
    {
        string displayName = group
            .GroupBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<string> topLabels = group
            .SelectMany(record => record.HighlightedMistakes)
            .GroupBy(item => item.Tag?.Label ?? "unclassified")
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Key)
            .ToList();

        int? averageCpl = TryAverage(group.SelectMany(record => record.Result.MoveAnalyses).Select(move => move.CentipawnLoss));

        return new PlayerProfileSummary(
            group.Key,
            displayName,
            group.Count(),
            group.Sum(record => record.HighlightedMistakes.Count),
            averageCpl,
            topLabels);
    }

    private static PlayerProfileReport BuildReport(IReadOnlyList<PlayerProfileRecord> records)
    {
        string playerKey = records[0].PlayerKey;
        string displayName = records
            .GroupBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<ProfileLabelStat> topLabels = records
            .SelectMany(record => record.HighlightedMistakes)
            .GroupBy(item => item.Tag?.Label ?? "unclassified")
            .Select(group => new ProfileLabelStat(
                group.Key,
                group.Count(),
                group.Average(item => item.Tag?.Confidence ?? 0.0)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.AverageConfidence)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        IReadOnlyList<ProfilePhaseStat> mistakesByPhase = records
            .SelectMany(record => record.Result.MoveAnalyses)
            .Where(move => move.Quality != MoveQualityBucket.Good)
            .GroupBy(move => move.Replay.Phase)
            .Select(group => new ProfilePhaseStat(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Phase)
            .ToList();

        IReadOnlyList<ProfileOpeningStat> mistakesByOpening = records
            .SelectMany(record => record.Result.MoveAnalyses
                .Where(move => move.Quality != MoveQualityBucket.Good)
                .Select(_ => string.IsNullOrWhiteSpace(record.Result.Game.Eco) ? "Unknown" : record.Result.Game.Eco!))
            .GroupBy(eco => eco, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProfileOpeningStat(group.First(), group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        IReadOnlyList<ProfileSideStat> gamesBySide = records
            .GroupBy(record => record.Side)
            .Select(group => new ProfileSideStat(
                group.Key,
                group.Count(),
                group.Sum(item => item.HighlightedMistakes.Count)))
            .OrderBy(item => item.Side)
            .ToList();

        IReadOnlyList<ProfileMonthlyTrend> monthlyTrend = records
            .GroupBy(record => record.MonthKey ?? "Unknown")
            .Select(group => new ProfileMonthlyTrend(
                group.Key,
                group.Count(),
                group.Sum(item => item.HighlightedMistakes.Count),
                TryAverage(group.SelectMany(item => item.Result.MoveAnalyses).Select(move => move.CentipawnLoss))))
            .OrderBy(item => item.MonthKey, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<TrainingRecommendation> recommendations = topLabels
            .Take(3)
            .Select(CreateRecommendation)
            .ToList();

        return new PlayerProfileReport(
            playerKey,
            displayName,
            records.Count,
            records.Sum(record => record.Result.MoveAnalyses.Count),
            records.Sum(record => record.HighlightedMistakes.Count),
            TryAverage(records.SelectMany(record => record.Result.MoveAnalyses).Select(move => move.CentipawnLoss)),
            topLabels,
            mistakesByPhase,
            mistakesByOpening,
            gamesBySide,
            monthlyTrend,
            recommendations);
    }

    private static TrainingRecommendation CreateRecommendation(ProfileLabelStat labelStat)
    {
        return labelStat.Label switch
        {
            "hanging_piece" => new TrainingRecommendation(
                "Protect Loose Pieces",
                "Review attacker-defender counts before every move and train short 'undefended pieces' tactical sets."),
            "missed_tactic" => new TrainingRecommendation(
                "Checks, Captures, Threats",
                "Spend a few minutes on forcing-line puzzles and make CCT scanning your first step in sharp positions."),
            "opening_principles" => new TrainingRecommendation(
                "Clean Up The Opening",
                "Review your first 10 moves and prefer development, king safety and central control over early side ideas."),
            "king_safety" => new TrainingRecommendation(
                "Safer King Decisions",
                "Study a handful of model games where pawn moves around the king either helped or fatally weakened the position."),
            "endgame_technique" => new TrainingRecommendation(
                "Sharpen Endgame Technique",
                "Train king activity and simple conversion patterns so winning plans become more automatic in reduced material."),
            "material_loss" => new TrainingRecommendation(
                "Material Discipline",
                "Before releasing a move, compare the resulting material balance after the most forcing continuation."),
            "piece_activity" => new TrainingRecommendation(
                "Improve Piece Activity",
                "Compare candidate squares by mobility and coordination so you avoid drifting into passive setups."),
            _ => new TrainingRecommendation(
                "Review Critical Moments",
                "Revisit your biggest mistakes and focus on the first forcing reply that changed the evaluation.")
        };
    }

    private static PlayerProfileRecord? CreateProfileRecord(GameAnalysisResult result)
    {
        string? playerName = result.AnalyzedSide == PlayerSide.White
            ? result.Game.WhitePlayer
            : result.Game.BlackPlayer;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        return new PlayerProfileRecord(
            GameFingerprint.Compute(result.Game.PgnText),
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            result.AnalyzedSide,
            result.HighlightedMistakes,
            ParseMonthKey(result.Game.DateText),
            result);
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

    private static string? ParseMonthKey(string? dateText)
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

        if (DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private sealed record PlayerProfileRecord(
        string GameFingerprint,
        string PlayerKey,
        string DisplayName,
        PlayerSide Side,
        IReadOnlyList<SelectedMistake> HighlightedMistakes,
        string? MonthKey,
        GameAnalysisResult Result);
}
