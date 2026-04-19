using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MaterialSkin.Controls;

namespace StockifhsGUI;

internal interface IBoardPresentationHost
{
    ChessBoardControl BoardSurface { get; }

    string?[,] Board { get; }

    IDictionary<string, Image> PieceImages { get; }

    IList<BoardArrow> BestMoveArrows { get; }

    IList<BoardArrow> AnalysisArrows { get; }

    IList<Point> AvailableMoves { get; }

    MaterialLabel SuggestionLabel { get; }

    MaterialLabel EvaluationLabel { get; }

    Panel EvaluationBarBackground { get; }

    Panel EvaluationBarFill { get; }

    StockfishEngine? Engine { get; }

    EvaluationSummary? CurrentEvaluation { get; set; }

    string MissingEngineMessage { get; }

    int BoardTileSize { get; }

    bool RotateBoard { get; }

    bool WhiteToMove { get; }

    Point? SelectedSquare { get; }

    Point? AnalysisTargetSquare { get; }

    Point? PreviewTargetSquare { get; }

    string GetCurrentFen();

    bool IsSuggestionLegal(string move);

    void UpdateExtendedControls();
}
