using System.Windows.Forms;

namespace StockifhsGUI;

public partial class MainForm
{
    private bool TryCreateGameFromCurrentPosition(out ChessGame? game, out string? error)
    {
        game = new ChessGame();
        if (game.TryLoadFen(GetCurrentFen(), out error))
        {
            return true;
        }

        game = null;
        return false;
    }

    private bool TryApplyFen(string fen, out string? error)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out error) || position is null)
        {
            return false;
        }

        ApplyPosition(position);
        error = null;
        return true;
    }

    private bool TryApplyMoveResult(AppliedMoveInfo appliedMove, bool advanceImportedCursor, out string? error)
    {
        error = null;

        undoStack.Push(CaptureCurrentState());
        if (!TryApplyFen(appliedMove.FenAfter, out error))
        {
            undoStack.Pop();
            return false;
        }

        analysisArrows.Clear();
        analysisTargetSquare = null;
        moveHistory.Add(appliedMove.Uci);
        if (advanceImportedCursor)
        {
            importedSession.Cursor++;
        }

        if (!suppressEngineRefresh)
        {
            RefreshEngineSuggestions();
            if (engine?.IsGameOver() == true)
            {
                MessageBox.Show("Game over. Stockfish reports no further legal continuation.", "Game Over", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        return true;
    }

    private void ApplyPosition(FenPosition position)
    {
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                board[x, y] = position.Board[x, y];
            }
        }

        whiteToMove = position.WhiteToMove;
        whiteKingMoved = position.WhiteKingMoved;
        blackKingMoved = position.BlackKingMoved;
        whiteRookLeftMoved = position.WhiteRookLeftMoved;
        whiteRookRightMoved = position.WhiteRookRightMoved;
        blackRookLeftMoved = position.BlackRookLeftMoved;
        blackRookRightMoved = position.BlackRookRightMoved;
        enPassantTargetSquare = position.EnPassantTargetSquare;
        halfmoveClock = position.HalfmoveClock;
        fullmoveNumber = position.FullmoveNumber;
    }
}
