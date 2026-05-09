namespace MoveMentorChess.Domain;

public sealed class OpeningNodeTag
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string Eco { get; set; } = string.Empty;
    public string OpeningName { get; set; } = string.Empty;
    public string VariationName { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
}
