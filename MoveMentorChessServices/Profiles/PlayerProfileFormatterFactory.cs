namespace MoveMentorChessServices;

public static class PlayerProfileFormatterFactory
{
    public static IPlayerProfileFormatter CreateDefault()
    {
        ILocalAdviceModel? localModel = AdviceRuntimeCatalog.TryCreateConfiguredModel();
        return new LocalModelPlayerProfileFormatter(localModel);
    }
}
