namespace StockifhsGUI;

internal sealed record PieceMoveOption(
    string San,
    string Uci,
    EvaluatedScore? Score,
    int? DeltaCp,
    bool IsBestMove,
    string? ErrorText = null,
    bool IsPending = false)
{
    public static PieceMoveOption Pending(string san, string uci)
    {
        return new PieceMoveOption(san, uci, null, null, false, null, true);
    }
}

internal sealed record PieceMoveOptionListItem(PieceMoveOption Option, string Label)
{
    public override string ToString() => Label;
}

internal sealed record EvaluatedScore(int? Centipawns, int? MateIn);
