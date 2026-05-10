namespace MoveMentorChess.Domain;

public sealed record OpeningMoveIdea(
    string Move,
    IReadOnlyList<OpeningMoveIdeaTag> IdeaTags,
    string ShortExplanation);
