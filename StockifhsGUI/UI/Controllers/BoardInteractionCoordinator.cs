using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;

namespace StockifhsGUI;

internal sealed class BoardInteractionCoordinator
{
    private readonly IBoardInteractionHost host;

    public BoardInteractionCoordinator(IBoardInteractionHost host)
    {
        this.host = host;
    }

    public void HandleBoardSquareClick(Point square)
    {
        int x = square.X;
        int y = square.Y;

        if (!IsOnBoard(x, y))
        {
            return;
        }

        if (host.SelectedSquare is null)
        {
            TrySelectPiece(x, y);
            return;
        }

        Point from = host.SelectedSquare.Value;
        Point to = new(x, y);
        string? piece = host.Board[from.X, from.Y];

        if (string.IsNullOrEmpty(piece) || from == to)
        {
            ClearSelection();
            return;
        }

        if (!host.TryExecuteMove(from, to, piece, advanceImportedCursor: false))
        {
            SystemSounds.Beep.Play();
            ClearSelection();
            return;
        }

        ClearSelection();
    }

    public void ClearSelection()
    {
        host.SelectedSquare = null;
        host.AvailableMoves.Clear();
        host.ClearPieceMoveOptions();
        host.InvalidateBoardSurface();
    }

    private void TrySelectPiece(int x, int y)
    {
        string? piece = host.Board[x, y];
        if (string.IsNullOrEmpty(piece) || host.IsPieceWhite(piece) != host.WhiteToMove)
        {
            return;
        }

        if (!host.TryCreateGameFromCurrentPosition(out ChessGame? game, out _) || game is null)
        {
            return;
        }

        Point selectedPoint = new(x, y);
        host.SelectedSquare = selectedPoint;
        host.AvailableMoves.Clear();

        string fromSquare = host.ToUci(selectedPoint);
        List<LegalMoveInfo> movesForPiece = game.GetLegalMoves()
            .Where(move => move.FromSquare == fromSquare)
            .ToList();

        foreach (LegalMoveInfo move in movesForPiece)
        {
            if (ChessMoveDisplayHelper.TryParseUciSquare(move.ToSquare, out Point targetSquare)
                && !host.AvailableMoves.Contains(targetSquare))
            {
                host.AvailableMoves.Add(targetSquare);
            }
        }

        host.UpdateSelectedPieceMoveOptions(host.GetCurrentFen(), selectedPoint, movesForPiece);
        host.InvalidateBoardSurface();
    }

    private bool IsOnBoard(int x, int y)
    {
        return x >= 0 && x < host.GridSize && y >= 0 && y < host.GridSize;
    }
}
