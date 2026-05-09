using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record MoveExplanation(string ShortText, string TrainingHint, string DetailedText = "");
