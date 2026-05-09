namespace MoveMentorChess.Domain;

public sealed class OpeningMoveEdge
{
    public Guid Id { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string MoveUci { get; set; } = string.Empty;
    public string MoveSan { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public int DistinctGameCount { get; set; }
    public bool IsMainMove { get; set; }
    public bool IsPlayableMove { get; set; }
    public int RankWithinPosition { get; set; }
}
