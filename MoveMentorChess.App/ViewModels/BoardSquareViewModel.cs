using Avalonia.Media;

namespace MoveMentorChess.App.ViewModels;

public sealed class BoardSquareViewModel : ViewModelBase
{
    private string pieceText = string.Empty;
    private IBrush background = Brushes.Transparent;
    private IBrush foreground = Brushes.Black;

    public BoardSquareViewModel(string squareName, int boardX, int boardY)
    {
        SquareName = squareName;
        BoardX = boardX;
        BoardY = boardY;
    }

    public string SquareName { get; }

    public int BoardX { get; }

    public int BoardY { get; }

    public string PieceText
    {
        get => pieceText;
        set => SetProperty(ref pieceText, value);
    }

    public IBrush Background
    {
        get => background;
        set => SetProperty(ref background, value);
    }

    public IBrush Foreground
    {
        get => foreground;
        set => SetProperty(ref foreground, value);
    }
}
