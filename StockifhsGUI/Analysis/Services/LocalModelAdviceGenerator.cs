namespace StockifhsGUI;

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

            UsedFallback = false;
            FallbackReason = null;
            return new MoveExplanation(
                Shorten(response.ShortText, settings.MaxShortTextLength),
                Shorten(response.TrainingHint, settings.MaxTrainingHintLength),
                Shorten(response.DetailedText, settings.MaxDetailedTextLength));
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

    private sealed class NullLocalAdviceModel : ILocalAdviceModel
    {
        public string Name => "null-local-model";

        public bool IsAvailable => false;

        public string? Generate(LocalModelAdviceRequest request) => null;
    }
}
