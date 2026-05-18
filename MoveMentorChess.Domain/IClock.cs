namespace MoveMentorChess.Domain;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTime UtcNow => DateTime.UtcNow;
}
