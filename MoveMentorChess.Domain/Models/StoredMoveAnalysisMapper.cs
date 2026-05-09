namespace MoveMentorChess.Domain;

public static class StoredMoveAnalysisMapper
{
    public static IReadOnlyList<StoredMoveAnalysis> FromAnalysisResult(
        GameAnalysisCacheKey key,
        GameAnalysisResult result,
        DateTime analysisUpdatedUtc)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);

        HashSet<int> highlightedPlys = result.HighlightedMistakes
            .SelectMany(mistake => mistake.Moves)
            .Select(move => move.Replay.Ply)
            .ToHashSet();

        return result.MoveAnalyses
            .Select(move => FromAnalysisResultMove(key, result, move, highlightedPlys.Contains(move.Replay.Ply), analysisUpdatedUtc))
            .ToList();
    }

    public static StoredMoveAnalysis FromAnalysisResultMove(
        GameAnalysisCacheKey key,
        GameAnalysisResult result,
        MoveAnalysisResult move,
        bool isHighlighted,
        DateTime analysisUpdatedUtc)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(move);

        return new StoredMoveAnalysis(
            new StoredGameContext(
                key.GameFingerprint,
                result.Game.WhitePlayer,
                result.Game.BlackPlayer,
                result.Game.DateText,
                result.Game.Result,
                result.Game.Eco,
                result.Game.Site,
                result.Game.WhiteElo,
                result.Game.BlackElo,
                result.Game.Metadata?.TimeControl,
                result.Game.Metadata?.TimeControlCategory ?? GameTimeControlCategory.Unknown,
                result.Game.Metadata?.UtcDate,
                result.Game.Metadata?.UtcTime,
                result.Game.Metadata?.EndDate,
                result.Game.Metadata?.EndTime,
                result.Game.Metadata?.Termination,
                result.Game.Metadata?.Link),
            new StoredAnalysisRunContext(key.Side, key.Depth, key.MultiPv, key.MoveTimeMs, analysisUpdatedUtc),
            new StoredMoveContext(
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
                move.BeforeAnalysis.BestMoveUci),
            new StoredMoveAdviceContext(
                move.MistakeTag?.Label,
                move.MistakeTag?.Confidence,
                move.MistakeTag?.Evidence ?? [],
                move.Explanation?.ShortText,
                move.Explanation?.DetailedText,
                move.Explanation?.TrainingHint,
                isHighlighted));
    }

    public static StoredMoveAnalysis FromSqliteRow(
        StoredGameContext game,
        StoredAnalysisRunContext analysis,
        StoredMoveContext move,
        StoredMoveAdviceContext advice,
        StoredManualFeedbackContext? manualFeedback = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(advice);

        return new StoredMoveAnalysis(game, analysis, move, advice, manualFeedback);
    }

    public static StoredMoveAnalysis CreateTestFixture(
        string gameFingerprint = "game",
        int ply = 1,
        string san = "a3",
        string uci = "a2a3",
        string? mistakeLabel = "test_mistake",
        MoveQualityBucket quality = MoveQualityBucket.Inaccuracy,
        bool isHighlighted = true,
        DateTime? analysisUpdatedUtc = null)
    {
        return new StoredMoveAnalysis(
            new StoredGameContext(
                gameFingerprint,
                "White",
                "Black",
                "2026.04.29",
                "1-0",
                "A00",
                "Local"),
            new StoredAnalysisRunContext(
                PlayerSide.White,
                14,
                3,
                null,
                analysisUpdatedUtc ?? new DateTime(2026, 4, 29, 0, 0, 0, DateTimeKind.Utc)),
            new StoredMoveContext(
                ply,
                ply,
                san,
                uci,
                "4k3/8/8/8/8/8/P7/4K3 w - - 0 1",
                "4k3/8/8/8/8/P7/8/4K3 b - - 0 1",
                ply % 3 == 0 ? GamePhase.Endgame : ply % 2 == 0 ? GamePhase.Middlegame : GamePhase.Opening,
                0,
                -120,
                null,
                null,
                120,
                quality,
                0,
                "a2a4"),
            new StoredMoveAdviceContext(
                mistakeLabel,
                0.8,
                ["evidence"],
                "Short explanation",
                "What: issue. Why: reason. Better: a2a4. Watch next time: cue.",
                "Training hint",
                isHighlighted));
    }
}
