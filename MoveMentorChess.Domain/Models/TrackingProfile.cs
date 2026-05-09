using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

public sealed record TrackingProfile(
    nint WindowHandle,
    string WindowTitle,
    Rectangle BoardRegion,
    Rectangle MoveListRegion,
    bool WhiteAtBottom,
    bool BoardOnly);
