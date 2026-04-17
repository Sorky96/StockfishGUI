using System;
using System.Collections.Generic;
using System.Drawing;

namespace StockifhsGUI;

public sealed record TrackingProfile(
    nint WindowHandle,
    string WindowTitle,
    Rectangle BoardRegion,
    Rectangle MoveListRegion,
    bool WhiteAtBottom,
    bool BoardOnly);

public enum TrackerStatusKind
{
    Idle,
    Tracking,
    WaitingForStableFrame,
    Mismatch,
    Unsupported,
    Error,
    Stopped
}

public sealed record TrackerStatus(TrackerStatusKind Kind, string Message);

public sealed record TrackedPositionSnapshot(
    string Fen,
    string PlacementFen,
    double Confidence,
    DateTime SourceTimestamp,
    IReadOnlyList<string> Moves);

public sealed record TrackingUpdate(TrackerStatus Status, TrackedPositionSnapshot? Snapshot);

public sealed record MoveListRecognitionResult(
    string Fen,
    string PlacementFen,
    IReadOnlyList<string> Moves,
    double Confidence,
    DateTime SourceTimestamp);

public sealed record WindowCaptureInfo(nint Handle, string Title);
