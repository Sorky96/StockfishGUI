namespace MoveMentorChess.Training;

public sealed class LocalModelTrainingPlanFormatter : ITrainingPlanFormatter
{
    private readonly ITrainingPlanFormatter fallbackFormatter;

    public LocalModelTrainingPlanFormatter(
        ITrainingPlanFormatter? fallbackFormatter = null)
    {
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

        return FormatFallback(report, audienceLevel, trainerStyle, "Local training plan model is unavailable in this build.");
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
}
