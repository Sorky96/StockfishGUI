namespace MoveMentorChess.Domain;

public sealed class OpeningPositionNode
{
    public Guid Id { get; set; }
    public string PositionKey { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public string SideToMove { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public int DistinctGameCount { get; set; }
}
