namespace MoveMentorChessServices;

public static class AdviceGeneratorFactory
{
    public static IAdviceGenerator CreateDefault()
    {
        return CreateInteractiveGenerator();
    }

    public static IAdviceGenerator CreateInteractiveGenerator()
    {
        AdviceGenerationSettings settings = AdviceGenerationSettingsResolver.ResolveFromEnvironment();
        ILocalAdviceModel? localModel = AdviceRuntimeCatalog.TryCreateConfiguredModel();
        IAdviceGenerationLogger logger = FileAdviceGenerationLogger.CreateDefault();

        return settings.Mode switch
        {
            AdviceGeneratorMode.Template => new LoggedAdviceGenerator(
                new TemplateAdviceGenerator(settings),
                settings.Mode,
                nameof(TemplateAdviceGenerator),
                logger),
            _ => new LoggedAdviceGenerator(
                new LocalModelAdviceGenerator(settings, localModel),
                settings.Mode,
                nameof(LocalModelAdviceGenerator),
                logger)
        };
    }

    public static IAdviceGenerator CreateBulkAnalysisGenerator()
    {
        AdviceGenerationSettings settings = AdviceGenerationSettingsResolver.ResolveFromEnvironment();
        ILocalAdviceModel? localModel = AdviceRuntimeCatalog.TryCreateConfiguredModel();
        IAdviceGenerationLogger logger = FileAdviceGenerationLogger.CreateDefault();

        // When a persistent llama-server is available, use LLM for bulk analysis too
        // because requests are fast (model is already loaded, ~0.5-2s per request).
        // Fall back to heuristics only when no server is available.
        bool hasServer = localModel is LlamaCppHttpAdviceModel;

        return settings.Mode switch
        {
            AdviceGeneratorMode.Template => new LoggedAdviceGenerator(
                new TemplateAdviceGenerator(settings),
                settings.Mode,
                nameof(TemplateAdviceGenerator),
                logger),
            _ when hasServer => new LoggedAdviceGenerator(
                new LocalModelAdviceGenerator(settings, localModel),
                settings.Mode,
                nameof(LocalModelAdviceGenerator),
                logger),
            _ => new LoggedAdviceGenerator(
                new LocalHeuristicAdviceGenerator(settings),
                settings.Mode,
                nameof(LocalHeuristicAdviceGenerator),
                logger)
        };
    }
}
