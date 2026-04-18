using System.Collections.Generic;
using System.Drawing;

namespace StockifhsGUI;

internal interface IAnalysisNavigationHost
{
    ImportedGameSession ImportedSession { get; }

    IList<BoardArrow> AnalysisArrows { get; }

    Point? AnalysisTargetSquare { get; set; }

    void ReplayImportedMovesThrough(int targetIndex);

    void SetSuggestionText(string text);

    void InvalidateBoardSurface();
}
