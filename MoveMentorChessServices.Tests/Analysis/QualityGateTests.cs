using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class QualityGateTests
{
    [Fact]
    public void AnalysisQualityGate_UnclassifiesPhaseContradiction()
    {
        using TempJsonl<QualityGateReport> tempLog = new();
        AnalysisQualityGate gate = new(tempLog.Logger, new TemplateAdviceGenerator());
        MoveAnalysisResult move = CreateMoveAnalysis(
            MoveQualityBucket.Mistake,
            "opening_principles",
            ["early_queen_move"],
            GamePhase.Middlegame,
            180);

        MoveAnalysisResult corrected = gate.Apply(move, "game-1", PlayerSide.White);

        Assert.Equal("unclassified", corrected.MistakeTag?.Label);
        Assert.Contains("quality_gate_evidence_opening_phase_mismatch", corrected.MistakeTag?.Evidence ?? []);
    }

    [Fact]
    public void AnalysisQualityGate_LowersConfidenceWhenEvidenceIsTooWeak()
    {
        using TempJsonl<QualityGateReport> tempLog = new();
        AnalysisQualityGate gate = new(tempLog.Logger, new TemplateAdviceGenerator());
        MoveAnalysisResult move = CreateMoveAnalysis(
            MoveQualityBucket.Mistake,
            "hanging_piece",
            ["quality_mistake"],
            GamePhase.Middlegame,
            180);

        MoveAnalysisResult corrected = gate.Apply(move, "game-2", PlayerSide.White);

        Assert.Equal("hanging_piece", corrected.MistakeTag?.Label);
        Assert.True(corrected.MistakeTag?.Confidence <= 0.49);
        Assert.Contains("quality_gate_evidence_missing_hanging_piece", corrected.MistakeTag?.Evidence ?? []);
    }

    [Fact]
    public void LocalModelAdviceResponseParser_ParsesGroundingMetadata()
    {
        const string rawResponse = """
{
  "short_text": "Opening principles: Nf3 was more natural.",
  "detailed_text": "What: Qh5 moved the queen early. Why: Opening principles matter. Better: Nf3 developed. Watch next time: improve a piece first.",
  "training_hint": "Develop a minor piece before queen moves.",
  "referenced_best_move_uci": "g1f3",
  "referenced_label": "opening_principles",
  "confidence": 0.82
}
""";

        bool parsed = LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response);

        Assert.True(parsed);
        Assert.Equal("g1f3", response?.ReferencedBestMoveUci);
        Assert.Equal("opening_principles", response?.ReferencedLabel);
        Assert.Equal(0.82, response?.Confidence);
    }

    [Fact]
    public void LocalModelAdviceGenerator_FallsBackWhenGroundingMetadataIsMissing()
    {
        LocalModelAdviceGenerator generator = new(
            new AdviceGenerationSettings(AdviceGeneratorMode.Adaptive, 260, 220, 420),
            new FakeLocalAdviceModel("""
short_text: Opening principles: vague model text
detailed_text: What: text. Why: text. Better: text. Watch next time: text.
training_hint: develop a piece
"""),
            new TemplateAdviceGenerator());
        MoveAnalysisResult move = CreateMoveAnalysis(
            MoveQualityBucket.Inaccuracy,
            "opening_principles",
            ["early_queen_move"],
            GamePhase.Opening,
            110);

        MoveExplanation explanation = generator.Generate(
            move.Replay,
            move.Quality,
            move.MistakeTag,
            move.BeforeAnalysis.BestMoveUci,
            move.CentipawnLoss);

        Assert.True(generator.UsedFallback);
        Assert.Contains("metadata", generator.FallbackReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What:", explanation.DetailedText);
    }

    [Fact]
    public void ManualReviewSetBuilder_UsesReviewLimit()
    {
        List<StoredMoveAnalysis> moves = Enumerable.Range(1, 25)
            .Select(i => CreateStoredMove(i, i % 2 == 0 ? "hanging_piece" : "opening_principles"))
            .ToList();

        string markdown = ManualReviewSetBuilder.BuildMarkdown(moves, 20);

        Assert.Contains("Positions: 20", markdown);
        Assert.DoesNotContain("## 21.", markdown);
        Assert.Contains("Czy rozumiem blad?", markdown);
    }

    private static MoveAnalysisResult CreateMoveAnalysis(
        MoveQualityBucket quality,
        string label,
        IReadOnlyList<string> evidence,
        GamePhase phase,
        int cpl)
    {
        ReplayPly replay = new(
            1,
            1,
            PlayerSide.White,
            "a3",
            "a3",
            "a2a3",
            "4k3/8/8/8/8/8/P7/4K3 w - - 0 1",
            "4k3/8/8/8/8/P7/8/4K3 b - - 0 1",
            string.Empty,
            string.Empty,
            phase,
            "P",
            null,
            "a2",
            "a3",
            false,
            false,
            false);

        return new MoveAnalysisResult(
            replay,
            AnalysisFor(replay.FenBefore, "a2a4", 0, null, "a2a4"),
            AnalysisFor(replay.FenAfter, "e8e7", -cpl, null, "e8e7"),
            0,
            -cpl,
            null,
            null,
            cpl,
            quality,
            0,
            new MistakeTag(label, 0.90, evidence),
            new MoveExplanation(
                $"{label.Replace('_', ' ')}: a3 was inaccurate and a2a4 was stronger.",
                $"{label.Replace('_', ' ')}: check the pattern before moving.",
                $"What: a3 caused the issue. Why: {label.Replace('_', ' ')} applies. Better: a2a4 was stronger. Watch next time: use the motif cue."));
    }

    private static EngineAnalysis AnalysisFor(string fen, string bestMove, int? centipawns, int? mateIn, params string[] pv)
    {
        return new EngineAnalysis(
            fen,
            [new EngineLine(bestMove, centipawns, mateIn, pv.Length == 0 ? [bestMove] : pv)],
            bestMove);
    }

    private static StoredMoveAnalysis CreateStoredMove(int ply, string label)
    {
        return new StoredMoveAnalysis(
            "game",
            PlayerSide.White,
            14,
            3,
            null,
            DateTime.UtcNow,
            "White",
            "Black",
            "2026.04.29",
            "1-0",
            "A00",
            "Local",
            ply,
            ply,
            "a3",
            "a2a3",
            "4k3/8/8/8/8/8/P7/4K3 w - - 0 1",
            "4k3/8/8/8/8/P7/8/4K3 b - - 0 1",
            ply % 3 == 0 ? GamePhase.Endgame : ply % 2 == 0 ? GamePhase.Middlegame : GamePhase.Opening,
            0,
            -120,
            null,
            null,
            120,
            MoveQualityBucket.Inaccuracy,
            0,
            "a2a4",
            label,
            0.8,
            ["evidence"],
            "Short explanation",
            "What: issue. Why: reason. Better: a2a4. Watch next time: cue.",
            "Training hint",
            true);
    }

    private sealed class FakeLocalAdviceModel(string response) : ILocalAdviceModel
    {
        public string Name => "fake";
        public bool IsAvailable => true;
        public string? Generate(LocalModelAdviceRequest request) => response;
    }

    private sealed class TempJsonl<T> : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"MoveMentorChessServices-{Guid.NewGuid():N}.jsonl");

        public JsonlDiagnosticsLogger<T> Logger => new(path);

        public void Dispose()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
