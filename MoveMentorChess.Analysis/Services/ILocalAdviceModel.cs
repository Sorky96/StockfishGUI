namespace MoveMentorChess.Analysis;

public interface ILocalAdviceModel
{
    string Name { get; }

    bool IsAvailable { get; }

    string? Generate(LocalModelAdviceRequest request);
}
