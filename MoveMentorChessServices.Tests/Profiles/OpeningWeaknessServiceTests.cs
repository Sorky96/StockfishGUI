using System.Globalization;
using Xunit;

namespace MoveMentorChessServices.Tests;

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

        ImportedGame theoryGame = CreateTheoryGame("C20", ["Nf3"]);
        OpeningWeaknessService service = new(new FakeAnalysisStore([gameA, gameB, gameC], theoryGames: [theoryGame]));

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
        Assert.Equal(OpeningWeaknessCategory.FixNow, c20.Category);
        Assert.Contains("Opening to fix now", c20.CategoryReason);
        Assert.True((c20.AverageOpeningCentipawnLoss ?? 0) >= 100);
        Assert.NotEmpty(c20.ExampleGames);
        Assert.NotEmpty(c20.ExampleBetterMoves);
        Assert.All(c20.ExampleBetterMoves, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.BetterMove));
            Assert.False(string.IsNullOrWhiteSpace(item.FenBefore));
            Assert.Equal(PlayerSide.White, item.Side);
            Assert.Contains("Nf3", item.BetterMove, StringComparison.OrdinalIgnoreCase);
        });

        OpeningMistakeSequenceStat sequence = Assert.Single(
            report.RecurringMistakeSequences,
            item => item.Key == "opening_principles -> king_safety");
        Assert.Equal(2, sequence.Count);
        Assert.Equal("C20", sequence.RepresentativeEco);
    }

    [Fact]
    public void OpeningWeaknessService_SkipsNonTheoryPositionsButStillShowsLaterTheoryMatchedExamples()
    {
        GameAnalysisResult game = CreateResult(
            "Sorky 1996",
            "Beta",
            PlayerSide.White,
            "C23",
            "2026.04.01",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                CreateMoveAnalysis(GamePhase.Opening, 220, "missed_tactics", moveNumber: 5, san: "Qh5", bestMoveUci: "g1f3"),
                CreateMoveAnalysis(GamePhase.Opening, 95, "opening_principles", moveNumber: 3, san: "Bc4", bestMoveUci: "g1f3")
            ]);

        ImportedGame theoryGame = CreateTheoryGame("C23", ["e4", "e5", "Bc4", "Bc5"]);
        OpeningWeaknessService service = new(new FakeAnalysisStore([game], theoryGames: [theoryGame]));

        Assert.True(service.TryBuildReport("Sorky 1996", out OpeningWeaknessReport? report));
        Assert.NotNull(report);

        OpeningWeaknessEntry c23 = Assert.Single(report!.WeakOpenings, item => item.Eco == "C23");
        Assert.NotEmpty(c23.ExampleBetterMoves);
        Assert.All(c23.ExampleBetterMoves, item => Assert.False(string.IsNullOrWhiteSpace(item.BetterMove)));
    }

    [Fact]
    public void OpeningWeaknessService_ClassifiesOpeningsByFrequencyCostMistakeAndTrend()
    {
        GameAnalysisResult stableA = CreateResult(
            "Alpha",
            "Beta",
            PlayerSide.White,
            "A00",
            "2026.04.01",
            [],
            [CreateMoveAnalysis(GamePhase.Opening, 30, "opening_principles", moveNumber: 2, san: "Nf3", bestMoveUci: "g1f3")]);
        GameAnalysisResult stableB = CreateResult(
            "Alpha",
            "Gamma",
            PlayerSide.White,
            "A00",
            "2026.04.02",
            [],
            [CreateMoveAnalysis(GamePhase.Opening, 35, "opening_principles", moveNumber: 2, san: "Nc3", bestMoveUci: "b1c3")]);
        GameAnalysisResult review = CreateResult(
            "Alpha",
            "Delta",
            PlayerSide.White,
            "B01",
            "2026.04.03",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 80, "opening_principles", moveNumber: 3, san: "h4", bestMoveUci: "g1f3")]);
        GameAnalysisResult fixA = CreateResult(
            "Alpha",
            "Epsilon",
            PlayerSide.White,
            "C20",
            "2026.04.04",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 100, "opening_principles", moveNumber: 2, san: "h3", bestMoveUci: "g1f3")]);
        GameAnalysisResult fixB = CreateResult(
            "Alpha",
            "Zeta",
            PlayerSide.White,
            "C20",
            "2026.04.05",
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [CreateMoveAnalysis(GamePhase.Opening, 95, "opening_principles", moveNumber: 2, san: "a3", bestMoveUci: "g1f3")]);

        OpeningWeaknessService service = new(new FakeAnalysisStore([stableA, stableB, review, fixA, fixB]));

        Assert.True(service.TryBuildReport("Alpha", out OpeningWeaknessReport? report));

        Assert.Equal(OpeningWeaknessCategory.FixNow, report!.WeakOpenings.Single(item => item.Eco == "C20").Category);
        Assert.Equal(OpeningWeaknessCategory.ReviewLater, report.WeakOpenings.Single(item => item.Eco == "B01").Category);
        Assert.Equal(OpeningWeaknessCategory.Stable, report.WeakOpenings.Single(item => item.Eco == "A00").Category);
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
            : cpl >= 70
                ? MoveQualityBucket.Mistake
                : MoveQualityBucket.Good;

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

    private static ImportedGame CreateTheoryGame(string eco, IReadOnlyList<string> sanMoves, string dateText = "2026.04.30")
    {
        return new ImportedGame(
            BuildPgn("TheoryBook", "TheoryLine", dateText, eco, sanMoves),
            sanMoves,
            "TheoryBook",
            "TheoryLine",
            null,
            null,
            dateText,
            "1-0",
            eco,
            "Imported");
    }

    private static string BuildPgn(string whitePlayer, string blackPlayer, string dateText, string eco, IReadOnlyList<string> tokens)
    {
        return string.Join(Environment.NewLine,
        [
            $"[White \"{whitePlayer}\"]",
            $"[Black \"{blackPlayer}\"]",
            $"[Date \"{dateText}\"]",
            $"[Result \"1-0\"]",
            $"[ECO \"{eco}\"]",
            string.Empty,
            $"{string.Join(' ', tokens)} 1-0"
        ]);
    }

    private sealed class FakeAnalysisStore : IAnalysisStore, IOpeningTheoryStore
    {
        private readonly IReadOnlyList<GameAnalysisResult> results;
        private readonly IReadOnlyList<StoredMoveAnalysis> moveAnalyses;
        private readonly Dictionary<string, OpeningTheoryPosition> theoryPositions;
        private readonly Dictionary<string, IReadOnlyList<OpeningTheoryMove>> theoryMoves;

        public FakeAnalysisStore(
            IReadOnlyList<GameAnalysisResult> results,
            IReadOnlyList<StoredMoveAnalysis>? moveAnalyses = null,
            IReadOnlyList<ImportedGame>? theoryGames = null)
        {
            this.results = results;
            this.moveAnalyses = moveAnalyses ?? BuildStoredMoves(results);
            (theoryPositions, theoryMoves) = BuildTheoryData(theoryGames ?? []);
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
        public bool TryGetOpeningPositionByKey(string positionKey, out OpeningTheoryPosition? position)
        {
            bool found = theoryPositions.TryGetValue(positionKey, out OpeningTheoryPosition? value);
            position = value;
            return found;
        }
        public IReadOnlyList<OpeningTheoryMove> GetOpeningMovesByPositionKey(string positionKey, int limit = 10, bool playableOnly = false)
        {
            if (!theoryMoves.TryGetValue(positionKey, out IReadOnlyList<OpeningTheoryMove>? moves))
            {
                return [];
            }

            IEnumerable<OpeningTheoryMove> filtered = playableOnly
                ? moves.Where(move => move.IsPlayableMove)
                : moves;

            return filtered.Take(limit).ToList();
        }
        public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result) => throw new NotSupportedException();
        public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result) => throw new NotSupportedException();
        public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state) => throw new NotSupportedException();
        public void SaveWindowState(string gameFingerprint, AnalysisWindowState state) => throw new NotSupportedException();
    }

    private static (Dictionary<string, OpeningTheoryPosition> Positions, Dictionary<string, IReadOnlyList<OpeningTheoryMove>> Moves) BuildTheoryData(
        IReadOnlyList<ImportedGame> games)
    {
        Dictionary<string, TheoryPositionAccumulator> positions = new(StringComparer.Ordinal);
        int nextOrder = 0;

        foreach (ImportedGame game in games)
        {
            IReadOnlyList<ReplayPly> replay = new GameReplayService().Replay(game)
                .Where(item => item.Phase == GamePhase.Opening)
                .OrderBy(item => item.Ply)
                .ToList();

            foreach (ReplayPly ply in replay)
            {
                string fromKey = OpeningPositionKeyBuilder.Build(ply.FenBefore);
                string toKey = OpeningPositionKeyBuilder.Build(ply.FenAfter);
                if (!positions.TryGetValue(fromKey, out TheoryPositionAccumulator? position))
                {
                    position = new TheoryPositionAccumulator(fromKey, ply.FenBefore, ply.Ply, ply.MoveNumber, ply.Side == PlayerSide.White ? "w" : "b", game.Eco);
                    positions[fromKey] = position;
                }

                position.DistinctGameFingerprints.Add(GameFingerprint.Compute(game.PgnText));
                string edgeKey = $"{ply.Uci}|{toKey}";
                if (!position.Moves.TryGetValue(edgeKey, out TheoryMoveAccumulator? move))
                {
                    move = new TheoryMoveAccumulator(ply.Uci, ply.San, toKey, ply.FenAfter, game.Eco, nextOrder++);
                    position.Moves[edgeKey] = move;
                }

                move.OccurrenceCount++;
                move.DistinctGameFingerprints.Add(GameFingerprint.Compute(game.PgnText));
            }
        }

        Dictionary<string, OpeningTheoryPosition> theoryPositions = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<OpeningTheoryMove>> theoryMoves = new(StringComparer.Ordinal);

        foreach ((string positionKey, TheoryPositionAccumulator position) in positions)
        {
            OpeningGameMetadata metadata = new(position.Eco ?? string.Empty, OpeningCatalog.GetName(position.Eco), string.Empty);
            theoryPositions[positionKey] = new OpeningTheoryPosition(
                Guid.NewGuid(),
                position.PositionKey,
                position.Fen,
                position.Ply,
                position.MoveNumber,
                position.SideToMove,
                position.Moves.Values.Sum(item => item.OccurrenceCount),
                position.DistinctGameFingerprints.Count,
                metadata);

            IReadOnlyList<OpeningTheoryMove> moves = position.Moves.Values
                .OrderByDescending(item => item.OccurrenceCount)
                .ThenBy(item => item.FirstSeenOrder)
                .Select((item, index) => new OpeningTheoryMove(
                    Guid.NewGuid(),
                    theoryPositions[positionKey].Id,
                    Guid.NewGuid(),
                    item.MoveUci,
                    item.MoveSan,
                    item.OccurrenceCount,
                    item.DistinctGameFingerprints.Count,
                    index == 0,
                    index < 2,
                    index + 1,
                    item.ToPositionKey,
                    item.ToFen,
                    new OpeningGameMetadata(item.Eco ?? string.Empty, OpeningCatalog.GetName(item.Eco), string.Empty)))
                .ToList();
            theoryMoves[positionKey] = moves;
        }

        return (theoryPositions, theoryMoves);
    }

    private sealed class TheoryPositionAccumulator
    {
        public TheoryPositionAccumulator(string positionKey, string fen, int ply, int moveNumber, string sideToMove, string? eco)
        {
            PositionKey = positionKey;
            Fen = fen;
            Ply = ply;
            MoveNumber = moveNumber;
            SideToMove = sideToMove;
            Eco = eco;
        }

        public string PositionKey { get; }
        public string Fen { get; }
        public int Ply { get; }
        public int MoveNumber { get; }
        public string SideToMove { get; }
        public string? Eco { get; }
        public HashSet<string> DistinctGameFingerprints { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, TheoryMoveAccumulator> Moves { get; } = new(StringComparer.Ordinal);
    }

    private sealed class TheoryMoveAccumulator
    {
        public TheoryMoveAccumulator(string moveUci, string moveSan, string toPositionKey, string toFen, string? eco, int firstSeenOrder)
        {
            MoveUci = moveUci;
            MoveSan = moveSan;
            ToPositionKey = toPositionKey;
            ToFen = toFen;
            Eco = eco;
            FirstSeenOrder = firstSeenOrder;
        }

        public string MoveUci { get; }
        public string MoveSan { get; }
        public string ToPositionKey { get; }
        public string ToFen { get; }
        public string? Eco { get; }
        public int FirstSeenOrder { get; }
        public int OccurrenceCount { get; set; }
        public HashSet<string> DistinctGameFingerprints { get; } = new(StringComparer.Ordinal);
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
