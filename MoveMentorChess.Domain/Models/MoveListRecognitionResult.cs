using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

public sealed record MoveListRecognitionResult(
    string Fen,
    string PlacementFen,
    IReadOnlyList<string> Moves,
    double Confidence,
    DateTime SourceTimestamp);
