namespace MoveMentorChess.Domain;

public sealed record OpeningLineCatalogItem(
    OpeningKey OpeningKey,
    OpeningLineKey LineKey,
    RepertoireSide RepertoireSide,
    string Eco,
    string OpeningName,
    string VariationName,
    string DisplayName,
    OpeningPositionKey RootPositionKey,
    string RootFen,
    int BookGameCount,
    int BookBranchCount);
