using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockifhsGUI.Avalonia.Views;

public partial class PromotionWindow : Window
{
    private readonly Dictionary<string, LegalMoveInfo> movesByPiece;

    public PromotionWindow()
    {
        movesByPiece = new Dictionary<string, LegalMoveInfo>(StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
    }

    public PromotionWindow(IReadOnlyList<LegalMoveInfo> moves)
    {
        movesByPiece = moves
            .Where(move => !string.IsNullOrWhiteSpace(move.PromotionPiece))
            .GroupBy(move => move.PromotionPiece!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        InitializeComponent();
        ConfigureButton(QueenButton, "Q", "Queen");
        ConfigureButton(RookButton, "R", "Rook");
        ConfigureButton(BishopButton, "B", "Bishop");
        ConfigureButton(KnightButton, "N", "Knight");
    }

    public LegalMoveInfo? SelectedMove { get; private set; }

    private void ConfigureButton(Button button, string pieceLetter, string label)
    {
        LegalMoveInfo? move = movesByPiece
            .Where(pair => string.Equals(pair.Key, pieceLetter, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        button.Content = label;
        button.IsEnabled = move is not null;
        button.Tag = move;
    }

    private void PromotionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LegalMoveInfo move })
        {
            return;
        }

        SelectedMove = move;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
