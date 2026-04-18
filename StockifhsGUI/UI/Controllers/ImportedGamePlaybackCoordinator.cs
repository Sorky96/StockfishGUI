using System;
using System.Collections.Generic;
using System.Linq;

namespace StockifhsGUI;

internal sealed class ImportedGamePlaybackCoordinator
{
    private readonly IImportedGamePlaybackHost host;

    public ImportedGamePlaybackCoordinator(IImportedGamePlaybackHost host)
    {
        this.host = host;
    }

    public ImportedGameSession Session => host.ImportedSession;

    public bool TryLoadImportedGame(ImportedGame parsedGame, out string? error)
    {
        ArgumentNullException.ThrowIfNull(parsedGame);

        error = null;
        try
        {
            GameReplayService replayService = new();
            IReadOnlyList<ReplayPly> replay = replayService.Replay(parsedGame);
            host.ResetBoardState();
            Session.LoadImportedGame(parsedGame, replay);
            host.PopulateImportedMovesList();
            host.RefreshEngineSuggestions();
            host.UpdateImportedControls();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryApplyNextImportedMove(out ImportedPlaybackError? error)
    {
        if (Session.Cursor >= Session.Moves.Count)
        {
            error = ImportedPlaybackError.CreateBeepOnly();
            return false;
        }

        return TryApplyImportedMove(Session.Cursor, out error);
    }

    public bool TryApplySelection(int targetIndex, bool resetToStart, out ImportedPlaybackError? error)
    {
        error = null;

        if (TryJumpToImportedMove(targetIndex, preserveUndoHistory: false))
        {
            return true;
        }

        if (resetToStart || targetIndex < Session.Cursor)
        {
            return TryReplayThrough(targetIndex, out error);
        }

        while (Session.Cursor <= targetIndex)
        {
            if (!TryApplyImportedMove(Session.Cursor, out error))
            {
                return false;
            }
        }

        return true;
    }

    public bool TryReplayThrough(int targetIndex, out ImportedPlaybackError? error)
    {
        error = null;

        if (targetIndex < 0 || targetIndex >= Session.Moves.Count)
        {
            error = ImportedPlaybackError.CreateBeepOnly();
            return false;
        }

        if (TryJumpToImportedMove(targetIndex, preserveUndoHistory: false))
        {
            return true;
        }

        host.ResetBoardState();
        Session.Cursor = 0;
        host.ClearSelection();

        host.SuppressEngineRefresh = true;
        try
        {
            for (int i = 0; i <= targetIndex; i++)
            {
                if (!TryApplyImportedMove(i, out error))
                {
                    return false;
                }
            }
        }
        finally
        {
            host.SuppressEngineRefresh = false;
        }

        host.RefreshEngineSuggestions();
        host.UpdateImportedControls();
        host.InvalidateBoardSurface();
        return true;
    }

    public bool TryJumpToImportedMove(int index, bool preserveUndoHistory)
    {
        if (index < 0 || index >= Session.Replay.Count)
        {
            return false;
        }

        if (preserveUndoHistory)
        {
            host.UndoStack.Push(host.CaptureCurrentState());
        }
        else
        {
            host.UndoStack.Clear();
        }

        ReplayPly replayPly = Session.Replay[index];
        if (!host.TryApplyFen(replayPly.FenAfter, out _))
        {
            if (preserveUndoHistory && host.UndoStack.Count > 0)
            {
                host.UndoStack.Pop();
            }

            return false;
        }

        host.ClearAnalysisFocus();
        host.MoveHistory.Clear();
        foreach (ReplayPly appliedReplayPly in Session.Replay.Take(index + 1))
        {
            host.MoveHistory.Add(appliedReplayPly.Uci);
        }

        Session.Cursor = index + 1;
        host.ClearSelection();

        if (!host.SuppressEngineRefresh)
        {
            host.RefreshEngineSuggestions();
        }

        host.UpdateImportedControls();
        host.InvalidateBoardSurface();
        return true;
    }

    private bool TryApplyImportedMove(int index, out ImportedPlaybackError? error)
    {
        error = null;

        if (index < 0 || index >= Session.Moves.Count)
        {
            error = ImportedPlaybackError.CreateBeepOnly();
            return false;
        }

        if (TryJumpToImportedMove(index, preserveUndoHistory: true))
        {
            host.ClearSelection();
            return true;
        }

        ImportedMoveListItem move = Session.Moves[index];
        if (!TryBuildImportedMoveResult(index, out AppliedMoveInfo? appliedMove, out string? buildError)
            || appliedMove is null)
        {
            error = new ImportedPlaybackError($"Move {move.DisplayText} could not be applied.\n{buildError}");
            return false;
        }

        if (!host.TryApplyMoveResult(appliedMove, advanceImportedCursor: true, out string? applyError))
        {
            error = new ImportedPlaybackError($"Move {move.DisplayText} could not be shown on the board.\n{applyError}");
            return false;
        }

        host.ClearSelection();
        return true;
    }

    private bool TryBuildImportedMoveResult(int index, out AppliedMoveInfo? appliedMove, out string? error)
    {
        appliedMove = null;
        error = null;

        try
        {
            ChessGame replayGame = new();
            for (int i = 0; i < index; i++)
            {
                replayGame.ApplySan(Session.Moves[i].San);
            }

            appliedMove = replayGame.ApplySanWithResult(Session.Moves[index].San);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

internal sealed record ImportedPlaybackError(string Message, string Caption = "Import PGN", bool BeepOnly = false)
{
    public static ImportedPlaybackError CreateBeepOnly()
    {
        return new ImportedPlaybackError(string.Empty, BeepOnly: true);
    }
}
