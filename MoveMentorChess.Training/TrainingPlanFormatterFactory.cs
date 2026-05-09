namespace MoveMentorChess.Training;

public static class TrainingPlanFormatterFactory
{
    public static ITrainingPlanFormatter CreateDefault()
    {
        ILocalAdviceModel? localModel = AdviceRuntimeCatalog.TryCreateConfiguredModel();
        return new LocalModelTrainingPlanFormatter(localModel);
    }
}
