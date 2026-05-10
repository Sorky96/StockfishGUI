namespace MoveMentorChess.Domain;

public readonly record struct OpeningBranchKey(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}
