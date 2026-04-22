namespace StockifhsGUI.Avalonia.ViewModels;

public sealed class PieceMoveOptionViewModel
{
    public PieceMoveOptionViewModel(
        string san,
        string uci,
        string label,
        string toSquare,
        bool isBestMove)
    {
        San = san;
        Uci = uci;
        Label = label;
        ToSquare = toSquare;
        IsBestMove = isBestMove;
    }

    public string San { get; }

    public string Uci { get; }

    public string Label { get; }

    public string ToSquare { get; }

    public bool IsBestMove { get; }
}
