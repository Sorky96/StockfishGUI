namespace MoveMentorChess.Domain;

public sealed record CanonicalLineResolutionResult(
    OpeningPositionIdentity Identity,
    OpeningLineKey? CanonicalLineKey,
    bool IsKnownTheoryPosition,
    bool ReachedByTransposition,
    string Summary);
