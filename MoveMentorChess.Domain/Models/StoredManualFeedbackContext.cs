using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredManualFeedbackContext(
    AdviceFeedbackKind? ManualFeedbackKind = null,
    string? ManualCorrectedLabel = null,
    string? ManualComment = null,
    DateTime? ManualCorrectedUtc = null);
