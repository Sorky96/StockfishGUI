namespace MoveMentorChessServices;

public interface ITrainingPlanFormatter
{
    TrainingPlanFormattedOutput Format(
        TrainingPlanReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer);
}
