namespace MoveMentorChessServices;

public sealed class LocalModelAdviceGenerator : IAdviceGenerator, IAdviceGeneratorDiagnostics
{
    private readonly AdviceGenerationSettings settings;
    private readonly ILocalAdviceModel localModel;
    private readonly IAdviceGenerator fallbackGenerator;

    public LocalModelAdviceGenerator(
        AdviceGenerationSettings? settings = null,
        ILocalAdviceModel? localModel = null,
        IAdviceGenerator? fallbackGenerator = null)
    {
        this.settings = settings ?? AdviceGenerationSettings.Default;
        this.localModel = localModel ?? new NullLocalAdviceModel();
        this.fallbackGenerator = fallbackGenerator ?? new LocalHeuristicAdviceGenerator(this.settings);
    }

    public bool UsedFallback { get; private set; }

    public string? FallbackReason { get; private set; }

    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate,
        AdviceGenerationContext? context = null)
    {
        AdviceNarrationStyle narrationStyle = context?.NarrationStyle
            ?? AdviceNarrationStyle.RegularTrainer;

        LocalModelAdviceRequest request = new(
            replay,
            quality,
            tag,
            bestMoveUci,
            centipawnLoss,
            level,
            context,
            string.Empty,
            narrationStyle);
        request = request with { Prompt = AdvicePromptFormatter.BuildPrompt(request) };

        if (!localModel.IsAvailable)
        {
            return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, $"Local model '{localModel.Name}' is unavailable.");
        }

        try
        {
            string? rawResponse = localModel.Generate(request);
            if (!LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response)
                || response is null)
            {
                return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, $"Local model '{localModel.Name}' returned an unparsable response.");
            }

            if (!IsResponseGrounded(response, replay, tag, bestMoveUci, out string groundingReason))
            {
                return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, groundingReason);
            }

            MoveExplanation candidate = new(
                Shorten(response.ShortText, settings.MaxShortTextLength),
                Shorten(response.TrainingHint, settings.MaxTrainingHintLength),
                Shorten(response.DetailedText, settings.MaxDetailedTextLength));

            if (!AdviceQualityValidator.IsUsable(candidate, tag, bestMoveUci, settings, out string adviceReason))
            {
                return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, $"Local model '{localModel.Name}' returned weak advice: {adviceReason}.");
            }

            if (!AdviceQualityValidator.HasOnlyAllowedReferencedMoves(
                    $"{candidate.ShortText} {candidate.DetailedText} {candidate.TrainingHint}",
                    replay.Uci,
                    bestMoveUci,
                    out string unexpectedMove))
            {
                return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, $"Local model '{localModel.Name}' referenced move {unexpectedMove} outside the input data.");
            }

            UsedFallback = false;
            FallbackReason = null;
            return candidate;
        }
        catch (Exception ex)
        {
            return GenerateFallback(replay, quality, tag, bestMoveUci, centipawnLoss, level, context, $"Local model '{localModel.Name}' failed: {ex.Message}");
        }
    }

    private MoveExplanation GenerateFallback(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level,
        AdviceGenerationContext? context,
        string reason)
    {
        UsedFallback = true;
        FallbackReason = reason;
        return fallbackGenerator.Generate(replay, quality, tag, bestMoveUci, centipawnLoss, level, context);
    }

    private static string Shorten(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0 || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        if (maxLength <= 3)
        {
            return text[..maxLength];
        }

        int candidateLength = Math.Max(1, maxLength - 3);
        return $"{text[..candidateLength].Trim()}...";
    }

    private static bool IsResponseGrounded(
        LocalModelAdviceResponse response,
        ReplayPly replay,
        MistakeTag? tag,
        string? bestMoveUci,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(response.ReferencedBestMoveUci)
            || string.IsNullOrWhiteSpace(response.ReferencedLabel)
            || response.Confidence is null)
        {
            reason = "Local model response missed required grounding metadata.";
            return false;
        }

        string expectedLabel = tag?.Label ?? "general";
        if (!string.Equals(response.ReferencedLabel, expectedLabel, StringComparison.Ordinal))
        {
            reason = $"Local model response referenced label '{response.ReferencedLabel}' instead of '{expectedLabel}'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bestMoveUci)
            && !string.Equals(response.ReferencedBestMoveUci, bestMoveUci, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Local model response referenced best move '{response.ReferencedBestMoveUci}' instead of '{bestMoveUci}'.";
            return false;
        }

        if (!IsLegalMove(replay.FenBefore, response.ReferencedBestMoveUci))
        {
            reason = $"Local model response referenced illegal best move '{response.ReferencedBestMoveUci}'.";
            return false;
        }

        if (response.Confidence is < 0.0 or > 1.0)
        {
            reason = "Local model response confidence metadata was outside 0..1.";
            return false;
        }

        return true;
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

    private sealed class NullLocalAdviceModel : ILocalAdviceModel
    {
        public string Name => "null-local-model";

        public bool IsAvailable => false;

        public string? Generate(LocalModelAdviceRequest request) => null;
    }
}
