using System;
using System.Drawing;

namespace StockifhsGUI;

internal static class ChessMoveDisplayHelper
{
    public static bool TryParseUciMove(string? uciMove, out Point from, out Point to)
    {
        from = default;
        to = default;

        if (string.IsNullOrWhiteSpace(uciMove) || uciMove.Length < 4)
        {
            return false;
        }

        if (!TryParseUciSquare(uciMove[..2], out from) || !TryParseUciSquare(uciMove.Substring(2, 2), out to))
        {
            return false;
        }

        return true;
    }

    public static bool TryParseUciSquare(string square, out Point point)
    {
        point = default;
        if (square.Length != 2)
        {
            return false;
        }

        char file = char.ToLowerInvariant(square[0]);
        char rank = square[1];
        if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
        {
            return false;
        }

        point = new Point(file - 'a', 8 - (rank - '0'));
        return true;
    }

    public static string FormatBestMoveLabel(MoveAnalysisResult moveAnalysis)
    {
        string? bestMoveUci = moveAnalysis.BeforeAnalysis.BestMoveUci;
        if (string.IsNullOrWhiteSpace(bestMoveUci))
        {
            return "(unknown)";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(moveAnalysis.Replay.FenBefore, out _)
            || !game.TryApplyUci(bestMoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return bestMoveUci;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    public static string FormatSanAndUci(string san, string uci)
    {
        return string.Equals(san, uci, StringComparison.OrdinalIgnoreCase)
            ? san
            : $"{san} ({uci})";
    }
}
