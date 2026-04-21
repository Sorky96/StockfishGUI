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
                [CreateMoveAnalysis(GamePhase.Opening, 120, "opening_principles", bestMoveUci: "e2e4")]),
            CreateResult(
                "Alpha",
                "Gamma",
                PlayerSide.White,
                "C42",
                "2026.05.03",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 260, "hanging_piece", moveNumber: 12, san: "Qh5", bestMoveUci: "g1f3")]),
            CreateResult(
                "Delta",
                "Alpha",
                PlayerSide.Black,
                "B01",
                "2026.05.21",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 180, "hanging_piece", moveNumber: 15, san: "Nc6", bestMoveUci: "d7d5")]),
            CreateResult(
                "Omega",
                "Alpha",
                PlayerSide.Black,
                "B01",
                "2026.06.04",
                [CreateSelectedMistake("hanging_piece", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Endgame, 80, "hanging_piece", moveNumber: 32, san: "Kf7", bestMoveUci: "e7e5")])
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
        Assert.Equal("hanging_piece", report.CostliestMistakeLabels[0].Label);
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
        Assert.Contains("centipawns in total", topRecommendation.Description, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(topRecommendation.Examples!.Count, 2, 3);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostFrequent);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostCostly);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostRepresentative);
        Assert.All(topRecommendation.Examples, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BetterMove));
            Assert.False(string.IsNullOrWhiteSpace(item.Eco));
        });
        Assert.Equal(ProfileProgressDirection.Improving, report.ProgressSignal.Direction);
        Assert.Equal("Alpha Weekly Training Plan", report.WeeklyPlan.Title);
        Assert.Equal(7, report.WeeklyPlan.Days.Count);
        Assert.Equal("Board safety", report.WeeklyPlan.Days[0].PrimaryFocus);
        Assert.Contains("Protect Loose Pieces", report.WeeklyPlan.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.WeeklyPlan.Days, day => day.Theme.Contains("Applied game", StringComparison.OrdinalIgnoreCase));
        Assert.All(report.WeeklyPlan.Days, day => Assert.NotEmpty(day.Activities));
        Assert.Contains(report.MistakeExamples, item => item.Label == "hanging_piece");
    }

    [Fact]
    public void PlayerProfileService_DistinguishesFrequentVsCostlyLabels_AndDetectsRegression()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult(
                "Sigma",
                "Beta",
                PlayerSide.White,
                "C20",
                "2026.01.04",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", bestMoveUci: "e2e4")]),
            CreateResult(
                "Sigma",
                "Gamma",
                PlayerSide.White,
                "C20",
                "2026.02.11",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 95, "opening_principles", moveNumber: 2, san: "h3", bestMoveUci: "g1f3")]),
            CreateResult(
                "Sigma",
                "Theta",
                PlayerSide.White,
                "C23",
                "2026.02.22",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 88, "opening_principles", moveNumber: 3, san: "a3", bestMoveUci: "d2d4")]),
            CreateResult(
                "Sigma",
                "Delta",
                PlayerSide.White,
                "B01",
                "2026.03.18",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 320, "material_loss", moveNumber: 18, san: "Bxh7+", bestMoveUci: "d1d5")]),
            CreateResult(
                "Sigma",
                "Omega",
                PlayerSide.White,
                "B01",
                "2026.04.25",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 340, "material_loss", moveNumber: 20, san: "Qxd4", bestMoveUci: "c3d5")])
        ]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Sigma", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal("opening_principles", report!.TopMistakeLabels[0].Label);
        Assert.Equal("material_loss", report.CostliestMistakeLabels[0].Label);
        Assert.Equal("Material Discipline", report.Recommendations[0].Title);
        Assert.Equal(ProfileProgressDirection.Regressing, report.ProgressSignal.Direction);
        Assert.NotNull(report.ProgressSignal.Recent);
        Assert.NotNull(report.ProgressSignal.Previous);
        Assert.True((report.ProgressSignal.Recent!.AverageCentipawnLoss ?? 0) > (report.ProgressSignal.Previous!.AverageCentipawnLoss ?? 0));
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

    [Fact]
    public void PlayerProfileService_MergesStructuredMovesWithLegacyResults_WithoutDroppingGames()
    {
        GameAnalysisResult resultA = CreateResult(
            "Alpha",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
            [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", bestMoveUci: "e2e4")]);
        GameAnalysisResult resultB = CreateResult(
            "Alpha",
            "Gamma",
            PlayerSide.White,
            "B01",
            "2026.04.02",
            [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
            [CreateMoveAnalysis(GamePhase.Middlegame, 250, "material_loss", moveNumber: 11, san: "Qh4", bestMoveUci: "g1f3")]);
        GameAnalysisResult resultC = CreateResult(
            "Delta",
            "Alpha",
            PlayerSide.Black,
            "C23",
            "2026.04.03",
            [CreateSelectedMistake("missed_tactic", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Middlegame, 180, "missed_tactic", moveNumber: 14, san: "Re8", bestMoveUci: "d2d4")]);

        FakeAnalysisStore store = new(
            [resultA, resultB, resultC],
            BuildStoredMoves([resultA]));

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Alpha", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal(3, report!.GamesAnalyzed);
        Assert.Equal(3, report.MonthlyTrend.Sum(item => item.GamesAnalyzed));
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

    private static MoveAnalysisResult CreateMoveAnalysis(
        GamePhase phase,
        int cpl,
        string label,
        int moveNumber = 1,
        string san = "e4",
        string bestMoveUci = "e2e4")
    {
        const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string AfterE4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        ReplayPly replay = new(
            moveNumber * 2 - 1,
            moveNumber,
            PlayerSide.White,
            san,
            san,
            bestMoveUci,
            StartFen,
            AfterE4Fen,
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
            new EngineAnalysis(StartFen, [], bestMoveUci),
            new EngineAnalysis(AfterE4Fen, [], null),
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
        private readonly IReadOnlyList<StoredMoveAnalysis> moveAnalyses;

        public FakeAnalysisStore(IReadOnlyList<GameAnalysisResult> results, IReadOnlyList<StoredMoveAnalysis>? moveAnalyses = null)
        {
            this.results = results;
            this.moveAnalyses = moveAnalyses ?? BuildStoredMoves(results);
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

        public IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000)
        {
            IEnumerable<StoredMoveAnalysis> filtered = moveAnalyses;
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filtered = filtered.Where(move =>
                    (move.WhitePlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (move.BlackPlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return filtered
                .Take(limit)
                .ToList();
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

    private static IReadOnlyList<StoredMoveAnalysis> BuildStoredMoves(IReadOnlyList<GameAnalysisResult> results)
    {
        return results
            .SelectMany(result =>
            {
                HashSet<string> highlightedLabels = result.HighlightedMistakes
                    .Select(mistake => mistake.Tag?.Label ?? "unclassified")
                    .ToHashSet(StringComparer.Ordinal);

                return result.MoveAnalyses.Select(move => new StoredMoveAnalysis(
                    GameFingerprint.Compute(result.Game.PgnText),
                    result.AnalyzedSide,
                    14,
                    3,
                    null,
                    DateTime.Parse("2026-04-18T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal),
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
                    highlightedLabels.Contains(move.MistakeTag?.Label ?? "unclassified")));
            })
            .ToList();
    }
}
