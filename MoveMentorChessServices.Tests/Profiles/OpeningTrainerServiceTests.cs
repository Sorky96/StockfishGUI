using System.Globalization;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class OpeningTrainerServiceTests
{
    [Fact]
    public void OpeningTrainerService_BuildsSessionAcrossAllRequestedSourcesAndModes()
    {
        GameAnalysisResult gameA = CreateResult(
            "Alpha",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 18, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 22, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(7, 95, "opening_principles", "d2d4")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Alpha",
            "Gamma",
            PlayerSide.White,
            "C20",
            "2026.04.08",
            ["e4", "e5", "Nf3", "d6", "Bc4", "Nf6", "h3", "Be7"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 16, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 20, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(7, 85, "opening_principles", "d2d4")
            ]);
        GameAnalysisResult gameC = CreateResult(
            "Alpha",
            "Delta",
            PlayerSide.White,
            "B01",
            "2026.04.16",
            ["Nf3", "d5", "h4", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 22, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 90, "opening_principles", "d2d4")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [gameA, gameB, gameC],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "d4", "Nf6"]),
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "d6", "Bc4", "Nf6", "d4", "Be7"]),
                CreateTheoryGame("B01", ["Nf3", "d5", "d4", "Nc6"])
            ]));

        bool found = service.TryBuildSession("Alpha", out OpeningTrainingSession? session);

        Assert.True(found);
        Assert.NotNull(session);
        Assert.Equal("alpha", session!.PlayerKey);
        Assert.Contains(OpeningTrainingMode.LineRecall, session.SupportedModes);
        Assert.Contains(OpeningTrainingMode.MistakeRepair, session.SupportedModes);
        Assert.Contains(OpeningTrainingMode.BranchAwareness, session.SupportedModes);
        Assert.Contains(OpeningTrainingSourceKind.ExampleGame, session.IncludedSources);
        Assert.Contains(OpeningTrainingSourceKind.OpeningWeakness, session.IncludedSources);
        Assert.Contains(OpeningTrainingSourceKind.FirstOpeningMistake, session.IncludedSources);
        Assert.NotEmpty(session.Lines);
        Assert.NotEmpty(session.SourceSummaries);

        OpeningTrainingPosition lineRecall = session.Positions.First(item => item.Mode == OpeningTrainingMode.LineRecall);
        Assert.Equal(OpeningTrainingSourceKind.ExampleGame, lineRecall.SourceKind);
        Assert.False(string.IsNullOrWhiteSpace(lineRecall.PlayedMove));
        Assert.NotEmpty(lineRecall.CandidateMoves);
        Assert.Contains(lineRecall.Tags, tag => tag.Equals("example-game", StringComparison.OrdinalIgnoreCase));

        OpeningTrainingPosition repair = session.Positions.First(item => item.Mode == OpeningTrainingMode.MistakeRepair);
        Assert.Equal(OpeningTrainingSourceKind.FirstOpeningMistake, repair.SourceKind);
        Assert.False(string.IsNullOrWhiteSpace(repair.PlayedMove));
        Assert.False(string.IsNullOrWhiteSpace(repair.BetterMove));
        Assert.Contains(repair.CandidateMoves, option => option.Role == OpeningTrainingMoveRole.Repair && option.IsPreferred);

        OpeningTrainingPosition branch = session.Positions.First(item => item.Mode == OpeningTrainingMode.BranchAwareness);
        Assert.Equal(OpeningTrainingSourceKind.OpeningWeakness, branch.SourceKind);
        Assert.NotNull(branch.Branches);
        Assert.NotEmpty(branch.Branches!);
        Assert.All(branch.Branches!, item => Assert.True(item.Frequency >= 1));
        Assert.Contains(branch.Branches!, item => item.RecommendedResponse is not null);
        Assert.Contains("imported", branch.BranchSelectionSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        OpeningTrainingSourceSummary weaknessSummary = Assert.Single(session.SourceSummaries, item => item.SourceKind == OpeningTrainingSourceKind.OpeningWeakness);
        Assert.True(weaknessSummary.PositionCount >= 1);
        Assert.Contains("C20", weaknessSummary.RelatedOpenings);

        Assert.True(service.TryBuildSession("Alpha", out OpeningTrainingSession? branchOnlySession, new OpeningTrainingSessionOptions(
            Modes: [OpeningTrainingMode.BranchAwareness])));
        Assert.NotNull(branchOnlySession);
        Assert.All(branchOnlySession!.Positions, position => Assert.Equal(OpeningTrainingMode.BranchAwareness, position.Mode));
        Assert.Equal([OpeningTrainingMode.BranchAwareness], branchOnlySession.SupportedModes);

        Assert.True(service.TryBuildSession("Alpha", out OpeningTrainingSession? b01Session, new OpeningTrainingSessionOptions(
            TargetOpenings: ["B01"],
            MaxPositions: 6,
            MaxPositionsPerSource: 6)));
        Assert.NotNull(b01Session);
        Assert.NotEmpty(b01Session!.Positions);
        Assert.All(b01Session.Positions, position => Assert.Equal("B01", position.Eco));
    }

    [Fact]
    public void OpeningTrainerService_RespectsRequestedSourcesAndLimits()
    {
        GameAnalysisResult game = CreateResult(
            "Sigma",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "Nf3", "Nc6", "h3", "Nf6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 18, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 15, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 95, "opening_principles", "f1c4")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [game],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6", "Bc4", "Nf6"])
            ]));
        OpeningTrainingSessionOptions options = new(
            Sources: [OpeningTrainingSourceKind.FirstOpeningMistake],
            MaxPositions: 1,
            MaxPositionsPerSource: 1);

        bool found = service.TryBuildSession("Sigma", out OpeningTrainingSession? session, options);

        Assert.True(found);
        Assert.NotNull(session);
        Assert.Single(session!.Positions);
        Assert.Single(session.SourceSummaries);
        Assert.Equal(OpeningTrainingSourceKind.FirstOpeningMistake, session.SourceSummaries[0].SourceKind);
        Assert.All(session.Positions, item => Assert.Equal(OpeningTrainingSourceKind.FirstOpeningMistake, item.SourceKind));
        Assert.Equal(OpeningTrainingMode.MistakeRepair, session.Positions[0].Mode);
    }

    [Fact]
    public void OpeningTrainerService_EvaluatesLineRecallAsCorrectPlayableOrWrong()
    {
        GameAnalysisResult gameA = CreateResult(
            "Tau",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 95, "opening_principles", "g1f3")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Tau",
            "Gamma",
            PlayerSide.White,
            "C20",
            "2026.04.08",
            ["d4", "d5", "Nf3", "Nf6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 18, null, "d2d4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 85, "opening_principles", "g1f3")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [gameA, gameB],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5"]),
                CreateTheoryGame("D00", ["d4", "d5", "Nf3", "Nf6"])
            ]));
        Assert.True(service.TryBuildSession("Tau", out OpeningTrainingSession? session));
        OpeningTrainingPosition lineRecall = session!.Positions.First(position => position.Mode == OpeningTrainingMode.LineRecall);

        OpeningLineRecallAttemptResult correct = service.EvaluateLineRecallMove(lineRecall, "e2e4");
        OpeningLineRecallAttemptResult playable = service.EvaluateLineRecallMove(lineRecall, "d4");
        OpeningLineRecallAttemptResult wrong = service.EvaluateLineRecallMove(lineRecall, "h4");

        Assert.Equal(OpeningLineRecallGrade.Correct, correct.Grade);
        Assert.NotEmpty(correct.PreferredReferences);
        Assert.Contains(correct.PreferredReferences, option => option.ReferenceKind == OpeningLineRecallReferenceKind.ReferenceLine);

        Assert.Equal(OpeningLineRecallGrade.Playable, playable.Grade);
        Assert.NotEmpty(playable.MatchingReferences);
        Assert.Contains(playable.MatchingReferences, option => option.ReferenceKind == OpeningLineRecallReferenceKind.BetterMove);

        Assert.Equal(OpeningLineRecallGrade.Wrong, wrong.Grade);
        Assert.NotNull(wrong.ResolvedSan);
        Assert.NotEmpty(wrong.PreferredReferences);
    }

    [Fact]
    public void OpeningTrainerService_EvaluatesMistakeRepairAsCorrectPlayableOrWrong()
    {
        GameAnalysisResult gameA = CreateResult(
            "Lambda",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "h3", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 95, "opening_principles", "g1f3")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Lambda",
            "Gamma",
            PlayerSide.White,
            "C20",
            "2026.04.08",
            ["e4", "e5", "a3", "Nf6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 18, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 85, "opening_principles", "d2d4")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [gameA, gameB],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6"]),
                CreateTheoryGame("C20", ["e4", "e5", "d4", "Nf6"])
            ]));
        Assert.True(service.TryBuildSession("Lambda", out OpeningTrainingSession? session));
        OpeningTrainingPosition repair = session!.Positions.First(position => position.Mode == OpeningTrainingMode.MistakeRepair);

        OpeningMistakeRepairAttemptResult correct = service.EvaluateMistakeRepairMove(repair, "g1f3");
        OpeningMistakeRepairAttemptResult playable = service.EvaluateMistakeRepairMove(repair, "d4");
        OpeningMistakeRepairAttemptResult wrong = service.EvaluateMistakeRepairMove(repair, "h3");

        Assert.Equal(OpeningMistakeRepairGrade.Correct, correct.Grade);
        Assert.Contains("Better move:", correct.BetterMoveSummary);
        Assert.Contains("Why:", correct.WhyBetter);
        Assert.NotEmpty(correct.PreferredReferences);

        Assert.Equal(OpeningMistakeRepairGrade.Playable, playable.Grade);
        Assert.NotEmpty(playable.PlayableReferences);
        Assert.Contains(playable.MatchingReferences, option => option.Role == OpeningTrainingMoveRole.Repair && !option.IsPreferred);

        Assert.Equal(OpeningMistakeRepairGrade.Wrong, wrong.Grade);
        Assert.NotNull(wrong.ResolvedSan);
        Assert.DoesNotContain(wrong.MatchingReferences, option => option.Role == OpeningTrainingMoveRole.Repair);
    }

    [Fact]
    public void OpeningTrainerService_UsesImportedTheoryAsSourceOfTruth()
    {
        GameAnalysisResult playerGame = CreateResult(
            "TheoryUser",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "Nf3", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 95, "opening_principles", "g1f3")
            ]);
        ImportedGame importedTheoryGame = new(
            BuildPgn("TheoryBook", "BookLine", "2026.04.02", "D00", ["d4", "d5", "c4", "e6"]),
            ["d4", "d5", "c4", "e6"],
            "TheoryBook",
            "BookLine",
            null,
            null,
            "2026.04.02",
            "1-0",
            "D00",
            "Imported");

        OpeningTrainerService service = new(new FakeAnalysisStore([playerGame], theoryGames: [importedTheoryGame]));

        Assert.True(service.TryBuildSession("TheoryUser", out OpeningTrainingSession? session));
        OpeningTrainingPosition lineRecall = session!.Positions.First(position => position.Mode == OpeningTrainingMode.LineRecall);

        OpeningLineRecallAttemptResult localMove = service.EvaluateLineRecallMove(lineRecall, "e4");
        OpeningLineRecallAttemptResult importedMove = service.EvaluateLineRecallMove(lineRecall, "d4");

        Assert.Equal(OpeningLineRecallGrade.Wrong, localMove.Grade);
        Assert.Equal(OpeningLineRecallGrade.Correct, importedMove.Grade);
        Assert.Contains(importedMove.PreferredReferences, option => option.Uci == "d2d4");
        Assert.DoesNotContain(lineRecall.CandidateMoves, option => option.Uci == "e2e4");
    }

    [Fact]
    public void OpeningTrainerService_UsesOnlyImportedTheoryBranchesForBranchAwareness()
    {
        GameAnalysisResult gameA = CreateResult(
            "Sorky 1996",
            "Beta",
            PlayerSide.White,
            "C23",
            "2026.04.01",
            ["e4", "e5", "Bc4", "Bc5", "Qh5", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 18, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 20, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 95, "opening_principles", "g1f3")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Sorky 1996",
            "Gamma",
            PlayerSide.White,
            "C23",
            "2026.04.08",
            ["e4", "e5", "Bc4", "Nf6", "Qh5", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 18, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 20, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 90, "opening_principles", "g1f3")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [gameA, gameB],
            theoryGames:
            [
                CreateTheoryGame("C23", ["e4", "e5", "Bc4", "Bc5"])
            ]));

        bool found = service.TryBuildSession("Sorky 1996", out OpeningTrainingSession? session, new OpeningTrainingSessionOptions(
            Modes: [OpeningTrainingMode.BranchAwareness],
            Sources: [OpeningTrainingSourceKind.OpeningWeakness],
            TargetOpenings: ["C23"]));

        Assert.True(found);
        Assert.NotNull(session);
        Assert.NotEmpty(session!.Positions);
        Assert.All(session.Positions, position =>
        {
            Assert.Equal(OpeningTrainingMode.BranchAwareness, position.Mode);
            Assert.Equal("C23", position.Eco);
            Assert.Contains("imported opponent branch", position.BranchSelectionSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(position.Branches);
            Assert.NotEmpty(position.Branches!);
            Assert.DoesNotContain(position.Branches!, branch => branch.OpponentMove is "Nf6" or "Nc6");
        });
    }

    [Fact]
    public void OpeningTrainerService_UsesImportedTheoryMoveAsDisplayedBetterMove()
    {
        GameAnalysisResult game = CreateResult(
            "TheoryDisplay",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "h3", "Nc6", "Bc4", "Bc5"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 95, "opening_principles", "g1f3"),
                new AnalyzedMoveSpec(5, 90, "opening_principles", "d2d4")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [game],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5"])
            ]));

        Assert.True(service.TryBuildSession("TheoryDisplay", out OpeningTrainingSession? session, new OpeningTrainingSessionOptions(
            Sources: [OpeningTrainingSourceKind.ExampleGame, OpeningTrainingSourceKind.FirstOpeningMistake],
            Modes: [OpeningTrainingMode.LineRecall, OpeningTrainingMode.MistakeRepair],
            MaxPositions: 8,
            MaxPositionsPerSource: 8)));
        Assert.NotNull(session);

        OpeningTrainingPosition lineRecall = session!.Positions.First(position => position.Mode == OpeningTrainingMode.LineRecall);
        OpeningTrainingPosition repair = session.Positions.First(position => position.Mode == OpeningTrainingMode.MistakeRepair);
        string expectedLineRecallMove = lineRecall.CandidateMoves.First(option => option.IsPreferred).DisplayText;
        string expectedRepairMove = repair.CandidateMoves.First(option => option.IsPreferred).DisplayText;

        Assert.Equal(expectedLineRecallMove, lineRecall.BetterMove);
        Assert.Equal(expectedRepairMove, repair.BetterMove);
    }

    [Fact]
    public void OpeningTrainerService_EvaluatesAllModesThroughCommonAttemptResult()
    {
        GameAnalysisResult gameA = CreateResult(
            "Omega",
            "Beta",
            PlayerSide.White,
            "C20",
            "2026.04.01",
            ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 18, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 22, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(7, 95, "opening_principles", "d2d4")
            ]);
        GameAnalysisResult gameB = CreateResult(
            "Omega",
            "Gamma",
            PlayerSide.White,
            "C20",
            "2026.04.08",
            ["e4", "e5", "Nf3", "d6", "Bc4", "Nf6", "h3", "Be7"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 20, null, "e2e4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 16, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(5, 20, null, "f1c4", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(7, 85, "opening_principles", "d2d4")
            ]);
        GameAnalysisResult gameC = CreateResult(
            "Omega",
            "Delta",
            PlayerSide.White,
            "B01",
            "2026.04.16",
            ["Nf3", "d5", "h4", "Nc6"],
            [CreateSelectedMistake("opening_principles", MoveQualityBucket.Mistake)],
            [
                new AnalyzedMoveSpec(1, 22, null, "g1f3", MoveQualityBucket.Good),
                new AnalyzedMoveSpec(3, 90, "opening_principles", "d2d4")
            ]);

        OpeningTrainerService service = new(new FakeAnalysisStore(
            [gameA, gameB, gameC],
            theoryGames:
            [
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "d4", "Nf6"]),
                CreateTheoryGame("C20", ["e4", "e5", "Nf3", "d6", "Bc4", "Nf6", "d4", "Be7"]),
                CreateTheoryGame("B01", ["Nf3", "d5", "d4", "Nc6"])
            ]));
        Assert.True(service.TryBuildSession("Omega", out OpeningTrainingSession? session));

        OpeningTrainingPosition lineRecall = session!.Positions.First(position => position.Mode == OpeningTrainingMode.LineRecall);
        OpeningTrainingAttemptResult lineResult = service.EvaluateMove(lineRecall, lineRecall.CandidateMoves.First(option => option.IsPreferred).Uci!);

        Assert.Equal(OpeningTrainingMode.LineRecall, lineResult.Mode);
        Assert.Equal(lineRecall.SourceKind, lineResult.PositionSource);
        Assert.Equal(OpeningTrainingScore.Correct, lineResult.Score);
        Assert.NotEmpty(lineResult.ExpectedMoves);
        Assert.False(string.IsNullOrWhiteSpace(lineResult.ShortExplanation));

        OpeningTrainingPosition repair = session.Positions.First(position => position.Mode == OpeningTrainingMode.MistakeRepair);
        OpeningTrainingAttemptResult repairResult = service.EvaluateMove(repair, repair.CandidateMoves.First(option => option.IsPreferred).Uci!);

        Assert.Equal(OpeningTrainingMode.MistakeRepair, repairResult.Mode);
        Assert.Equal(repair.SourceKind, repairResult.PositionSource);
        Assert.Equal(OpeningTrainingScore.Correct, repairResult.Score);
        Assert.Contains(repairResult.ExpectedMoves, option => option.Role == OpeningTrainingMoveRole.Repair);
        Assert.Contains("Correct repair", repairResult.ShortExplanation);

        OpeningTrainingPosition branch = session.Positions.First(position => position.Mode == OpeningTrainingMode.BranchAwareness);
        OpeningTrainingBranch primaryBranch = branch.Branches!.OrderByDescending(item => item.Frequency).ThenBy(item => item.OpponentMove).First();
        OpeningTrainingAttemptResult branchResult = service.EvaluateMove(branch, primaryBranch.OpponentMoveUci ?? primaryBranch.OpponentMove);

        Assert.Equal(OpeningTrainingMode.BranchAwareness, branchResult.Mode);
        Assert.Equal(branch.SourceKind, branchResult.PositionSource);
        Assert.Equal(OpeningTrainingScore.Correct, branchResult.Score);
        Assert.Contains(branchResult.ExpectedMoves, option => option.Role == OpeningTrainingMoveRole.Alternative);
        Assert.Contains("Correct branch", branchResult.ShortExplanation);
    }

    private static GameAnalysisResult CreateResult(
        string whitePlayer,
        string blackPlayer,
        PlayerSide side,
        string eco,
        string dateText,
        IReadOnlyList<string> sanMoves,
        IReadOnlyList<SelectedMistake> highlightedMistakes,
        IReadOnlyList<AnalyzedMoveSpec> moveSpecs)
    {
        ImportedGame game = new(
            BuildPgn(whitePlayer, blackPlayer, dateText, eco, sanMoves),
            sanMoves,
            whitePlayer,
            blackPlayer,
            null,
            null,
            dateText,
            "1-0",
            eco,
            "Local");

        IReadOnlyList<ReplayPly> replay = new GameReplayService().Replay(game);
        IReadOnlyDictionary<int, ReplayPly> replayIndex = replay.ToDictionary(item => item.Ply);
        IReadOnlyList<MoveAnalysisResult> moveAnalyses = moveSpecs
            .Select(spec => CreateMoveAnalysis(replayIndex[spec.Ply], spec.Cpl, spec.Label, spec.BestMoveUci, spec.QualityOverride))
            .ToList();

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
        ReplayPly replay,
        int cpl,
        string? label,
        string bestMoveUci,
        MoveQualityBucket? qualityOverride = null)
    {
        MoveQualityBucket quality = qualityOverride ?? (cpl >= 200
            ? MoveQualityBucket.Blunder
            : MoveQualityBucket.Mistake);

        return new MoveAnalysisResult(
            replay,
            new EngineAnalysis(replay.FenBefore, [], bestMoveUci),
            new EngineAnalysis(replay.FenAfter, [], null),
            20,
            -cpl,
            null,
            null,
            cpl,
            quality,
            0,
            label is null ? null : new MistakeTag(label, 0.8, ["evidence"]),
            new MoveExplanation("Short", "Hint", "Detailed"));
    }

    private static string BuildPgn(string whitePlayer, string blackPlayer, string dateText, string eco, IReadOnlyList<string> sanMoves)
    {
        List<string> tokens = [];
        for (int i = 0; i < sanMoves.Count; i += 2)
        {
            int moveNumber = i / 2 + 1;
            tokens.Add($"{moveNumber}. {sanMoves[i]}");
            if (i + 1 < sanMoves.Count)
            {
                tokens.Add(sanMoves[i + 1]);
            }
        }

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

    private sealed record AnalyzedMoveSpec(
        int Ply,
        int Cpl,
        string? Label,
        string BestMoveUci,
        MoveQualityBucket? QualityOverride = null);

    private sealed class FakeAnalysisStore : IAnalysisStore, IOpeningTheoryStore
    {
        private readonly IReadOnlyList<GameAnalysisResult> results;
        private readonly IReadOnlyList<StoredMoveAnalysis> moveAnalyses;
        private readonly Dictionary<string, ImportedGame> importedGames;
        private readonly Dictionary<string, OpeningTheoryPosition> theoryPositions;
        private readonly Dictionary<string, IReadOnlyList<OpeningTheoryMove>> theoryMoves;

        public FakeAnalysisStore(
            IReadOnlyList<GameAnalysisResult> results,
            IReadOnlyList<StoredMoveAnalysis>? moveAnalyses = null,
            IReadOnlyList<ImportedGame>? theoryGames = null)
        {
            this.results = results;
            this.moveAnalyses = moveAnalyses ?? BuildStoredMoves(results);
            importedGames = results.ToDictionary(result => GameFingerprint.Compute(result.Game.PgnText), result => result.Game);
            (theoryPositions, theoryMoves) = BuildTheoryData(theoryGames ?? results.Select(result => result.Game).ToList());
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

            return filtered.Take(limit).ToList();
        }

        public bool DeleteImportedGame(string gameFingerprint) => throw new NotSupportedException();
        public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200)
        {
            IEnumerable<KeyValuePair<string, ImportedGame>> filtered = importedGames;
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filtered = filtered.Where(item =>
                    (item.Value.WhitePlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.Value.BlackPlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return filtered
                .Take(limit)
                .Select(item => new SavedImportedGameSummary(
                    item.Key,
                    $"{item.Value.WhitePlayer} vs {item.Value.BlackPlayer}",
                    item.Value.WhitePlayer,
                    item.Value.BlackPlayer,
                    item.Value.DateText,
                    item.Value.Result,
                    item.Value.Eco,
                    item.Value.Site,
                    DateTime.UtcNow))
                .ToList();
        }

        public void SaveImportedGame(ImportedGame game) => importedGames[GameFingerprint.Compute(game.PgnText)] = game;
        public void SaveImportedGames(IReadOnlyList<ImportedGame> games)
        {
            foreach (ImportedGame game in games)
            {
                importedGames[GameFingerprint.Compute(game.PgnText)] = game;
            }
        }

        public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
            => importedGames.TryGetValue(gameFingerprint, out game);
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
        public Dictionary<string, TheoryMoveAccumulator> Moves { get; } = new(StringComparer.OrdinalIgnoreCase);
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
