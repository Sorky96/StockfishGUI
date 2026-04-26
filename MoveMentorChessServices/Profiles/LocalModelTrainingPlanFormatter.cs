namespace MoveMentorChessServices;

public sealed class LocalModelTrainingPlanFormatter : ITrainingPlanFormatter
{
    private readonly ILocalAdviceModel? localModel;
    private readonly ITrainingPlanFormatter fallbackFormatter;

    public LocalModelTrainingPlanFormatter(
        ILocalAdviceModel? localModel = null,
        ITrainingPlanFormatter? fallbackFormatter = null)
    {
        this.localModel = localModel;
        this.fallbackFormatter = fallbackFormatter ?? new HeuristicTrainingPlanFormatter();
    }

    public bool UsedFallback { get; private set; }

    public string? FallbackReason { get; private set; }

    public TrainingPlanFormattedOutput Format(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (localModel is null || !localModel.IsAvailable)
        {
            return FormatFallback(report, audienceLevel, trainerStyle, "Local training plan model is unavailable.");
        }

        string prompt = TrainingPlanLlmPromptFormatter.BuildPrompt(report, audienceLevel, trainerStyle);
        LocalModelAdviceRequest request = CreateRequest(prompt, audienceLevel, trainerStyle);

        try
        {
            string? rawResponse = localModel.Generate(request);
            if (!LocalModelTrainingPlanResponseParser.TryParse(rawResponse, out TrainingPlanFormattedOutput? output)
                || output is null)
            {
                return FormatFallback(report, audienceLevel, trainerStyle, "Local training plan model returned an unparsable response.");
            }

            if (!TrainingPlanFormattedOutputValidator.IsValid(output, report))
            {
                return FormatFallback(report, audienceLevel, trainerStyle, "Local training plan model returned text outside the training plan data contract.");
            }

            UsedFallback = false;
            FallbackReason = null;
            return output;
        }
        catch (Exception ex)
        {
            return FormatFallback(report, audienceLevel, trainerStyle, $"Local training plan model failed: {ex.Message}");
        }
    }

    private TrainingPlanFormattedOutput FormatFallback(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle,
        string reason)
    {
        UsedFallback = true;
        FallbackReason = reason;
        return fallbackFormatter.Format(report, audienceLevel, trainerStyle);
    }

    private static LocalModelAdviceRequest CreateRequest(
        string prompt,
        PlayerProfileAudienceLevel audienceLevel,
        AdviceNarrationStyle trainerStyle)
    {
        const string StartFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        ReplayPly replay = new(
            1,
            1,
            PlayerSide.White,
            "training-plan",
            "training-plan",
            "0000",
            StartFen,
            StartFen,
            string.Empty,
            string.Empty,
            GamePhase.Middlegame,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            false,
            false,
            false);

        return new LocalModelAdviceRequest(
            replay,
            MoveQualityBucket.Good,
            null,
            null,
            null,
            ToExplanationLevel(audienceLevel),
            null,
            prompt,
            trainerStyle,
            TrainingPlanLlmPromptFormatter.OutputKeys);
    }

    private static ExplanationLevel ToExplanationLevel(PlayerProfileAudienceLevel audienceLevel)
    {
        return audienceLevel switch
        {
            PlayerProfileAudienceLevel.Beginner => ExplanationLevel.Beginner,
            PlayerProfileAudienceLevel.Advanced => ExplanationLevel.Advanced,
            _ => ExplanationLevel.Intermediate
        };
    }
}
