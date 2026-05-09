using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public static class MoveQualityBucketExtensions
{
    public static bool IsProblem(this MoveQualityBucket quality) => quality >= MoveQualityBucket.Inaccuracy;

    public static bool IsPositiveOrNeutral(this MoveQualityBucket quality) => quality <= MoveQualityBucket.Good;
}
