using System.Collections.Generic;
using System.Drawing;

namespace StockifhsGUI;

internal interface IBoardInteractionHost
{
    int GridSize { get; }

    string?[,] Board { get; }

    Point? SelectedSquare { get; set; }

    IList<Point> AvailableMoves { get; }

    bool WhiteToMove { get; }

    bool IsPieceWhite(string piece);

    string GetCurrentFen();

    string ToUci(Point point);

    bool TryCreateGameFromCurrentPosition(out ChessGame? game, out string? error);

    bool TryExecuteMove(Point from, Point to, string piece, bool advanceImportedCursor);

    void UpdateSelectedPieceMoveOptions(string currentFen, Point selectedPoint, IReadOnlyList<LegalMoveInfo> movesForPiece);

    void ClearPieceMoveOptions();

    void InvalidateBoardSurface();
}
