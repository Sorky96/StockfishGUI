using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record MistakeTag(string Label, double Confidence, IReadOnlyList<string> Evidence);
