namespace StockifhsGUI;

public static class AdviceGeneratorFactory
{
    public static IAdviceGenerator CreateDefault()
    {
        AdviceGenerationSettings settings = AdviceGenerationSettingsResolver.ResolveFromEnvironment();
        IAdviceGenerationLogger logger = FileAdviceGenerationLogger.CreateDefault();

        return settings.Mode switch
        {
            AdviceGeneratorMode.Template => new LoggedAdviceGenerator(
                new TemplateAdviceGenerator(settings),
                settings.Mode,
                nameof(TemplateAdviceGenerator),
                logger),
            _ => new LoggedAdviceGenerator(
                new LocalHeuristicAdviceGenerator(settings),
                settings.Mode,
                nameof(LocalHeuristicAdviceGenerator),
                logger)
        };
    }
}
