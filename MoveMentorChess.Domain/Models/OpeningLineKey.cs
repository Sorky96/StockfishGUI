namespace MoveMentorChess.Domain;

public readonly record struct OpeningLineKey(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}
