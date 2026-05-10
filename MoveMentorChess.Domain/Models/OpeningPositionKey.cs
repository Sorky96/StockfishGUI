namespace MoveMentorChess.Domain;

public readonly record struct OpeningPositionKey(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}
