namespace MoveMentorChess.Domain;

public sealed record OpeningGameMetadata(
    string Eco,
    string OpeningName,
    string VariationName)
{
    public static OpeningGameMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty);
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Eco)
        || !string.IsNullOrWhiteSpace(OpeningName)
        || !string.IsNullOrWhiteSpace(VariationName);
}
