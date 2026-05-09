namespace MoveMentorChessServices;

public interface IPlayerStrengthEstimator
{
    MoveMentorStrengthPoint Estimate(PlayerStrengthEstimateInput input);
}

public sealed record PlayerStrengthEstimateInput(
    string GameFingerprint,
    DateTime? GameDate,
    GameTimeControlCategory TimeControlCategory,
    int? PlayerRating,
    int? OpponentRating,
    double? ActualScore,
    double? ExpectedScore,
    IReadOnlyList<StoredMoveAnalysis> Moves,
    int SameTimeControlSampleSize);

public sealed class HeuristicPlayerStrengthEstimator : IPlayerStrengthEstimator
{
    private const int NeutralAnchor = 800;

    public MoveMentorStrengthPoint Estimate(PlayerStrengthEstimateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<StoredMoveAnalysis> moves = input.Moves.ToList();
        int moveCount = moves.Count;
        int anchor = input.PlayerRating ?? NeutralAnchor;
        int averageCpl = AverageCpl(moves);
        int medianCpl = MedianCpl(moves);
        double qualityScore = BuildQualityScore(moves);
        double resultScore = BuildResultScore(input.ActualScore, input.ExpectedScore);
        double moveCountWeight = Math.Clamp(moveCount / 25.0, 0.25, 1.0);

        int cplAdjustment = (int)Math.Round(Math.Clamp((65 - averageCpl) * 3.2, -260, 220) * moveCountWeight);
        int medianAdjustment = (int)Math.Round(Math.Clamp((55 - medianCpl) * 1.6, -120, 120) * moveCountWeight);
        int qualityAdjustment = (int)Math.Round(qualityScore * 180 * moveCountWeight);
        int resultAdjustment = (int)Math.Round(resultScore * 120);

        int estimated = Math.Clamp(anchor + cplAdjustment + medianAdjustment + qualityAdjustment + resultAdjustment, 100, 3200);
        MoveMentorStrengthConfidence confidence = BuildConfidence(input.SameTimeControlSampleSize, moveCount);
        int range = confidence switch
        {
            MoveMentorStrengthConfidence.High => 70,
            MoveMentorStrengthConfidence.Medium => 120,
            _ => 190
        };

        return new MoveMentorStrengthPoint(
            input.GameFingerprint,
            input.GameDate,
            input.TimeControlCategory,
            estimated,
            Math.Max(100, estimated - range),
            Math.Min(3200, estimated + range),
            confidence,
            MoveMentorStrengthEstimatorKind.HeuristicV1,
            BuildReasonSummary(averageCpl, moveCount, input.ActualScore, input.ExpectedScore));
    }

    private static int AverageCpl(IReadOnlyList<StoredMoveAnalysis> moves)
    {
        List<int> values = moves
            .Select(move => move.CentipawnLoss)
            .Where(value => value.HasValue)
            .Select(value => Math.Max(0, value!.Value))
            .ToList();
        return values.Count == 0 ? 100 : (int)Math.Round(values.Average());
    }

    private static int MedianCpl(IReadOnlyList<StoredMoveAnalysis> moves)
    {
        List<int> values = moves
            .Select(move => move.CentipawnLoss)
            .Where(value => value.HasValue)
            .Select(value => Math.Max(0, value!.Value))
            .Order()
            .ToList();
        if (values.Count == 0)
        {
            return 100;
        }

        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (int)Math.Round((values[middle - 1] + values[middle]) / 2.0);
    }

    private static double BuildQualityScore(IReadOnlyList<StoredMoveAnalysis> moves)
    {
        if (moves.Count == 0)
        {
            return -0.2;
        }

        double positive = moves.Count(move => move.Quality <= MoveQualityBucket.Good) / (double)moves.Count;
        double excellent = moves.Count(move => move.Quality <= MoveQualityBucket.Excellent) / (double)moves.Count;
        double severe = moves.Count(move => move.Quality is MoveQualityBucket.Mistake or MoveQualityBucket.Blunder) / (double)moves.Count;
        double blunders = moves.Count(move => move.Quality == MoveQualityBucket.Blunder) / (double)moves.Count;
        double brilliantGreat = moves.Count(move => move.Quality is MoveQualityBucket.Brilliant or MoveQualityBucket.Great) / (double)moves.Count;

        return Math.Clamp((positive - 0.58) + (excellent - 0.33) * 0.7 + brilliantGreat * 0.6 - severe * 1.2 - blunders * 1.5, -1.0, 1.0);
    }

    private static double BuildResultScore(double? actualScore, double? expectedScore)
    {
        if (!actualScore.HasValue || !expectedScore.HasValue)
        {
            return 0;
        }

        return Math.Clamp(actualScore.Value - expectedScore.Value, -1.0, 1.0);
    }

    private static MoveMentorStrengthConfidence BuildConfidence(int sampleSize, int moveCount)
    {
        if (sampleSize >= 20 && moveCount >= 15)
        {
            return MoveMentorStrengthConfidence.High;
        }

        if (sampleSize >= 5 && moveCount >= 10)
        {
            return MoveMentorStrengthConfidence.Medium;
        }

        return MoveMentorStrengthConfidence.Low;
    }

    private static string BuildReasonSummary(int averageCpl, int moveCount, double? actualScore, double? expectedScore)
    {
        string result = actualScore.HasValue && expectedScore.HasValue
            ? $"result delta {(actualScore.Value - expectedScore.Value):+0.00;-0.00;0.00}"
            : "no rating-result calibration";
        return $"HeuristicV1 from {moveCount} analyzed moves, average CPL {averageCpl}, {result}.";
    }
}

public sealed class ProfileMlPlayerStrengthEstimator : IPlayerStrengthEstimator
{
    public MoveMentorStrengthPoint Estimate(PlayerStrengthEstimateInput input)
    {
        throw new NotSupportedException("Per-profile ML strength estimation is planned but not implemented yet.");
    }
}
