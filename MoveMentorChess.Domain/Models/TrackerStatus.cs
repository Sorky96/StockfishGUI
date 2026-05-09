using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChess.Domain;

public sealed record TrackerStatus(TrackerStatusKind Kind, string Message);
