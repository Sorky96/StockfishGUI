namespace MoveMentorChessServices;

public sealed class LocalModelPlayerProfileFormatter : IPlayerProfileFormatter
{
    private readonly ILocalAdviceModel? localModel;
    private readonly IPlayerProfileFormatter fallbackFormatter;

    public LocalModelPlayerProfileFormatter(
        ILocalAdviceModel? localModel = null,
        IPlayerProfileFormatter? fallbackFormatter = null)
    {
        this.localModel = localModel;
        this.fallbackFormatter = fallbackFormatter ?? new HeuristicPlayerProfileFormatter();
    }

    public bool UsedFallback { get; private set; }

    public string? FallbackReason { get; private set; }

    public PlayerProfileFormattedOutput Format(
        PlayerProfileReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (localModel is null || !localModel.IsAvailable)
        {
            return FormatFallback(report, audienceLevel, trainerStyle, "Local profile model is unavailable.");
        }

        string prompt = PlayerProfileLlmPromptFormatter.BuildPrompt(report, audienceLevel, trainerStyle);
        LocalModelAdviceRequest request = CreateRequest(prompt, audienceLevel, trainerStyle);

        try
        {
            string? rawResponse = localModel.Generate(request);
            if (!LocalModelPlayerProfileResponseParser.TryParse(rawResponse, out PlayerProfileFormattedOutput? output)
                || output is null)
            {
                return FormatFallback(report, audienceLevel, trainerStyle, "Local profile model returned an unparsable response.");
            }

            if (!PlayerProfileFormattedOutputValidator.IsValid(output, report))
            {
                return FormatFallback(report, audienceLevel, trainerStyle, "Local profile model returned text outside the profile data contract.");
            }

            UsedFallback = false;
            FallbackReason = null;
            return output;
        }
        catch (Exception ex)
        {
            return FormatFallback(report, audienceLevel, trainerStyle, $"Local profile model failed: {ex.Message}");
        }
    }

    private PlayerProfileFormattedOutput FormatFallback(
        PlayerProfileReport report,
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
            "profile",
            "profile",
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
            PlayerProfileLlmPromptFormatter.OutputKeys);
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
