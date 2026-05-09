using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

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
