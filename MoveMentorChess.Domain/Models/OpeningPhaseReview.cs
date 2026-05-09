using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record OpeningPhaseReview(
    OpeningBranchReference Branch,
    OpeningCriticalMoment? TheoryExit,
    OpeningCriticalMoment? FirstSignificantMistake);
