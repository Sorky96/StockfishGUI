namespace StockifhsGUI;

/// <summary>
/// Builds a lightweight <see cref="PlayerMistakeProfile"/> from stored analysis results.
/// Designed to be fast enough to call per-game (not per-move) during analysis.
/// </summary>
public static class PlayerMistakeProfileProvider
{
    private const int MinGamesForProfile = 2;
    private const int MaxPatterns = 3;
    private const int MaxResultsToScan = 200;

    public static PlayerMistakeProfile? TryBuild(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        try
        {
            IAnalysisStore? store = AnalysisStoreProvider.GetStore();
            if (store is null)
            {
                return null;
            }

            return TryBuildFromStore(store, playerName.Trim());
        }
        catch
        {
            // Store not available (e.g., during tests or first run).
            return null;
        }
    }

    internal static PlayerMistakeProfile? TryBuildFromStore(IAnalysisStore store, string playerName)
    {
        IReadOnlyList<GameAnalysisResult> results = store.ListResults(null, MaxResultsToScan);

        string normalizedName = playerName.ToLowerInvariant();
        List<GameAnalysisResult> playerResults = results
            .Where(result => IsPlayerMatch(result, normalizedName))
            .ToList();

        if (playerResults.Count < MinGamesForProfile)
        {
            return null;
        }

        IReadOnlyList<PlayerMistakePatternEntry> topPatterns = playerResults
            .SelectMany(result => result.HighlightedMistakes)
            .Where(mistake => mistake.Tag is not null)
            .GroupBy(mistake => mistake.Tag!.Label)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(MaxPatterns)
            .Select(group => new PlayerMistakePatternEntry(group.Key, group.Count()))
            .ToList();

        if (topPatterns.Count == 0)
        {
            return null;
        }

        int? averageCpl = ComputeAverageCpl(playerResults);
        GamePhase? weakestPhase = FindWeakestPhase(playerResults);

        return new PlayerMistakeProfile(
            playerName,
            playerResults.Count,
            averageCpl,
            topPatterns,
            weakestPhase);
    }

    private static bool IsPlayerMatch(GameAnalysisResult result, string normalizedName)
    {
        string? playerName = result.AnalyzedSide == PlayerSide.White
            ? result.Game.WhitePlayer
            : result.Game.BlackPlayer;

        return !string.IsNullOrWhiteSpace(playerName)
            && string.Equals(playerName.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase);
    }

    private static int? ComputeAverageCpl(IReadOnlyList<GameAnalysisResult> results)
    {
        List<int> cplValues = results
            .SelectMany(result => result.MoveAnalyses)
            .Where(move => move.CentipawnLoss.HasValue)
            .Select(move => move.CentipawnLoss!.Value)
            .ToList();

        return cplValues.Count == 0
            ? null
            : (int)Math.Round(cplValues.Average());
    }

    private static GamePhase? FindWeakestPhase(IReadOnlyList<GameAnalysisResult> results)
    {
        var phaseGroups = results
            .SelectMany(result => result.MoveAnalyses)
            .Where(move => move.Quality != MoveQualityBucket.Good)
            .GroupBy(move => move.Replay.Phase)
            .Select(group => new { Phase = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Phase)
            .ToList();

        return phaseGroups.Count > 0 ? phaseGroups[0].Phase : null;
    }
}
