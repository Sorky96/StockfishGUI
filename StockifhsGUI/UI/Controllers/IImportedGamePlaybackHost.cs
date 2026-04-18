using System.Collections.Generic;

namespace StockifhsGUI;

internal interface IImportedGamePlaybackHost
{
    ImportedGameSession ImportedSession { get; }

    Stack<GameStateSnapshot> UndoStack { get; }

    IList<string> MoveHistory { get; }

    bool SuppressEngineRefresh { get; set; }

    void ResetBoardState();

    void ClearSelection();

    void ClearAnalysisFocus();

    void RefreshEngineSuggestions();

    void UpdateImportedControls();

    void InvalidateBoardSurface();

    void PopulateImportedMovesList();

    GameStateSnapshot CaptureCurrentState();

    bool TryApplyFen(string fen, out string? error);

    bool TryApplyMoveResult(AppliedMoveInfo appliedMove, bool advanceImportedCursor, out string? error);
}
