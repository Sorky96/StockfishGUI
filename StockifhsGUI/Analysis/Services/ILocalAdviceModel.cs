namespace StockifhsGUI;

public interface ILocalAdviceModel
{
    string Name { get; }

    bool IsAvailable { get; }

    string? Generate(LocalModelAdviceRequest request);
}
