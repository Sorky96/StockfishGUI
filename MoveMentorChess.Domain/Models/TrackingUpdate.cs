using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

public sealed record TrackingUpdate(TrackerStatus Status, TrackedPositionSnapshot? Snapshot);
