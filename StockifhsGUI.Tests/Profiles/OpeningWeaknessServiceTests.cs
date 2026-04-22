using System.Globalization;
using Xunit;

namespace StockifhsGUI.Tests;

public sealed class OpeningWeaknessServiceTests
{
    [Fact]
    public void OpeningWeaknessService_AggregatesWeakOpeningsSequencesAndBetterMoves()
    {
        GameAnalysisResult gameA = CreateResult(
            "Alpha",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                CreateMoveAnalysis(GamePhase.Opening, 95, "opening_principles", moveNumber: 2, san: "h3", bestMoveUci: "g1f3"),
                CreateMoveAnalysis(GamePhase.Opening, 120, "king_safety", moveNumber: 4, san: "g4", bestMoveUci: "d2d4"),
                CreateMoveAnalysis(GamePhase.Middlegame, 180, "missed_tactic", moveNumber: 10, san: "Qh5", bestMoveUci: "d2d4")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Alpha",
            "Gamma",
            PlayerSide.White,
            "C20",
            "2026.04.08",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                CreateMoveAnalysis(GamePhase.Opening, 85, "opening_principles", moveNumber: 2, san: "a3", bestMoveUci: "g1f3"),
                CreateMoveAnalysis(GamePhase.Opening, 110, "king_safety", moveNumber: 5, san: "f3", bestMoveUci: "e2e4")
            ]);
        GameAnalysisResult gameC = CreateResult(
            "Alpha",
            "Delta",
            PlayerSide.White,
            "B01",
            "2026.04.16",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", moveNumber: 3, san: "h4", bestMoveUci: "g1f3")]);

        OpeningWeaknessService service = new(new FakeAnalysisStore([gameA, gameB, gameC]));

        bool found = service.TryBuildReport("Alpha", out OpeningWeaknessReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal(3, report!.GamesAnalyzed);
        Assert.Equal(3, report.OpeningGamesAnalyzed);
        Assert.True((report.AverageOpeningCentipawnLoss ?? 0) >= 90);

        OpeningWeaknessEntry c20 = Assert.Single(report.WeakOpenings, item => item.Eco == "C20");
        Assert.Equal(2, c20.Count);
        Assert.Equal("opening_principles", c20.FirstRecurringMistakeType);
        Assert.Equal(2, c20.FirstRecurringMistakeCount);
        Assert.True((c20.AverageOpeningCentipawnLoss ?? 0) >= 100);
        Assert.NotEmpty(c20.ExampleGames);
        Assert.NotEmpty(c20.ExampleBetterMoves);
        Assert.All(c20.ExampleBetterMoves, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BetterMove));
            Assert.False(string.IsNullOrWhiteSpace(item.FenBefore));
            Assert.Equal(PlayerSide.White, item.Side);
        });

        OpeningMistakeSequenceStat sequence = Assert.Single(
            report.RecurringMistakeSequences,
            item => item.Key == "opening_principles -> king_safety");
        Assert.Equal(2, sequence.Count);
        Assert.Equal("C20", sequence.RepresentativeEco);
    }

    [Fact]
    public void OpeningWeaknessService_MergesStructuredMovesWithLegacyResults()
    {
        GameAnalysisResult storedGame = CreateResult(
            "Alpha",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 90, "opening_principles", moveNumber: 2, san: "h3", bestMoveUci: "g1f3")]);
        GameAnalysisResult legacyOnlyGame = CreateResult(
            "Alpha",
            "Gamma",
            PlayerSide.White,
            "B01",
            "2026.04.05",
            [CreateSelectedMistake("king_safety", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 110, "king_safety", moveNumber: 4, san: "g4", bestMoveUci: "d2d4")]);

        FakeAnalysisStore store = new(
            [storedGame, legacyOnlyGame],
            BuildStoredMoves([storedGame]));
        OpeningWeaknessService service = new(store);

        bool found = service.TryBuildReport("Alpha", out OpeningWeaknessReport? report);

        Assert.True(found);
        Assert.NotNull(report);
        Assert.Equal(2, report!.GamesAnalyzed);
        Assert.Contains(report.WeakOpenings, item => item.Eco == "C20");
        Assert.Contains(report.WeakOpenings, item => item.Eco == "B01");
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
        const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string afterFen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        ReplayPly replay = new(
            moveNumber * 2 - 1,
            moveNumber,
            PlayerSide.White,
            san,
            san,
            bestMoveUci,
            startFen,
            afterFen,
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

        MoveQualityBucket quality = cpl >= 200
            ? MoveQualityBucket.Blunder
            : MoveQualityBucket.Mistake;

        return new MoveAnalysisResult(
            replay,
            new EngineAnalysis(startFen, [], bestMoveUci),
            new EngineAnalysis(afterFen, [], null),
            20,
            -cpl,
            null,
            null,
            cpl,
            quality,
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
                    DateTime.Parse("2026-04-18T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
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
