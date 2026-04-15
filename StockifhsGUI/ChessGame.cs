using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StockifhsGUI;

public sealed class ChessGame
{
    private readonly string?[,] board = new string?[8, 8];

    private bool whiteToMove;
    private bool whiteKingMoved;
    private bool blackKingMoved;
    private bool whiteRookLeftMoved;
    private bool whiteRookRightMoved;
    private bool blackRookLeftMoved;
    private bool blackRookRightMoved;
    private int halfmoveClock;
    private int fullmoveNumber;

    public ChessGame()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Clear(board, 0, board.Length);
        whiteToMove = true;
        whiteKingMoved = false;
        blackKingMoved = false;
        whiteRookLeftMoved = false;
        whiteRookRightMoved = false;
        blackRookLeftMoved = false;
        blackRookRightMoved = false;
        halfmoveClock = 0;
        fullmoveNumber = 1;

        string[] rows = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR".Split('/');
        for (int y = 0; y < 8; y++)
        {
            int x = 0;
            foreach (char c in rows[y])
            {
                if (char.IsDigit(c))
                {
                    x += (int)char.GetNumericValue(c);
                    continue;
                }

                board[x, y] = c.ToString();
                x++;
            }
        }
    }

    public void ApplyPgn(string pgn)
    {
        foreach (string san in ParsePgnMoves(pgn))
        {
            ApplySan(san);
        }
    }

    public void ApplySan(string san)
    {
        if (!TryResolveSan(san, out MoveCandidate move, out string? error))
        {
            throw new InvalidOperationException(error ?? $"Could not resolve SAN '{san}'.");
        }

        ExecuteMove(move);
    }

    public string GetFen()
    {
        return $"{GetPlacementFen()} {(whiteToMove ? "w" : "b")} {GetCastlingFen()} - {halfmoveClock} {fullmoveNumber}";
    }

    public string GetPlacementFen()
    {
        List<string> rows = new();
        for (int y = 0; y < 8; y++)
        {
            int empty = 0;
            string row = string.Empty;
            for (int x = 0; x < 8; x++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece))
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    row += empty.ToString();
                    empty = 0;
                }

                row += piece;
            }

            if (empty > 0)
            {
                row += empty.ToString();
            }

            rows.Add(row);
        }

        return string.Join("/", rows);
    }

    public List<string> GetLegalSanMoves()
    {
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);
        return legalMoves
            .Select(move => GenerateSan(move, legalMoves))
            .OrderBy(move => move, StringComparer.Ordinal)
            .ToList();
    }

    private string GetCastlingFen()
    {
        string rights = string.Empty;
        if (!whiteKingMoved && !whiteRookRightMoved && board[4, 7] == "K" && board[7, 7] == "R") rights += "K";
        if (!whiteKingMoved && !whiteRookLeftMoved && board[4, 7] == "K" && board[0, 7] == "R") rights += "Q";
        if (!blackKingMoved && !blackRookRightMoved && board[4, 0] == "k" && board[7, 0] == "r") rights += "k";
        if (!blackKingMoved && !blackRookLeftMoved && board[4, 0] == "k" && board[0, 0] == "r") rights += "q";
        return string.IsNullOrEmpty(rights) ? "-" : rights;
    }

    public static List<string> ParsePgnMoves(string pgnText)
    {
        return SanNotation.ParsePgnMoves(pgnText);
    }

    private bool TryResolveSan(string san, out MoveCandidate candidate, out string? error)
    {
        string normalizedSan = SanNotation.NormalizeSan(san);
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);

        List<MoveCandidate> generatedMatches = new();
        foreach (MoveCandidate move in legalMoves)
        {
            if (SanNotation.NormalizeSan(GenerateSan(move, legalMoves)) == normalizedSan)
            {
                generatedMatches.Add(move);
            }
        }

        if (generatedMatches.Count == 1)
        {
            candidate = generatedMatches[0];
            error = null;
            return true;
        }

        candidate = default;
        error = $"No legal move matches SAN '{san}' in the current position.";
        return false;
    }

    private string GenerateSan(MoveCandidate move, List<MoveCandidate> legalMoves)
    {
        if (move.Piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(move.To.X - move.From.X) == 2)
        {
            return (move.To.X > move.From.X ? "O-O" : "O-O-O") + GetCheckSuffix(move);
        }

        bool isPawn = move.Piece.Equals("P", StringComparison.OrdinalIgnoreCase);
        string piecePrefix = isPawn ? string.Empty : move.Piece.ToUpperInvariant();
        string disambiguation = GetSanDisambiguation(move, legalMoves, isPawn);
        string capture = move.IsCapture ? "x" : string.Empty;
        string target = ToSquare(move.To);
        string promotion = move.PromotionPiece is null ? string.Empty : $"={move.PromotionPiece.ToUpperInvariant()}";
        return $"{piecePrefix}{disambiguation}{capture}{target}{promotion}{GetCheckSuffix(move)}";
    }

    private string GetSanDisambiguation(MoveCandidate move, List<MoveCandidate> legalMoves, bool isPawn)
    {
        if (isPawn)
        {
            return move.IsCapture ? ((char)('a' + move.From.X)).ToString() : string.Empty;
        }

        List<MoveCandidate> conflicts = legalMoves
            .Where(candidate => candidate.From != move.From && candidate.Piece == move.Piece && candidate.To == move.To)
            .ToList();

        if (conflicts.Count == 0)
        {
            return string.Empty;
        }

        bool fileUnique = conflicts.All(candidate => candidate.From.X != move.From.X);
        bool rankUnique = conflicts.All(candidate => candidate.From.Y != move.From.Y);
        char file = (char)('a' + move.From.X);
        char rank = (char)('8' - move.From.Y);

        if (fileUnique) return file.ToString();
        if (rankUnique) return rank.ToString();
        return $"{file}{rank}";
    }

    private string GetCheckSuffix(MoveCandidate move)
    {
        Snapshot snapshot = Capture();
        ExecuteMoveInternal(move);

        Point? opponentKing = FindKing(whiteToMove);
        bool check = opponentKing.HasValue && IsSquareAttacked(opponentKing.Value, !whiteToMove);
        bool hasMoves = GetAllLegalMoves(whiteToMove).Count > 0;

        Restore(snapshot);
        if (!check) return string.Empty;
        return hasMoves ? "+" : "#";
    }

    private void ExecuteMove(MoveCandidate move)
    {
        ExecuteMoveInternal(move);
    }

    private void ExecuteMoveInternal(MoveCandidate move)
    {
        string? capturedPiece = board[move.To.X, move.To.Y];
        bool pawnMove = move.Piece.Equals("P", StringComparison.OrdinalIgnoreCase);

        ApplyMoveToBoard(move.From, move.To, move.Piece, move.PromotionPiece);
        UpdateCastlingRights(move.From, move.To, move.Piece, capturedPiece);

        halfmoveClock = pawnMove || move.IsCapture ? 0 : halfmoveClock + 1;
        if (!whiteToMove)
        {
            fullmoveNumber++;
        }

        whiteToMove = !whiteToMove;
    }

    private List<MoveCandidate> GetAllLegalMoves(bool forWhite)
    {
        List<MoveCandidate> moves = new();
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece) || IsPieceWhite(piece) != forWhite)
                {
                    continue;
                }

                moves.AddRange(GetLegalMovesForPiece(new Point(x, y)));
            }
        }

        return moves;
    }

    private List<MoveCandidate> GetLegalMovesForPiece(Point from)
    {
        List<MoveCandidate> moves = new();
        string? piece = board[from.X, from.Y];
        if (string.IsNullOrEmpty(piece))
        {
            return moves;
        }

        for (int tx = 0; tx < 8; tx++)
        {
            for (int ty = 0; ty < 8; ty++)
            {
                Point to = new(tx, ty);
                if (!IsLegalMove(from, to, piece))
                {
                    continue;
                }

                bool capture = !string.IsNullOrEmpty(board[to.X, to.Y]);
                if (NeedsPromotion(piece, to))
                {
                    foreach (string promotion in GetPromotionOptions(piece))
                    {
                        moves.Add(new MoveCandidate(from, to, piece, promotion, capture));
                    }
                }
                else
                {
                    moves.Add(new MoveCandidate(from, to, piece, null, capture));
                }
            }
        }

        return moves;
    }

    private bool IsLegalMove(Point from, Point to, string piece)
    {
        if (from == to || !IsOnBoard(from) || !IsOnBoard(to))
        {
            return false;
        }

        if (board[from.X, from.Y] != piece)
        {
            return false;
        }

        if (!IsPseudoLegalMove(from, to, piece))
        {
            return false;
        }

        return !WouldLeaveKingInCheck(from, to, piece);
    }

    private bool IsPseudoLegalMove(Point from, Point to, string piece)
    {
        string? target = board[to.X, to.Y];
        if (!string.IsNullOrEmpty(target) && IsPieceWhite(target) == IsPieceWhite(piece))
        {
            return false;
        }

        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        switch (piece.ToLowerInvariant())
        {
            case "k":
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) return true;
                return dy == 0 && Math.Abs(dx) == 2 && CanCastle(from, to, IsPieceWhite(piece));
            case "p":
                int direction = IsPieceWhite(piece) ? -1 : 1;
                int startRow = IsPieceWhite(piece) ? 6 : 1;
                if (dx == 0 && dy == direction && string.IsNullOrEmpty(target)) return true;
                if (dx == 0 && dy == 2 * direction && from.Y == startRow && string.IsNullOrEmpty(target) && string.IsNullOrEmpty(board[from.X, from.Y + direction])) return true;
                if (Math.Abs(dx) == 1 && dy == direction && !string.IsNullOrEmpty(target) && IsPieceWhite(target) != IsPieceWhite(piece)) return true;
                return false;
            case "r":
                return (dx == 0 || dy == 0) && IsPathClear(from, to);
            case "n":
                return (Math.Abs(dx) == 2 && Math.Abs(dy) == 1) || (Math.Abs(dx) == 1 && Math.Abs(dy) == 2);
            case "b":
                return Math.Abs(dx) == Math.Abs(dy) && IsPathClear(from, to);
            case "q":
                return (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && IsPathClear(from, to);
            default:
                return false;
        }
    }

    private bool CanCastle(Point from, Point to, bool isWhite)
    {
        int homeRow = isWhite ? 7 : 0;
        if (from.Y != homeRow || from.X != 4 || to.Y != homeRow) return false;
        if (IsSquareAttacked(from, !isWhite)) return false;

        if (to.X == 6)
        {
            if (isWhite ? whiteKingMoved || whiteRookRightMoved : blackKingMoved || blackRookRightMoved) return false;
            if (board[7, homeRow] != (isWhite ? "R" : "r")) return false;
            if (!string.IsNullOrEmpty(board[5, homeRow]) || !string.IsNullOrEmpty(board[6, homeRow])) return false;
            return !IsSquareAttacked(new Point(5, homeRow), !isWhite) && !IsSquareAttacked(new Point(6, homeRow), !isWhite);
        }

        if (to.X == 2)
        {
            if (isWhite ? whiteKingMoved || whiteRookLeftMoved : blackKingMoved || blackRookLeftMoved) return false;
            if (board[0, homeRow] != (isWhite ? "R" : "r")) return false;
            if (!string.IsNullOrEmpty(board[1, homeRow]) || !string.IsNullOrEmpty(board[2, homeRow]) || !string.IsNullOrEmpty(board[3, homeRow])) return false;
            return !IsSquareAttacked(new Point(3, homeRow), !isWhite) && !IsSquareAttacked(new Point(2, homeRow), !isWhite);
        }

        return false;
    }

    private bool WouldLeaveKingInCheck(Point from, Point to, string piece)
    {
        Snapshot snapshot = Capture();
        ApplyMoveToBoard(from, to, piece, null);
        Point? king = FindKing(IsPieceWhite(piece));
        bool inCheck = king is null || IsSquareAttacked(king.Value, !IsPieceWhite(piece));
        Restore(snapshot);
        return inCheck;
    }

    private Point? FindKing(bool white)
    {
        string king = white ? "K" : "k";
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                if (board[x, y] == king)
                {
                    return new Point(x, y);
                }
            }
        }

        return null;
    }

    private bool IsSquareAttacked(Point square, bool byWhite)
    {
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece) || IsPieceWhite(piece) != byWhite)
                {
                    continue;
                }

                if (AttacksSquare(new Point(x, y), square, piece))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool AttacksSquare(Point from, Point to, string piece)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        return piece.ToLowerInvariant() switch
        {
            "p" => Math.Abs(dx) == 1 && dy == (IsPieceWhite(piece) ? -1 : 1),
            "n" => (Math.Abs(dx) == 2 && Math.Abs(dy) == 1) || (Math.Abs(dx) == 1 && Math.Abs(dy) == 2),
            "b" => Math.Abs(dx) == Math.Abs(dy) && IsPathClear(from, to),
            "r" => (dx == 0 || dy == 0) && IsPathClear(from, to),
            "q" => (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && IsPathClear(from, to),
            "k" => Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
            _ => false
        };
    }

    private void ApplyMoveToBoard(Point from, Point to, string piece, string? promotionPiece)
    {
        board[from.X, from.Y] = null;

        if (piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(to.X - from.X) == 2)
        {
            if (to.X > from.X)
            {
                board[5, from.Y] = board[7, from.Y];
                board[7, from.Y] = null;
            }
            else
            {
                board[3, from.Y] = board[0, from.Y];
                board[0, from.Y] = null;
            }
        }

        board[to.X, to.Y] = promotionPiece ?? piece;
    }

    private void UpdateCastlingRights(Point from, Point to, string movingPiece, string? capturedPiece)
    {
        switch (movingPiece)
        {
            case "K": whiteKingMoved = true; break;
            case "k": blackKingMoved = true; break;
            case "R":
                if (from == new Point(0, 7)) whiteRookLeftMoved = true;
                if (from == new Point(7, 7)) whiteRookRightMoved = true;
                break;
            case "r":
                if (from == new Point(0, 0)) blackRookLeftMoved = true;
                if (from == new Point(7, 0)) blackRookRightMoved = true;
                break;
        }

        switch (capturedPiece)
        {
            case "R":
                if (to == new Point(0, 7)) whiteRookLeftMoved = true;
                if (to == new Point(7, 7)) whiteRookRightMoved = true;
                break;
            case "r":
                if (to == new Point(0, 0)) blackRookLeftMoved = true;
                if (to == new Point(7, 0)) blackRookRightMoved = true;
                break;
        }
    }

    private bool IsPathClear(Point from, Point to)
    {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);
        int x = from.X + dx;
        int y = from.Y + dy;

        while (x != to.X || y != to.Y)
        {
            if (!string.IsNullOrEmpty(board[x, y])) return false;
            x += dx;
            y += dy;
        }

        return true;
    }

    private static bool NeedsPromotion(string piece, Point to)
    {
        return piece == "P" && to.Y == 0 || piece == "p" && to.Y == 7;
    }

    private static IEnumerable<string> GetPromotionOptions(string piece)
    {
        bool white = IsPieceWhite(piece);
        yield return white ? "Q" : "q";
        yield return white ? "R" : "r";
        yield return white ? "B" : "b";
        yield return white ? "N" : "n";
    }

    private static bool IsPieceWhite(string piece) => char.IsUpper(piece[0]);
    private static bool IsOnBoard(Point point) => point.X >= 0 && point.X < 8 && point.Y >= 0 && point.Y < 8;
    private static string ToSquare(Point point) => $"{(char)('a' + point.X)}{8 - point.Y}";

    private Snapshot Capture()
    {
        string?[,] copy = new string?[8, 8];
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                copy[x, y] = board[x, y];
            }
        }

        return new Snapshot(copy, whiteToMove, whiteKingMoved, blackKingMoved, whiteRookLeftMoved, whiteRookRightMoved, blackRookLeftMoved, blackRookRightMoved, halfmoveClock, fullmoveNumber);
    }

    private void Restore(Snapshot snapshot)
    {
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                board[x, y] = snapshot.Board[x, y];
            }
        }

        whiteToMove = snapshot.WhiteToMove;
        whiteKingMoved = snapshot.WhiteKingMoved;
        blackKingMoved = snapshot.BlackKingMoved;
        whiteRookLeftMoved = snapshot.WhiteRookLeftMoved;
        whiteRookRightMoved = snapshot.WhiteRookRightMoved;
        blackRookLeftMoved = snapshot.BlackRookLeftMoved;
        blackRookRightMoved = snapshot.BlackRookRightMoved;
        halfmoveClock = snapshot.HalfmoveClock;
        fullmoveNumber = snapshot.FullmoveNumber;
    }

    private readonly record struct MoveCandidate(Point From, Point To, string Piece, string? PromotionPiece, bool IsCapture);
    private readonly record struct Snapshot(
        string?[,] Board,
        bool WhiteToMove,
        bool WhiteKingMoved,
        bool BlackKingMoved,
        bool WhiteRookLeftMoved,
        bool WhiteRookRightMoved,
        bool BlackRookLeftMoved,
        bool BlackRookRightMoved,
        int HalfmoveClock,
        int FullmoveNumber);

    private readonly record struct Point(int X, int Y);
}
