namespace MoveMentorChess.Domain;

public sealed record OpeningReviewItem(
    OpeningBranchKey BranchKey,
    OpeningPositionKey PositionKey,
    DateTime? LastReviewedUtc,
    DateTime NextReviewUtc,
    double Ease,
    int CorrectStreak,
    int WrongStreak,
    int TotalAttempts);
