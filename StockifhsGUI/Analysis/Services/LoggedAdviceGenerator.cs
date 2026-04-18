namespace StockifhsGUI;

public sealed class LoggedAdviceGenerator : IAdviceGenerator
{
    private readonly IAdviceGenerator inner;
    private readonly IAdviceGenerationLogger? logger;
    private readonly AdviceGeneratorMode mode;
    private readonly string generatorName;

    public LoggedAdviceGenerator(
        IAdviceGenerator inner,
        AdviceGeneratorMode mode,
        string generatorName,
        IAdviceGenerationLogger? logger = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.mode = mode;
        this.generatorName = string.IsNullOrWhiteSpace(generatorName) ? inner.GetType().Name : generatorName;
        this.logger = logger;
    }

    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate,
        AdviceGenerationContext? context = null)
    {
        MoveExplanation explanation = inner.Generate(replay, quality, tag, bestMoveUci, centipawnLoss, level, context);
        logger?.Record(new AdviceGenerationTrace(
            DateTime.UtcNow,
            generatorName,
            mode,
            UsedFallback: false,
            FallbackReason: null,
            context?.Source ?? "unknown",
            context?.GameFingerprint,
            context?.AnalyzedSide,
            level,
            quality,
            tag?.Label ?? "general",
            replay.San,
            bestMoveUci,
            centipawnLoss,
            explanation.ShortText,
            explanation.DetailedText,
            explanation.TrainingHint));
        return explanation;
    }
}
