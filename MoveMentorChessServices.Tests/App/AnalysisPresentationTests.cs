using MoveMentorChess.Presentation.Models;
using Xunit;

namespace MoveMentorChessServices.Tests.App;

public sealed class AnalysisPresentationTests
{
    [Fact]
    public void BuildPhaseSegments_groups_adjacent_phases()
    {
        ReplayPly[] replay =
        [
            Ply(1, 1, PlayerSide.White, "e4", GamePhase.Opening),
            Ply(2, 1, PlayerSide.Black, "e5", GamePhase.Opening),
            Ply(3, 2, PlayerSide.White, "Nf3", GamePhase.Middlegame),
            Ply(4, 2, PlayerSide.Black, "Nc6", GamePhase.Endgame),
            Ply(5, 3, PlayerSide.White, "Bb5", GamePhase.Endgame)
        ];

        List<PhaseSegment> segments = AnalysisTimelinePresentation.BuildPhaseSegments(replay);

        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal(GamePhase.Opening, segment.Phase);
                Assert.Equal(2, segment.PlyCount);
            },
            segment =>
            {
                Assert.Equal(GamePhase.Middlegame, segment.Phase);
                Assert.Equal(1, segment.PlyCount);
            },
            segment =>
            {
                Assert.Equal(GamePhase.Endgame, segment.Phase);
                Assert.Equal(2, segment.PlyCount);
            });
    }

    [Fact]
    public void SelectedMistakeViewItem_marks_costliest_reviewed_highlight()
    {
        MoveAnalysisResult quiet = Analysis(
            Ply(1, 1, PlayerSide.White, "e4", GamePhase.Opening),
            MoveQualityBucket.Inaccuracy,
            "king_safety",
            centipawnLoss: 45);
        MoveAnalysisResult costly = Analysis(
            Ply(8, 4, PlayerSide.Black, "Qh4+", GamePhase.Middlegame),
            MoveQualityBucket.Blunder,
            "hanging_piece",
            centipawnLoss: 260);
        SelectedMistake quietMistake = Mistake(quiet);
        SelectedMistake costlyMistake = Mistake(costly);
        GameAnalysisResult result = Result([quiet, costly], [quietMistake, costlyMistake]);

        SelectedMistakeViewItem item = new(costlyMistake, result, isReviewed: true);

        Assert.Equal(costly, item.LeadMove);
        Assert.Equal("4... Qh4+", item.MoveRange);
        Assert.Equal("Loose piece", item.LabelText);
        Assert.Equal("Costliest", item.PriorityText);
        Assert.Equal("Reviewed", item.ReviewStatusText);
        Assert.Contains("evaluation loss 260 cp", item.MetaText);
    }

    [Fact]
    public void BuildSummaryDiagnosis_prefers_recurring_pattern_and_reports_costliest_move()
    {
        MoveAnalysisResult firstTactic = Analysis(
            Ply(3, 2, PlayerSide.White, "Nf3", GamePhase.Opening),
            MoveQualityBucket.Mistake,
            "missed_tactic",
            centipawnLoss: 120);
        MoveAnalysisResult secondTactic = Analysis(
            Ply(9, 5, PlayerSide.White, "Qh5", GamePhase.Middlegame),
            MoveQualityBucket.Mistake,
            "missed_tactic",
            centipawnLoss: 180);
        MoveAnalysisResult costliest = Analysis(
            Ply(12, 6, PlayerSide.Black, "Bxf2+", GamePhase.Middlegame),
            MoveQualityBucket.Blunder,
            "material_loss",
            centipawnLoss: 320);
        SelectedMistake firstTacticMistake = Mistake(firstTactic);
        SelectedMistake secondTacticMistake = Mistake(secondTactic);
        SelectedMistake costliestMistake = Mistake(costliest);
        GameAnalysisResult result = Result(
            [firstTactic, secondTactic, costliest],
            [firstTacticMistake, secondTacticMistake, costliestMistake]);

        string summary = AnalysisTimelinePresentation.BuildSummaryDiagnosis(result);

        Assert.Contains("Biggest pattern: Missed tactics, 2 times", summary);
        Assert.Contains("average loss 150 cp", summary);
        Assert.Contains("Costliest moment: 6... Bxf2+.", summary);
    }

    [Fact]
    public void BuildTopCandidateMovesText_adds_coaching_notes_for_best_and_second_line()
    {
        MoveAnalysisResult lead = Analysis(
            Ply(4, 2, PlayerSide.Black, "Nc6", GamePhase.Opening),
            MoveQualityBucket.Mistake,
            "opening_principles",
            centipawnLoss: 90);

        string text = AnalysisCoachingTextFormatter.BuildTopCandidateMovesText(lead);

        Assert.Contains("1. e2e4", text);
        Assert.Contains("best: because it keeps development and central control on track", text);
        Assert.Contains("2. g1f3", text);
        Assert.Contains("playable, but less direct for development", text);
    }

    [Fact]
    public void SimplifyAdviceText_strips_known_narration_prefixes()
    {
        string simplified = AnalysisCoachingTextFormatter.SimplifyAdviceText(
            "Coach recap: Candidate-move check: Check forcing replies before moving.");

        Assert.Equal("Check forcing replies before moving.", simplified);
    }

    [Fact]
    public void AnalysisSelectedDetailsPresenter_builds_complete_detail_copy()
    {
        MoveAnalysisResult lead = Analysis(
            Ply(4, 2, PlayerSide.Black, "Nc6", GamePhase.Opening),
            MoveQualityBucket.Mistake,
            "opening_principles",
            centipawnLoss: 90);
        SelectedMistake mistake = Mistake(lead);
        MoveExplanation explanation = new(
            "Practical view: Develop first. Then calculate tactics.",
            "Speed drill: Check development before moving. Review candidate replies.",
            "Coach recap: The played move slowed development while the engine preferred a cleaner developing move.");

        AnalysisSelectedDetailsPresentation details = AnalysisSelectedDetailsPresenter.Build(
            mistake,
            lead,
            openingReview: null,
            explanation,
            isLoading: false,
            feedback: null);

        Assert.Equal("opening_principles", details.EffectiveLabel);
        Assert.Equal("2... Nc6", details.MoveText);
        Assert.Equal("Mistake - Opening discipline", details.QualityText);
        Assert.Equal("Evaluation loss: 90 cp", details.LossText);
        Assert.Contains("Phase: opening", details.ContextText);
        Assert.Contains("Motif: Opening discipline", details.ContextText);
        Assert.Equal("Develop first. Then calculate tactics.", details.AdviceText);
        Assert.Contains("cleaner developing move", details.WhyText);
        Assert.Contains("Am I developing a piece and fighting for the center?", details.ChecklistText);
        Assert.Contains("Label: Opening discipline", details.DetailsText);
    }

    [Fact]
    public void BuildSnapshotArrows_returns_neutral_played_move_arrow()
    {
        MoveAnalysisResult lead = Analysis(
            Ply(4, 2, PlayerSide.Black, "Nc6", GamePhase.Opening),
            MoveQualityBucket.Mistake,
            "opening_principles",
            centipawnLoss: 90);

        IReadOnlyList<AnalysisSnapshotArrow> arrows = AnalysisSnapshotPresentation.BuildSnapshotArrows(
            lead,
            AnalysisSnapshotMode.Played);

        AnalysisSnapshotArrow arrow = Assert.Single(arrows);
        Assert.Equal("e2", arrow.FromSquare);
        Assert.Equal("e4", arrow.ToSquare);
        Assert.Equal("#D9822B", arrow.ColorHex);
    }

    [Fact]
    public void AnalysisSelectionState_filters_reviewed_items_and_reports_cache()
    {
        MoveAnalysisResult blunder = Analysis(
            Ply(4, 2, PlayerSide.Black, "Nc6", GamePhase.Opening),
            MoveQualityBucket.Blunder,
            "hanging_piece",
            centipawnLoss: 240);
        MoveAnalysisResult mistake = Analysis(
            Ply(6, 3, PlayerSide.Black, "Nf6", GamePhase.Middlegame),
            MoveQualityBucket.Mistake,
            "missed_tactic",
            centipawnLoss: 110);
        GameAnalysisResult result = Result([blunder, mistake], [Mistake(blunder), Mistake(mistake)]);
        AnalysisSelectionState state = new();
        state.SetCurrentResult(result, isCached: true);
        state.MarkReviewed(blunder);

        AnalysisFilterResult reviewedOnly = state.BuildFilterResult(new AnalysisFilterOption(
            "Reviewed",
            QualityFilter: null,
            AnalysisReviewFilter.Reviewed));
        AnalysisFilterResult mistakesOnly = state.BuildFilterResult(new AnalysisFilterOption(
            "Mistakes",
            MoveQualityBucket.Mistake));

        SelectedMistakeViewItem reviewedItem = Assert.Single(reviewedOnly.Items);
        Assert.Equal(blunder, reviewedItem.LeadMove);
        Assert.Equal("Reviewed", reviewedItem.ReviewStatusText);
        Assert.Contains("Loaded from cache", reviewedOnly.SummaryText);
        SelectedMistakeViewItem mistakeItem = Assert.Single(mistakesOnly.Items);
        Assert.Equal(mistake, mistakeItem.LeadMove);
        Assert.Equal(string.Empty, mistakeItem.ReviewStatusText);
    }

    private static GameAnalysisResult Result(
        IReadOnlyList<MoveAnalysisResult> analyses,
        IReadOnlyList<SelectedMistake> mistakes)
    {
        ImportedGame game = new(
            PgnText: "1. e4 e5 2. Nf3 Nc6",
            SanMoves: analyses.Select(analysis => analysis.Replay.San).ToList(),
            WhitePlayer: "White",
            BlackPlayer: "Black",
            WhiteElo: null,
            BlackElo: null,
            DateText: null,
            Result: null,
            Eco: null,
            Site: null);
        return new GameAnalysisResult(
            game,
            PlayerSide.White,
            analyses.Select(analysis => analysis.Replay).ToList(),
            analyses,
            mistakes);
    }

    private static SelectedMistake Mistake(MoveAnalysisResult analysis)
        => new(
            [analysis],
            analysis.Quality,
            analysis.MistakeTag,
            analysis.Explanation ?? new MoveExplanation("short", "hint"));

    private static MoveAnalysisResult Analysis(
        ReplayPly replay,
        MoveQualityBucket quality,
        string label,
        int centipawnLoss)
    {
        EngineAnalysis before = new(
            replay.FenBefore,
            [
                new EngineLine("e2e4", 40, null, ["e2e4"]),
                new EngineLine("g1f3", -90, null, ["g1f3"])
            ],
            "e2e4");
        EngineAnalysis after = new(
            replay.FenAfter,
            [new EngineLine("g1f3", -centipawnLoss, null, ["g1f3"])],
            "g1f3");
        return new MoveAnalysisResult(
            replay,
            before,
            after,
            EvalBeforeCp: 40,
            EvalAfterCp: 40 - centipawnLoss,
            BestMateIn: null,
            PlayedMateIn: null,
            CentipawnLoss: centipawnLoss,
            Quality: quality,
            MaterialDeltaCp: 0,
            MistakeTag: new MistakeTag(label, 0.9, []),
            Explanation: new MoveExplanation("short", "hint"));
    }

    private static ReplayPly Ply(
        int ply,
        int moveNumber,
        PlayerSide side,
        string san,
        GamePhase phase)
        => new(
            ply,
            moveNumber,
            side,
            san,
            san,
            "e2e4",
            "8/8/8/8/8/8/8/8 w - - 0 1",
            "8/8/8/8/8/8/8/8 b - - 0 1",
            "8/8/8/8/8/8/8/8 w - - 0 1",
            "8/8/8/8/8/8/8/8 b - - 0 1",
            phase,
            "P",
            null,
            "e2",
            "e4",
            IsCapture: false,
            IsEnPassant: false,
            IsCastle: false);
}
