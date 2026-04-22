namespace StockifhsGUI.Avalonia.ViewModels;

public sealed class ImportedMoveItemViewModel
{
    public ImportedMoveItemViewModel(int index, ReplayPly replayPly)
    {
        Index = index;
        ReplayPly = replayPly;
        DisplayText = replayPly.Side == PlayerSide.White
            ? $"{replayPly.MoveNumber,3}. {replayPly.San}"
            : $"{replayPly.MoveNumber,3}... {replayPly.San}";
    }

    public int Index { get; }

    public ReplayPly ReplayPly { get; }

    public string DisplayText { get; }

    public override string ToString() => DisplayText;
}
