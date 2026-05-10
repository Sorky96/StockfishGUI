namespace MoveMentorChess.Training;

public static class TrainingPlanFormatterFactory
{
    public static ITrainingPlanFormatter CreateDefault() => new LocalModelTrainingPlanFormatter();
}
