using StockifhsGUI;
using Xunit;

namespace StockifhsGUI.Tests;

public sealed class PlayerProfileServiceTests
{
    [Fact]
    public void PlayerProfileService_AggregatesTopLabelsAndRecommendations()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult(
                "Alpha",
                "Beta",
                PlayerSide.White,
                "C20",
                "2026.04.01",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 120, "opening_principles")]),
            CreateResult(
                "Alpha",
                "Gamma",
                PlayerSide.White,
                "C42",
                "2026.05.03",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 260, "hanging_piece")]),
            CreateResult(
                "Delta",
                "Alpha",
                PlayerSide.Black,
                "B01",
                "2026.05.21",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 180, "hanging_piece")]),
            CreateResult(
                "Omega",
                "Alpha",
                PlayerSide.Black,
                "B01",
                "2026.06.04",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Endgame, 180, "hanging_piece")])
        ]);

        PlayerProfileService service = new(store);

        IReadOnlyList<PlayerProfileSummary> summaries = service.ListProfiles();
        Assert.Contains(summaries, item => item.DisplayName == "Alpha");

        bool found = service.TryBuildProfile("Alpha", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal(4, report!.GamesAnalyzed);
        Assert.Equal("hanging_piece", report.TopMistakeLabels[0].Label);
        Assert.Equal(3, report.TopMistakeLabels[0].Count);
        TrainingRecommendation topRecommendation = Assert.Single(
            report.Recommendations,
            item => item.Title.Contains("Protect Loose Pieces", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, topRecommendation.Priority);
        Assert.Equal("Board safety", topRecommendation.FocusArea);
        Assert.Equal(GamePhase.Middlegame, topRecommendation.EmphasisPhase);
        Assert.Equal(PlayerSide.Black, topRecommendation.EmphasisSide);
        Assert.Contains("B01", topRecommendation.RelatedOpenings);
        Assert.NotEmpty(topRecommendation.Checklist);
        Assert.NotEmpty(topRecommendation.SuggestedDrills);
        Assert.Contains(report.MonthlyTrend, item => item.MonthKey == "2026-05" && item.GamesAnalyzed == 2);
        Assert.Contains(report.QuarterlyTrend, item => item.QuarterKey == "2026-Q2" && item.GamesAnalyzed == 4);
        Assert.Contains("middlegame", topRecommendation.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Alpha Weekly Training Plan", report.WeeklyPlan.Title);
        Assert.Equal(7, report.WeeklyPlan.Days.Count);
        Assert.Equal("Board safety", report.WeeklyPlan.Days[0].PrimaryFocus);
        Assert.Contains("Protect Loose Pieces", report.WeeklyPlan.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.WeeklyPlan.Days, day => day.Theme.Contains("Applied game", StringComparison.OrdinalIgnoreCase));
        Assert.All(report.WeeklyPlan.Days, day => Assert.NotEmpty(day.Activities));
    }

    [Fact]
    public void PlayerProfileService_FiltersProfilesByPlayerName()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult("Alpha", "Beta", PlayerSide.White, "C20", "2026.04.01", [], []),
            CreateResult("Gamma", "Delta", PlayerSide.White, "B01", "2026.04.02", [], [])
        ]);

        PlayerProfileService service = new(store);

        IReadOnlyList<PlayerProfileSummary> summaries = service.ListProfiles("alp");

        Assert.Single(summaries);
        Assert.Equal("Alpha", summaries[0].DisplayName);
    }

    private static GameAnalysisResult CreateResult(
        string whitePlayer,
        string blackPlayer,
        PlayerSide side,
        string eco,
        string dateText,
        IReadOnlyList<SelectedMistake> highlightedMistakes,
        IReadOnlyList<MoveAnalysisResult> moveAnalyses)
    {
        ImportedGame game = new(
            $"[White \"{whitePlayer}\"] [Black \"{blackPlayer}\"]",
            [],
            whitePlayer,
            blackPlayer,
            null,
            null,
            dateText,
            "1-0",
            eco,
            "Local");

        return new GameAnalysisResult(game, side, [], moveAnalyses, highlightedMistakes);
    }

    private static SelectedMistake CreateSelectedMistake(string label, MoveQualityBucket quality)
    {
        return new SelectedMistake(
            [],
            quality,
            new MistakeTag(label, 0.82, ["evidence"]),
            new MoveExplanation("Short", "Hint", "Detailed"));
    }

    private static MoveAnalysisResult CreateMoveAnalysis(GamePhase phase, int cpl, string label)
    {
        ReplayPly replay = new(
            1,
            1,
            PlayerSide.White,
            "e4",
            "e4",
            "e2e4",
            "start",
            "after",
            string.Empty,
            string.Empty,
            phase,
            "P",
            null,
            "e2",
            "e4",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            new EngineAnalysis("start", [], null),
            new EngineAnalysis("after", [], null),
            20,
            -cpl,
            null,
            null,
            cpl,
            cpl >= 200 ? MoveQualityBucket.Blunder : MoveQualityBucket.Mistake,
            0,
            new MistakeTag(label, 0.8, ["evidence"]),
            new MoveExplanation("Short", "Hint", "Detailed"));
    }

    private sealed class FakeAnalysisStore : IAnalysisStore
    {
        private readonly IReadOnlyList<GameAnalysisResult> results;

        public FakeAnalysisStore(IReadOnlyList<GameAnalysisResult> results)
        {
            this.results = results;
        }

        public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500)
        {
            IEnumerable<GameAnalysisResult> filtered = results;
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filtered = filtered.Where(result =>
                    (result.Game.WhitePlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (result.Game.BlackPlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return filtered.Take(limit).ToList();
        }

        public bool DeleteImportedGame(string gameFingerprint) => throw new NotSupportedException();
        public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200) => [];
        public void SaveImportedGame(ImportedGame game) => throw new NotSupportedException();
        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game) => throw new NotSupportedException();
        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result) => throw new NotSupportedException();
        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result) => throw new NotSupportedException();
        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state) => throw new NotSupportedException();
        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state) => throw new NotSupportedException();
    }
}
