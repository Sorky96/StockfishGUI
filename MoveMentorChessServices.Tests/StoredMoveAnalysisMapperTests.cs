using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class StoredMoveAnalysisMapperTests
{
    [Fact]
    public void FromAnalysisResult_ComposesGameRunMoveAndAdviceContexts()
    {
        DateTime updatedUtc = new(2026, 4, 18, 12, 30, 0, DateTimeKind.Utc);
        MoveAnalysisResult move = CreateMoveAnalysis(
            ply: 3,
            quality: MoveQualityBucket.Mistake,
            label: "opening_principles");
        GameAnalysisResult result = CreateResult(move, highlighted: true);
        GameAnalysisCacheKey key = new("fingerprint", PlayerSide.White, 14, 3, 250);

        StoredMoveAnalysis stored = Assert.Single(StoredMoveAnalysisMapper.FromAnalysisResult(key, result, updatedUtc));

        Assert.Equal("fingerprint", stored.Game.GameFingerprint);
        Assert.Equal("Alpha", stored.Game.WhitePlayer);
        Assert.Equal(812, stored.Game.WhiteElo);
        Assert.Equal("600", stored.Game.TimeControl);
        Assert.Equal(PlayerSide.White, stored.Analysis.AnalyzedSide);
        Assert.Equal(250, stored.Analysis.MoveTimeMs);
        Assert.Equal(updatedUtc, stored.Analysis.AnalysisUpdatedUtc);
        Assert.Equal(3, stored.Move.Ply);
        Assert.Equal("Bc4", stored.Move.San);
        Assert.Equal(MoveQualityBucket.Mistake, stored.Move.Quality);
        Assert.Equal("opening_principles", stored.Advice.MistakeLabel);
        Assert.Equal(["develop"], stored.Advice.Evidence);
        Assert.True(stored.Advice.IsHighlighted);
    }

    [Fact]
    public void FromSqliteRow_ComposesContextsAndManualFeedback()
    {
        DateTime updatedUtc = new(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc);
        DateTime correctedUtc = new(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc);

        StoredMoveAnalysis stored = StoredMoveAnalysisMapper.FromSqliteRow(
            new StoredGameContext("game", "Alpha", "Beta", "2026.04.18", "1-0", "C20", "Chess.com"),
            new StoredAnalysisRunContext(PlayerSide.Black, 18, 2, null, updatedUtc),
            new StoredMoveContext(
                4,
                2,
                "Nc6",
                "b8c6",
                "fen-before",
                "fen-after",
                GamePhase.Opening,
                15,
                -60,
                null,
                null,
                75,
                MoveQualityBucket.Inaccuracy,
                -100,
                "g8f6"),
            new StoredMoveAdviceContext(
                "piece_activity",
                0.73,
                ["tempo"],
                "Short",
                "Detailed",
                "Hint",
                false,
                "original_label"),
            new StoredManualFeedbackContext(
                AdviceFeedbackKind.WrongLabel,
                "development",
                "Prefer another label.",
                correctedUtc));

        Assert.Equal("game", stored.Game.GameFingerprint);
        Assert.Equal(PlayerSide.Black, stored.Analysis.AnalyzedSide);
        Assert.Null(stored.Analysis.MoveTimeMs);
        Assert.Equal(updatedUtc, stored.Analysis.AnalysisUpdatedUtc);
        Assert.Equal("Nc6", stored.Move.San);
        Assert.Equal("piece_activity", stored.Advice.MistakeLabel);
        Assert.Equal("original_label", stored.Advice.OriginalMistakeLabel);
        Assert.NotNull(stored.ManualFeedback);
        Assert.Equal(AdviceFeedbackKind.WrongLabel, stored.ManualFeedback.ManualFeedbackKind);
        Assert.Equal("development", stored.ManualFeedback.ManualCorrectedLabel);
        Assert.Equal(correctedUtc, stored.ManualFeedback.ManualCorrectedUtc);
    }

    private static GameAnalysisResult CreateResult(MoveAnalysisResult move, bool highlighted)
    {
        ImportedGame game = new(
            "1. e4 e5 2. Bc4 Nc6 1-0",
            ["e4", "e5", "Bc4", "Nc6"],
            "Alpha",
            "Beta",
            812,
            799,
            "2026.04.18",
            "1-0",
            "C20",
            "Chess.com",
            new PgnGameMetadata(
                null,
                null,
                null,
                null,
                "2026.04.18",
                "12:00:00",
                "600",
                null,
                null,
                null,
                null,
                null,
                GameTimeControlCategory.Blitz));

        SelectedMistake[] highlightedMistakes = highlighted
            ? [new SelectedMistake([move], move.Quality, move.MistakeTag, move.Explanation!)]
            : [];

        return new GameAnalysisResult(
            game,
            PlayerSide.White,
            [move.Replay],
            [move],
            highlightedMistakes);
    }

    private static MoveAnalysisResult CreateMoveAnalysis(int ply, MoveQualityBucket quality, string label)
    {
        ReplayPly replay = new(
            ply,
            2,
            PlayerSide.White,
            "Bc4",
            "Bc4",
            "f1c4",
            "fen-before",
            "fen-after",
            "placement-before",
            "placement-after",
            GamePhase.Opening,
            "B",
            null,
            "f1",
            "c4",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            new EngineAnalysis("fen-before", [new EngineLine("g1f3", 20, null, ["g1f3"])], "g1f3"),
            new EngineAnalysis("fen-after", [new EngineLine("g8f6", -80, null, ["g8f6"])], "g8f6"),
            20,
            -80,
            null,
            null,
            100,
            quality,
            -100,
            new MistakeTag(label, 0.81, ["develop"]),
            new MoveExplanation("Short", "Hint", "Detailed"));
    }
}
