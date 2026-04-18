using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class MainForm
{
    private readonly PieceMoveOptionsCoordinator pieceMoveOptionsCoordinator = new();
    private Label? pieceMoveOptionsLabel;
    private ListBox? pieceMoveOptionsList;
    private int pieceMoveOptionsRequestId;
    private Point? pieceMoveOptionTargetSquare;

    private void InitializePieceMoveOptionsControls()
    {
        pieceMoveOptionsLabel = new Label
        {
            AutoSize = false,
            Text = "Selected piece: none"
        };
        Controls.Add(pieceMoveOptionsLabel);

        pieceMoveOptionsList = new ListBox
        {
            Font = new Font("Consolas", 9),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        pieceMoveOptionsList.SelectedIndexChanged += (_, _) => UpdatePieceMoveOptionPreview();
        Controls.Add(pieceMoveOptionsList);
        ClearPieceMoveOptions();
    }

    private void UpdateSelectedPieceMoveOptions(
        string currentFen,
        Point selectedPoint,
        IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        if (pieceMoveOptionsLabel is null || pieceMoveOptionsList is null)
        {
            return;
        }

        string fromSquare = ToUCI(selectedPoint);
        string pieceName = board[selectedPoint.X, selectedPoint.Y] ?? "?";
        pieceMoveOptionsLabel.Text = $"Selected piece: {pieceName} from {fromSquare} | legal moves: {movesForPiece.Count}";
        pieceMoveOptionTargetSquare = null;

        if (movesForPiece.Count == 0)
        {
            pieceMoveOptionsList.Items.Clear();
            pieceMoveOptionsList.Items.Add("No legal moves for this piece.");
            InvalidateBoardSurface();
            return;
        }

        ApplyPieceMoveOptions(
            pieceMoveOptionsCoordinator.CreatePendingOptions(movesForPiece),
            movesForPiece.Count,
            pieceName,
            fromSquare,
            appendCachedMarker: false);

        if (pieceMoveOptionsCoordinator.TryGetCachedOptions(currentFen, fromSquare, out IReadOnlyList<PieceMoveOption>? cachedOptions)
            && cachedOptions is not null)
        {
            ApplyPieceMoveOptions(cachedOptions, movesForPiece.Count, pieceName, fromSquare, appendCachedMarker: true);
            return;
        }

        if (engine is null)
        {
            ApplyPieceMoveOptions(
                pieceMoveOptionsCoordinator.CreateFallbackOptions(movesForPiece),
                movesForPiece.Count,
                pieceName,
                fromSquare,
                appendCachedMarker: false);
            return;
        }

        int requestId = ++pieceMoveOptionsRequestId;
        _ = AnalyzePieceMoveOptionsAsync(currentFen, fromSquare, pieceName, movesForPiece.ToList(), requestId);
    }

    private async Task AnalyzePieceMoveOptionsAsync(
        string currentFen,
        string fromSquare,
        string pieceName,
        IReadOnlyList<LegalMoveInfo> movesForPiece,
        int requestId)
    {
        IReadOnlyList<PieceMoveOption> analyzedOptions;
        try
        {
            if (engine is null)
            {
                analyzedOptions = pieceMoveOptionsCoordinator.CreateFallbackOptions(movesForPiece);
            }
            else
            {
                analyzedOptions = await pieceMoveOptionsCoordinator.AnalyzeAsync(currentFen, movesForPiece, engine);
            }
        }
        catch (Exception ex)
        {
            analyzedOptions =
            [
                new PieceMoveOption("analysis error", string.Empty, null, null, false, $"Could not analyze moves: {ex.Message}")
            ];
        }

        if (IsDisposed || requestId != pieceMoveOptionsRequestId)
        {
            return;
        }

        pieceMoveOptionsCoordinator.StoreOptions(currentFen, fromSquare, analyzedOptions);
        if (!IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || requestId != pieceMoveOptionsRequestId)
            {
                return;
            }

            ApplyPieceMoveOptions(analyzedOptions, movesForPiece.Count, pieceName, fromSquare, appendCachedMarker: false);
        }));
    }

    private void ApplyPieceMoveOptions(
        IReadOnlyList<PieceMoveOption> options,
        int moveCount,
        string pieceName,
        string fromSquare,
        bool appendCachedMarker)
    {
        if (pieceMoveOptionsLabel is null || pieceMoveOptionsList is null)
        {
            return;
        }

        string? selectedUci = pieceMoveOptionsList.SelectedItem is PieceMoveOptionListItem selectedItem
            ? selectedItem.Option.Uci
            : null;
        pieceMoveOptionsLabel.Text = pieceMoveOptionsCoordinator.BuildHeaderText(pieceName, fromSquare, moveCount, appendCachedMarker);
        pieceMoveOptionsList.Items.Clear();

        IReadOnlyList<PieceMoveOptionListItem> displayItems = pieceMoveOptionsCoordinator.BuildDisplayItems(options);
        foreach (PieceMoveOptionListItem item in displayItems)
        {
            pieceMoveOptionsList.Items.Add(item);
        }

        int selectedIndex = pieceMoveOptionsCoordinator.FindSelectionIndex(displayItems, selectedUci);
        if (selectedIndex >= 0)
        {
            pieceMoveOptionsList.SelectedIndex = selectedIndex;
        }

        UpdatePieceMoveOptionPreview();
    }

    private void ClearPieceMoveOptions()
    {
        pieceMoveOptionsRequestId++;
        pieceMoveOptionTargetSquare = null;
        if (pieceMoveOptionsLabel is not null)
        {
            pieceMoveOptionsLabel.Text = "Selected piece: none";
        }

        if (pieceMoveOptionsList is not null)
        {
            pieceMoveOptionsList.Items.Clear();
            pieceMoveOptionsList.Items.Add("Select a piece to inspect all legal moves.");
        }

        InvalidateBoardSurface();
    }

    private void UpdatePieceMoveOptionPreview()
    {
        if (pieceMoveOptionsList?.SelectedItem is PieceMoveOptionListItem optionItem
            && ChessMoveDisplayHelper.TryParseUciMove(optionItem.Option.Uci, out _, out Point to))
        {
            pieceMoveOptionTargetSquare = to;
        }
        else
        {
            pieceMoveOptionTargetSquare = null;
        }

        InvalidateBoardSurface();
    }
}
