namespace MoveMentorChess.App.ViewModels;

public sealed class PieceMoveOptionViewModel
{
    public PieceMoveOptionViewModel(
        string san,
        string uci,
        string label,
        string toSquare,
        bool isBestMove,
        string evalText = "",
        string evalBrush = "#657386")
    {
        San = san;
        Uci = uci;
        Label = label;
        ToSquare = toSquare;
        IsBestMove = isBestMove;
        EvalText = evalText;
        EvalBrush = evalBrush;
    }

    public string San { get; }

    public string Uci { get; }

    public string Label { get; }

    public string ToSquare { get; }

    public bool IsBestMove { get; }

    public string EvalText { get; }

    public string EvalBrush { get; }
}
