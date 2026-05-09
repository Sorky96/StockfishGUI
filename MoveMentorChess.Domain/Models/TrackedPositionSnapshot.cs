using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

public sealed record TrackedPositionSnapshot(
    string Fen,
    string PlacementFen,
    double Confidence,
    DateTime SourceTimestamp,
    IReadOnlyList<string> Moves);
