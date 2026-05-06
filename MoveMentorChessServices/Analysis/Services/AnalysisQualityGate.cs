namespace MoveMentorChessServices;

public sealed class AnalysisQualityGate
{
    private const double WeakEvidenceConfidenceCap = 0.49;
    private const double ContradictoryEvidenceConfidence = 0.25;
    private const int GreatMoveMaxCentipawnLoss = 20;
    private const int GreatMoveAlternativeDropCp = 80;
    private const int GreatMoveCloseAlternativeCp = 40;

    private readonly JsonlDiagnosticsLogger<QualityGateReport>? logger;
    private readonly IAdviceGenerator fallbackAdviceGenerator;
    private readonly AdviceGenerationSettings settings;

    public AnalysisQualityGate(
        JsonlDiagnosticsLogger<QualityGateReport>? logger = null,
        IAdviceGenerator? fallbackAdviceGenerator = null,
        AdviceGenerationSettings? settings = null)
    {
        this.logger = logger ?? QualityGateDiagnosticsLogger.CreateDefault();
        this.settings = settings ?? AdviceGenerationSettings.Default;
        this.fallbackAdviceGenerator = fallbackAdviceGenerator ?? new LocalHeuristicAdviceGenerator(this.settings);
    }

    public MoveAnalysisResult Apply(
        MoveAnalysisResult result,
        string gameFingerprint,
        PlayerSide analyzedSide,
        AdviceGenerationContext? adviceContext = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<QualityGateFinding> findings = [];
        int correctedCount = 0;
        int fallbackCount = 0;
        MistakeTag? tag = result.MistakeTag;
        MoveExplanation? explanation = result.Explanation;

        AddEngineConsistencyFindings(result, gameFingerprint, findings);

        MoveQualityBucket expectedQuality = ClassifyQuality(result, analyzedSide);
        if (expectedQuality != result.Quality)
        {
            findings.Add(CreateFinding(
                "quality_bucket_mismatch",
                QualityGateSeverity.Warning,
                $"Quality {result.Quality} does not match CPL/mate thresholds; expected {expectedQuality}.",
                result,
                gameFingerprint,
                "reported_in_quality_gate"));
        }

        if (tag is not null)
        {
            EvidenceValidation evidence = ValidateEvidence(result, tag);
            if (evidence.Action == EvidenceAction.Unclassify)
            {
                tag = new MistakeTag("unclassified", ContradictoryEvidenceConfidence, BuildCorrectedEvidence(tag, evidence.Code));
                correctedCount++;
                findings.Add(CreateFinding(
                    evidence.Code,
                    QualityGateSeverity.Warning,
                    evidence.Message,
                    result,
                    gameFingerprint,
                    "set_label_unclassified"));
            }
            else if (evidence.Action == EvidenceAction.LowerConfidence)
            {
                tag = tag with
                {
                    Confidence = Math.Min(tag.Confidence, WeakEvidenceConfidenceCap),
                    Evidence = BuildCorrectedEvidence(tag, evidence.Code)
                };
                correctedCount++;
                findings.Add(CreateFinding(
                    evidence.Code,
                    QualityGateSeverity.Warning,
                    evidence.Message,
                    result,
                    gameFingerprint,
                    "lowered_confidence"));
            }
        }

        if (explanation is not null)
        {
            string adviceText = $"{explanation.ShortText} {explanation.DetailedText} {explanation.TrainingHint}";
            if (!AdviceQualityValidator.HasOnlyAllowedReferencedMoves(
                    adviceText,
                    result.Replay.Uci,
                    result.BeforeAnalysis.BestMoveUci,
                    out string unexpectedMove))
            {
                findings.Add(CreateFinding(
                    "advice_references_unexpected_uci",
                    QualityGateSeverity.Warning,
                    $"Advice referenced UCI move {unexpectedMove}, which was not present in input data.",
                    result,
                    gameFingerprint,
                    "fallback_advice"));
                explanation = GenerateFallbackAdvice(result, tag, adviceContext);
                fallbackCount++;
                correctedCount++;
            }
            else if (!AdviceQualityValidator.IsUsable(explanation, tag, result.BeforeAnalysis.BestMoveUci, settings, out string adviceReason))
            {
                findings.Add(CreateFinding(
                    adviceReason,
                    QualityGateSeverity.Warning,
                    $"Advice failed quality validation: {adviceReason}.",
                    result,
                    gameFingerprint,
                    "fallback_advice"));
                explanation = GenerateFallbackAdvice(result, tag, adviceContext);
                fallbackCount++;
                correctedCount++;
            }
        }

        if (findings.Count > 0)
        {
            logger?.Record(new QualityGateReport(DateTime.UtcNow, findings, correctedCount, fallbackCount));
        }

        if (!ReferenceEquals(tag, result.MistakeTag) || !ReferenceEquals(explanation, result.Explanation))
        {
            return result with
            {
                MistakeTag = tag,
                Explanation = explanation
            };
        }

        return result;
    }

    private MoveExplanation GenerateFallbackAdvice(
        MoveAnalysisResult result,
        MistakeTag? tag,
        AdviceGenerationContext? adviceContext)
    {
        return fallbackAdviceGenerator.Generate(
            result.Replay,
            result.Quality,
            tag,
            result.BeforeAnalysis.BestMoveUci,
            result.CentipawnLoss,
            ExplanationLevel.Intermediate,
            adviceContext);
    }

    private static void AddEngineConsistencyFindings(
        MoveAnalysisResult result,
        string gameFingerprint,
        List<QualityGateFinding> findings)
    {
        if (!IsLegalMove(result.Replay.FenBefore, result.Replay.Uci))
        {
            findings.Add(CreateFinding(
                "played_move_illegal",
                QualityGateSeverity.Failure,
                $"Played move {result.Replay.Uci} is not legal in FenBefore.",
                result,
                gameFingerprint,
                "reported_in_quality_gate"));
        }

        if (!string.IsNullOrWhiteSpace(result.BeforeAnalysis.BestMoveUci)
            && !string.Equals(result.BeforeAnalysis.BestMoveUci, "(none)", StringComparison.Ordinal)
            && !IsLegalMove(result.Replay.FenBefore, result.BeforeAnalysis.BestMoveUci))
        {
            findings.Add(CreateFinding(
                "best_move_illegal",
                QualityGateSeverity.Failure,
                $"Best move {result.BeforeAnalysis.BestMoveUci} is not legal in FenBefore.",
                result,
                gameFingerprint,
                "reported_in_quality_gate"));
        }

        if (result.EvalBeforeCp is int before
            && result.EvalAfterCp is int after
            && result.BestMateIn is null
            && result.PlayedMateIn is null)
        {
            int expected = Math.Max(0, before - after);
            if (result.CentipawnLoss != expected)
            {
                findings.Add(CreateFinding(
                    "centipawn_loss_mismatch",
                    QualityGateSeverity.Warning,
                    $"CentipawnLoss {result.CentipawnLoss?.ToString() ?? "n/a"} does not match EvalBefore - EvalAfter ({expected}).",
                    result,
                    gameFingerprint,
                    "reported_in_quality_gate"));
            }
        }
    }

    private static MoveQualityBucket ClassifyQuality(MoveAnalysisResult result, PlayerSide analyzedSide)
    {
        if (result.Quality == MoveQualityBucket.Book)
        {
            return MoveQualityBucket.Book;
        }

        if (result.BestMateIn is > 0 && result.PlayedMateIn is null)
        {
            return MoveQualityBucket.Blunder;
        }

        if (result.BestMateIn is > 0 && result.PlayedMateIn is <= 0)
        {
            return MoveQualityBucket.Blunder;
        }

        if (result.PlayedMateIn is < 0)
        {
            return MoveQualityBucket.Blunder;
        }

        int loss = result.CentipawnLoss ?? 0;
        if (loss > 300)
        {
            return MoveQualityBucket.Blunder;
        }

        if (loss > 150)
        {
            return MoveQualityBucket.Mistake;
        }

        if (loss > 80)
        {
            return MoveQualityBucket.Inaccuracy;
        }

        if (MoveBrilliancyDetector.IsBrilliant(result, analyzedSide))
        {
            return MoveQualityBucket.Brilliant;
        }

        if (IsGreatMove(result, analyzedSide))
        {
            return MoveQualityBucket.Great;
        }

        if (!string.IsNullOrWhiteSpace(result.BeforeAnalysis.BestMoveUci)
            && string.Equals(result.Replay.Uci, result.BeforeAnalysis.BestMoveUci, StringComparison.Ordinal))
        {
            return MoveQualityBucket.Best;
        }

        if (loss <= 5)
        {
            return MoveQualityBucket.Best;
        }

        if (loss <= 20)
        {
            return MoveQualityBucket.Excellent;
        }

        return MoveQualityBucket.Good;
    }

    private static bool IsGreatMove(MoveAnalysisResult result, PlayerSide analyzedSide)
    {
        if (result.CentipawnLoss is not int loss
            || loss > GreatMoveMaxCentipawnLoss
            || result.EvalAfterCp is not int playedCp)
        {
            return false;
        }

        List<int> alternativeScores = result.BeforeAnalysis.Lines
            .Where(line => !string.Equals(line.MoveUci, result.Replay.Uci, StringComparison.Ordinal))
            .Select(line => NormalizeScore(line, analyzedSide, result.Replay.Side))
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();
        if (alternativeScores.Count == 0)
        {
            return false;
        }

        bool hasClearlyWorseAlternative = alternativeScores.Any(score => playedCp - score >= GreatMoveAlternativeDropCp);
        int closeAlternativeCount = alternativeScores.Count(score => score >= playedCp - GreatMoveCloseAlternativeCp);

        return hasClearlyWorseAlternative && closeAlternativeCount <= 1;
    }

    private static int? NormalizeScore(EngineLine line, PlayerSide analyzedSide, PlayerSide sideToMove)
    {
        int sign = analyzedSide == sideToMove ? 1 : -1;
        return line.Centipawns is int cp ? cp * sign : null;
    }

    private static EvidenceValidation ValidateEvidence(MoveAnalysisResult result, MistakeTag tag)
    {
        IReadOnlyList<string> evidence = tag.Evidence;
        string label = tag.Label;

        if (string.Equals(label, "opening_principles", StringComparison.Ordinal)
            && result.Replay.Phase != GamePhase.Opening)
        {
            return EvidenceValidation.Unclassify("evidence_opening_phase_mismatch", "opening_principles was assigned outside the opening phase.");
        }

        if (string.Equals(label, "endgame_technique", StringComparison.Ordinal)
            && result.Replay.Phase != GamePhase.Endgame)
        {
            return EvidenceValidation.Unclassify("evidence_endgame_phase_mismatch", "endgame_technique was assigned outside the endgame phase.");
        }

        bool valid = label switch
        {
            "material_loss" => result.MaterialDeltaCp < 0
                || EvidenceContains(evidence, "material", "swing", "delta", "loss"),
            "hanging_piece" => EvidenceContains(evidence, "hanging", "free_to_take", "underdefended", "piece_lost", "attack_deficit"),
            "king_safety" => EvidenceContains(evidence, "king", "shield", "shelter", "castle", "attack"),
            "opening_principles" => EvidenceContains(evidence, "development", "develop", "center", "central", "castle", "queen", "rook", "wing_pawn"),
            "endgame_technique" => EvidenceContains(evidence, "king", "centralization", "endgame", "activity", "technical"),
            _ => true
        };

        return valid
            ? EvidenceValidation.Valid()
            : EvidenceValidation.LowerConfidence($"evidence_missing_{label}", $"{label} does not have the minimum required evidence.");
    }

    private static bool EvidenceContains(IReadOnlyList<string> evidence, params string[] tokens)
    {
        foreach (string item in evidence)
        {
            foreach (string token in tokens)
            {
                if (item.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> BuildCorrectedEvidence(MistakeTag original, string correctionCode)
    {
        List<string> evidence = [..original.Evidence];
        evidence.Add($"quality_gate_{correctionCode}");
        return evidence;
    }

    private static bool IsLegalMove(string fenBefore, string? moveUci)
    {
        if (string.IsNullOrWhiteSpace(moveUci))
        {
            return false;
        }

        ChessGame game = new();
        return game.TryLoadFen(fenBefore, out _)
            && game.TryApplyUci(moveUci, out AppliedMoveInfo? appliedMove, out _)
            && appliedMove is not null;
    }

    private static QualityGateFinding CreateFinding(
        string code,
        QualityGateSeverity severity,
        string message,
        MoveAnalysisResult result,
        string gameFingerprint,
        string? correctiveAction)
    {
        return new QualityGateFinding(
            code,
            severity,
            message,
            gameFingerprint,
            result.Replay.Ply,
            result.MistakeTag?.Label,
            result.Quality,
            correctiveAction);
    }

    private enum EvidenceAction
    {
        None,
        LowerConfidence,
        Unclassify
    }

    private sealed record EvidenceValidation(EvidenceAction Action, string Code, string Message)
    {
        public static EvidenceValidation Valid() => new(EvidenceAction.None, string.Empty, string.Empty);
        public static EvidenceValidation LowerConfidence(string code, string message) => new(EvidenceAction.LowerConfidence, code, message);
        public static EvidenceValidation Unclassify(string code, string message) => new(EvidenceAction.Unclassify, code, message);
    }
}
