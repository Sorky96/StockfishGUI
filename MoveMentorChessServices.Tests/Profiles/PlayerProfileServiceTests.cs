using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

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
        Assert.NotNull(topRecommendation.Blocks);
        Assert.Equal(3, topRecommendation.Blocks!.Count);
        Assert.Contains(topRecommendation.Blocks, block => block.Purpose == TrainingBlockPurpose.Repair && block.Kind == TrainingBlockKind.Tactics);
        Assert.Contains(topRecommendation.Blocks, block => block.Purpose == TrainingBlockPurpose.Maintain && block.Kind == TrainingBlockKind.GameReview);
        Assert.Contains(topRecommendation.Blocks, block => block.Purpose == TrainingBlockPurpose.Checklist && block.Kind == TrainingBlockKind.SlowPlayFocus);
        Assert.Contains(report.MonthlyTrend, item => item.MonthKey == "2026-05" && item.GamesAnalyzed == 2);
        Assert.Contains(report.QuarterlyTrend, item => item.QuarterKey == "2026-Q2" && item.GamesAnalyzed == 4);
        Assert.Contains("middlegame", topRecommendation.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("material losses", topRecommendation.Description, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(topRecommendation.Examples!.Count, 2, 3);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostFrequent);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostCostly);
        Assert.Contains(topRecommendation.Examples, item => item.Rank == ProfileMistakeExampleRank.MostRepresentative);
        Assert.All(topRecommendation.Examples, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BetterMove));
            Assert.False(string.IsNullOrWhiteSpace(item.Eco));
        });
        Assert.Equal(ProfileProgressDirection.InsufficientData, report.ProgressSignal.Direction);
        Assert.All(report.TrainingPlan.Topics, topic =>
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.WhyThisTopicNow));
            Assert.Contains("Frequency:", topic.WhyThisTopicNow, StringComparison.Ordinal);
            Assert.Contains("CPL cost:", topic.WhyThisTopicNow, StringComparison.Ordinal);
            Assert.Contains("Trend:", topic.WhyThisTopicNow, StringComparison.Ordinal);
        });
        Assert.Equal("Alpha Weekly Training Plan", report.WeeklyPlan.Title);
        Assert.Equal(7, report.WeeklyPlan.Days.Count);
        Assert.Equal("Protect loose pieces", report.WeeklyPlan.Days[0].Topic);
        Assert.Contains("Repair", report.WeeklyPlan.Days[0].WorkType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TrainingBlockPurpose.Repair, report.WeeklyPlan.Days[0].Purpose);
        Assert.Equal(TrainingBlockKind.Tactics, report.WeeklyPlan.Days[0].BlockKind);
        Assert.False(string.IsNullOrWhiteSpace(report.WeeklyPlan.Days[0].Goal));
        Assert.Contains("Protect Loose Pieces", report.WeeklyPlan.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.WeeklyPlan.Budget.TotalMinutes > 0);
        Assert.True(report.WeeklyPlan.Budget.CoreWeaknessMinutes > 0);
        Assert.True(report.WeeklyPlan.Budget.SecondaryWeaknessMinutes > 0);
        Assert.Contains(report.WeeklyPlan.Days, day => day.BlockKind == TrainingBlockKind.SlowPlayFocus);
        Assert.Contains(report.MistakeExamples, item => item.Label == "hanging_piece");
    }

    [Fact]
    public void PlayerProfileService_AddsActionableOpeningsToTrainingPlan()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult(
                "Bookish",
                "Beta",
                PlayerSide.White,
                "C20",
                "2026.04.01",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Opening, 210, "opening_principles", moveNumber: 3, san: "h3", bestMoveUci: "g1f3")]),
            CreateResult(
                "Bookish",
                "Gamma",
                PlayerSide.White,
                "C20",
                "2026.04.08",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Opening, 190, "opening_principles", moveNumber: 4, san: "a3", bestMoveUci: "d2d4")]),
            CreateResult(
                "Bookish",
                "Delta",
                PlayerSide.White,
                "B01",
                "2026.04.15",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 140, "material_loss", moveNumber: 12, san: "Bxh7+", bestMoveUci: "d1d5")])
        ]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Bookish", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        TrainingPlanTopic openingTopic = Assert.Single(report!.TrainingPlan.Topics, topic => topic.Label == "opening_principles");
        Assert.Contains("C20", openingTopic.RelatedOpenings);
        Assert.Contains("Opening trainer:", openingTopic.WhyThisTopicNow, StringComparison.Ordinal);
        Assert.Contains(report.WeeklyPlan.Days, day =>
            day.BlockKind == TrainingBlockKind.OpeningReview
            && day.LaunchTrainingMode.HasValue
            && day.RelatedOpenings is not null
            && day.RelatedOpenings.Contains("C20"));
    }

    [Fact]
    public void PlayerProfileService_UsesOpeningTrainerResultsToUpdatePlanPriorityAndStatus()
    {
        OpeningTrainingSessionResult poorTraining = CreateTrainingResult(
            "opening-trainer:bookish:1",
            "bookish",
            "Bookish",
            "C20",
            correct: 1,
            playable: 0,
            wrong: 4);
        FakeAnalysisStore store = new(
        [
            CreateResult(
                "Bookish",
                "Beta",
                PlayerSide.White,
                "C20",
                "2026.04.01",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", moveNumber: 3, san: "h3", bestMoveUci: "g1f3")]),
            CreateResult(
                "Bookish",
                "Gamma",
                PlayerSide.White,
                "B01",
                "2026.04.08",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 260, "material_loss", moveNumber: 12, san: "Bxh7+", bestMoveUci: "d1d5")])
        ],
        trainingHistory: [poorTraining]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Bookish", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        TrainingPlanTopic openingTopic = Assert.Single(report!.TrainingPlan.Topics, topic => topic.Label == "opening_principles");
        Assert.Equal(TrainingPlanTopicStatus.Urgent, openingTopic.Status);
        Assert.True(openingTopic.PriorityBreakdown.TrainingScore > 0);
        Assert.Contains("Training results:", openingTopic.WhyThisTopicNow, StringComparison.Ordinal);
        Assert.Contains(report.TrainingPlan.WhyThisIsYourCurrentPlan ?? [], item => item.Title == "Opening trainer results");
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
                "2026.03.01",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", bestMoveUci: "e2e4")]),
            CreateResult(
                "Sigma",
                "Gamma",
                PlayerSide.White,
                "C20",
                "2026.03.05",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 95, "opening_principles", moveNumber: 2, san: "h3", bestMoveUci: "g1f3")]),
            CreateResult(
                "Sigma",
                "Theta",
                PlayerSide.White,
                "C23",
                "2026.03.10",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 88, "opening_principles", moveNumber: 3, san: "a3", bestMoveUci: "d2d4")]),
            CreateResult(
                "Sigma",
                "Lambda",
                PlayerSide.White,
                "C23",
                "2026.03.12",
                [CreateSelectedMistake("opening_principles", MoveQualityBucket.Inaccuracy)],
                [CreateMoveAnalysis(GamePhase.Opening, 92, "opening_principles", moveNumber: 4, san: "Rh3", bestMoveUci: "d2d4")]),
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
                "Kappa",
                PlayerSide.White,
                "B01",
                "2026.03.29",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 360, "material_loss", moveNumber: 19, san: "Nxf7", bestMoveUci: "c3d5")]),
            CreateResult(
                "Sigma",
                "Omega",
                PlayerSide.White,
                "B01",
                "2026.04.25",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 380, "material_loss", moveNumber: 20, san: "Qxd4", bestMoveUci: "c3d5")])
        ]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Sigma", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal("opening_principles", report!.TopMistakeLabels[0].Label);
        Assert.Equal("material_loss", report.CostliestMistakeLabels[0].Label);
        Assert.Equal("Calculate the full exchange", report.Recommendations[0].Title);
        Assert.Equal(ProfileProgressDirection.Regressing, report.ProgressSignal.Direction);
        Assert.Contains(report.LabelTrends, item => item.Label == "material_loss" && item.Direction == ProfileProgressDirection.Regressing);
        Assert.Contains(report.LabelTrends, item => item.Label == "opening_principles" && item.Direction == ProfileProgressDirection.Improving);
        TrainingPlanTopic materialLossTopic = Assert.Single(report.TrainingPlan.Topics, item => item.Label == "material_loss");
        Assert.Equal(ProfileProgressDirection.Regressing, materialLossTopic.TrendDirection);
        Assert.Equal(TrainingPlanTopicCategory.CoreWeakness, materialLossTopic.Category);
        Assert.Contains("Trend: regressing", materialLossTopic.WhyThisTopicNow, StringComparison.OrdinalIgnoreCase);
        TrainingPlanTopic openingTopic = Assert.Single(report.TrainingPlan.Topics, item => item.Label == "opening_principles");
        Assert.Equal(ProfileProgressDirection.Improving, openingTopic.TrendDirection);
        Assert.Equal(TrainingPlanTopicCategory.MaintenanceTopic, openingTopic.Category);
        Assert.Contains("maintenance/review", openingTopic.WhyThisTopicNow, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(report.Recommendations[0].Blocks);
        Assert.Contains(report.Recommendations[0].Blocks!, block => block.Purpose == TrainingBlockPurpose.Repair && block.Kind == TrainingBlockKind.Tactics);
        Assert.Contains(report.Recommendations, recommendation =>
            recommendation.FocusArea == "Opening discipline"
            && recommendation.Blocks is not null
            && recommendation.Blocks.Any(block => block.Purpose == TrainingBlockPurpose.Repair && block.Kind == TrainingBlockKind.OpeningReview));
        Assert.NotNull(report.ProgressSignal.Recent);
        Assert.NotNull(report.ProgressSignal.Previous);
        Assert.True((report.ProgressSignal.Recent!.AverageCentipawnLoss ?? 0) > (report.ProgressSignal.Previous!.AverageCentipawnLoss ?? 0));
        Assert.Equal("Calculate the full exchange", report.WeeklyPlan.Days[0].Topic);
        Assert.Contains("core weakness", report.WeeklyPlan.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            report.WeeklyPlan.Days.Sum(day => day.EstimatedMinutes),
            report.WeeklyPlan.Budget.TotalMinutes);
    }

    [Fact]
    public void PlayerProfileService_BuildsStableAndInsufficientTopicTrends()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult(
                "Tau",
                "Beta",
                PlayerSide.White,
                "C20",
                "2026.02.25",
                [CreateSelectedMistake("material_loss", MoveQualityBucket.Blunder)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 260, "material_loss", moveNumber: 12, san: "Bxh7+", bestMoveUci: "d1d5")]),
            CreateResult(
                "Tau",
                "Gamma",
                PlayerSide.White,
                "C23",
                "2026.03.18",
                [CreateSelectedMistake("missed_tactic", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 165, "missed_tactic", moveNumber: 10, san: "Ng5", bestMoveUci: "d2d4")]),
            CreateResult(
                "Tau",
                "Delta",
                PlayerSide.White,
                "C23",
                "2026.04.10",
                [CreateSelectedMistake("missed_tactic", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Middlegame, 160, "missed_tactic", moveNumber: 8, san: "Qh5", bestMoveUci: "g1f3")]),
            CreateResult(
                "Tau",
                "Omega",
                PlayerSide.White,
                "A00",
                "2026.04.20",
                [CreateSelectedMistake("king_safety", MoveQualityBucket.Mistake)],
                [CreateMoveAnalysis(GamePhase.Opening, 140, "king_safety", moveNumber: 6, san: "g4", bestMoveUci: "g1f3")])
        ]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Tau", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Contains(report!.LabelTrends, item => item.Label == "missed_tactic" && item.Direction == ProfileProgressDirection.Stable);
        Assert.Contains(report.LabelTrends, item => item.Label == "king_safety" && item.Direction == ProfileProgressDirection.InsufficientData);
        TrainingPlanTopic stableTopic = Assert.Single(report.TrainingPlan.Topics, item => item.Label == "missed_tactic");
        Assert.Contains("Trend: stable", stableTopic.WhyThisTopicNow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerProfileService_DoesNotTreatSameDayGamesAsProgressTrend()
    {
        FakeAnalysisStore store = new(
        [
            CreateResult("Burst", "One", PlayerSide.White, "C20", "2026.04.25", [], [CreateMoveAnalysis(GamePhase.Opening, 80, "opening_principles")]),
            CreateResult("Burst", "Two", PlayerSide.White, "C20", "2026.04.25", [], [CreateMoveAnalysis(GamePhase.Opening, 110, "opening_principles")]),
            CreateResult("Burst", "Three", PlayerSide.White, "C20", "2026.04.25", [], [CreateMoveAnalysis(GamePhase.Opening, 140, "opening_principles")]),
            CreateResult("Burst", "Four", PlayerSide.White, "C20", "2026.04.25", [], [CreateMoveAnalysis(GamePhase.Opening, 170, "opening_principles")]),
            CreateResult("Burst", "Five", PlayerSide.White, "C20", "2026.04.25", [], [CreateMoveAnalysis(GamePhase.Opening, 200, "opening_principles")])
        ]);

        PlayerProfileService service = new(store);

        bool found = service.TryBuildProfile("Burst", out PlayerProfileReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal(ProfileProgressDirection.InsufficientData, report!.ProgressSignal.Direction);
        Assert.Empty(report.LabelTrends);
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

    private static OpeningTrainingSessionResult CreateTrainingResult(
        string sessionId,
        string playerKey,
        string displayName,
        string eco,
        int correct,
        int playable,
        int wrong)
    {
        DateTime completedUtc = DateTime.Parse("2026-04-20T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        List<OpeningTrainingRecordedAttempt> attempts = [];
        foreach (OpeningTrainingScore score in Enumerable.Repeat(OpeningTrainingScore.Correct, correct)
            .Concat(Enumerable.Repeat(OpeningTrainingScore.Playable, playable))
            .Concat(Enumerable.Repeat(OpeningTrainingScore.Wrong, wrong)))
        {
            attempts.Add(new OpeningTrainingRecordedAttempt(
                $"position-{attempts.Count + 1}",
                OpeningTrainingMode.LineRecall,
                OpeningTrainingSourceKind.OpeningWeakness,
                eco,
                OpeningCatalog.GetName(eco),
                "opening_principles",
                "h3",
                "h3",
                "h2h3",
                score,
                completedUtc));
        }

        return new OpeningTrainingSessionResult(
            sessionId,
            playerKey,
            displayName,
            completedUtc.AddMinutes(-20),
            completedUtc,
            OpeningTrainingSessionOutcome.Completed,
            attempts.Count,
            attempts.Count,
            correct,
            playable,
            wrong,
            [eco],
            ["opening_principles"],
            attempts);
    }

    private sealed class FakeAnalysisStore : IAnalysisStore, IOpeningTrainingHistoryStore
    {
        private readonly IReadOnlyList<GameAnalysisResult> results;
        private readonly IReadOnlyList<StoredMoveAnalysis> moveAnalyses;
        private readonly List<OpeningTrainingSessionResult> trainingHistory;

        public FakeAnalysisStore(
            IReadOnlyList<GameAnalysisResult> results,
            IReadOnlyList<StoredMoveAnalysis>? moveAnalyses = null,
            IReadOnlyList<OpeningTrainingSessionResult>? trainingHistory = null)
        {
            this.results = results;
            this.moveAnalyses = moveAnalyses ?? BuildStoredMoves(results);
            this.trainingHistory = (trainingHistory ?? []).ToList();
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
        public void SaveImportedGames(IReadOnlyList<ImportedGame> games) => throw new NotSupportedException();
        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game) => throw new NotSupportedException();
        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result) => throw new NotSupportedException();
        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result) => throw new NotSupportedException();
        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state) => throw new NotSupportedException();
        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state) => throw new NotSupportedException();
        public void SaveOpeningTrainingSessionResult(OpeningTrainingSessionResult result) => trainingHistory.Add(result);
        public IReadOnlyList<OpeningTrainingSessionResult> ListOpeningTrainingSessionResults(string? playerKey = null, int limit = 200)
        {
            IEnumerable<OpeningTrainingSessionResult> filtered = trainingHistory;
            if (!string.IsNullOrWhiteSpace(playerKey))
            {
                filtered = filtered.Where(result => string.Equals(result.PlayerKey, playerKey.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(result => result.CompletedUtc)
                .Take(limit)
                .ToList();
        }
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
