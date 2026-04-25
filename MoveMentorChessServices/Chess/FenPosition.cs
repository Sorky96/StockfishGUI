using System;
using System.Collections.Generic;

namespace MoveMentorChessServices;

public sealed class FenPosition
{
    private static readonly HashSet<char> ValidPieceCodes = new("PNBRQKpnbrqk".ToCharArray());

    public FenPosition(
        string?[,] board,
        bool whiteToMove,
        bool whiteKingMoved,
        bool blackKingMoved,
        bool whiteRookLeftMoved,
        bool whiteRookRightMoved,
        bool blackRookLeftMoved,
        bool blackRookRightMoved,
        string? enPassantTargetSquare,
        int halfmoveClock,
        int fullmoveNumber)
    {
        Board = CloneBoard(board);
        WhiteToMove = whiteToMove;
        WhiteKingMoved = whiteKingMoved;
        BlackKingMoved = blackKingMoved;
        WhiteRookLeftMoved = whiteRookLeftMoved;
        WhiteRookRightMoved = whiteRookRightMoved;
        BlackRookLeftMoved = blackRookLeftMoved;
        BlackRookRightMoved = blackRookRightMoved;
        EnPassantTargetSquare = enPassantTargetSquare;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public string?[,] Board { get; }
    public bool WhiteToMove { get; }
    public bool WhiteKingMoved { get; }
    public bool BlackKingMoved { get; }
    public bool WhiteRookLeftMoved { get; }
    public bool WhiteRookRightMoved { get; }
    public bool BlackRookLeftMoved { get; }
    public bool BlackRookRightMoved { get; }
    public string? EnPassantTargetSquare { get; }
    public int HalfmoveClock { get; }
    public int FullmoveNumber { get; }

    public string?[,] CloneBoard() => CloneBoard(Board);

    public string GetPlacementFen()
    {
        List<string> rows = new();
        for (int y = 0; y < 8; y++)
        {
            int empty = 0;
            string row = string.Empty;
            for (int x = 0; x < 8; x++)
            {
                string? piece = Board[x, y];
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

    public string GetFen()
    {
        string castlingRights = GetCastlingRights();
        string enPassant = string.IsNullOrWhiteSpace(EnPassantTargetSquare) ? "-" : EnPassantTargetSquare;
        return $"{GetPlacementFen()} {(WhiteToMove ? "w" : "b")} {castlingRights} {enPassant} {HalfmoveClock} {FullmoveNumber}";
    }

    public static FenPosition FromBoardState(
        string?[,] board,
        bool whiteToMove,
        bool whiteKingMoved,
        bool blackKingMoved,
        bool whiteRookLeftMoved,
        bool whiteRookRightMoved,
        bool blackRookLeftMoved,
        bool blackRookRightMoved,
        string? enPassantTargetSquare,
        int halfmoveClock,
        int fullmoveNumber)
    {
        return new FenPosition(
            board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            enPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber);
    }

    public static bool TryParse(string fen, out FenPosition? position, out string? error)
    {
        position = null;
        error = null;

        if (string.IsNullOrWhiteSpace(fen))
        {
            error = "FEN is empty.";
            return false;
        }

        string[] parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            error = "FEN must contain 6 space-separated fields.";
            return false;
        }

        if (!TryParseBoard(parts[0], out string?[,] board, out error))
        {
            return false;
        }

        if (parts[1] is not ("w" or "b"))
        {
            error = "Active color must be 'w' or 'b'.";
            return false;
        }

        bool whiteToMove = parts[1] == "w";

        if (!TryParseCastlingRights(parts[2], board, out bool whiteKingMoved, out bool blackKingMoved,
            out bool whiteRookLeftMoved, out bool whiteRookRightMoved, out bool blackRookLeftMoved,
            out bool blackRookRightMoved, out error))
        {
            return false;
        }

        if (!TryParseEnPassantTarget(parts[3], whiteToMove, out string? enPassantTargetSquare, out error))
        {
            return false;
        }

        if (!int.TryParse(parts[4], out int halfmoveClock) || halfmoveClock < 0)
        {
            error = "Halfmove clock must be a non-negative integer.";
            return false;
        }

        if (!int.TryParse(parts[5], out int fullmoveNumber) || fullmoveNumber < 1)
        {
            error = "Fullmove number must be an integer greater than 0.";
            return false;
        }

        position = new FenPosition(
            board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            enPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber);
        return true;
    }

    private string GetCastlingRights()
    {
        string rights = string.Empty;
        if (!WhiteKingMoved && !WhiteRookRightMoved && Board[4, 7] == "K" && Board[7, 7] == "R") rights += "K";
        if (!WhiteKingMoved && !WhiteRookLeftMoved && Board[4, 7] == "K" && Board[0, 7] == "R") rights += "Q";
        if (!BlackKingMoved && !BlackRookRightMoved && Board[4, 0] == "k" && Board[7, 0] == "r") rights += "k";
        if (!BlackKingMoved && !BlackRookLeftMoved && Board[4, 0] == "k" && Board[0, 0] == "r") rights += "q";
        return string.IsNullOrEmpty(rights) ? "-" : rights;
    }

    private static bool TryParseBoard(string placement, out string?[,] board, out string? error)
    {
        board = new string?[8, 8];
        error = null;

        string[] rows = placement.Split('/');
        if (rows.Length != 8)
        {
            error = "Placement field must contain 8 ranks.";
            return false;
        }

        int whiteKings = 0;
        int blackKings = 0;

        for (int y = 0; y < 8; y++)
        {
            int x = 0;
            foreach (char symbol in rows[y])
            {
                if (char.IsDigit(symbol))
                {
                    int empty = symbol - '0';
                    if (empty < 1 || empty > 8 || x + empty > 8)
                    {
                        error = $"Invalid empty-square run '{symbol}' in placement field.";
                        return false;
                    }

                    x += empty;
                    continue;
                }

                if (!ValidPieceCodes.Contains(symbol) || x >= 8)
                {
                    error = $"Invalid piece symbol '{symbol}' in placement field.";
                    return false;
                }

                if (symbol == 'K') whiteKings++;
                if (symbol == 'k') blackKings++;

                board[x, y] = symbol.ToString();
                x++;
            }

            if (x != 8)
            {
                error = $"Rank {8 - y} does not contain exactly 8 squares.";
                return false;
            }
        }

        if (whiteKings != 1 || blackKings != 1)
        {
            error = "FEN must contain exactly one white king and one black king.";
            return false;
        }

        return true;
    }

    private static bool TryParseCastlingRights(
        string rights,
        string?[,] board,
        out bool whiteKingMoved,
        out bool blackKingMoved,
        out bool whiteRookLeftMoved,
        out bool whiteRookRightMoved,
        out bool blackRookLeftMoved,
        out bool blackRookRightMoved,
        out string? error)
    {
        whiteKingMoved = true;
        blackKingMoved = true;
        whiteRookLeftMoved = true;
        whiteRookRightMoved = true;
        blackRookLeftMoved = true;
        blackRookRightMoved = true;
        error = null;

        if (rights == "-")
        {
            return true;
        }

        HashSet<char> seen = new();
        foreach (char symbol in rights)
        {
            if (!seen.Add(symbol))
            {
                error = $"Duplicate castling right '{symbol}'.";
                return false;
            }

            switch (symbol)
            {
                case 'K':
                    if (board[4, 7] != "K" || board[7, 7] != "R")
                    {
                        error = "Castling right 'K' is invalid for the current board.";
                        return false;
                    }

                    whiteKingMoved = false;
                    whiteRookRightMoved = false;
                    break;

                case 'Q':
                    if (board[4, 7] != "K" || board[0, 7] != "R")
                    {
                        error = "Castling right 'Q' is invalid for the current board.";
                        return false;
                    }

                    whiteKingMoved = false;
                    whiteRookLeftMoved = false;
                    break;

                case 'k':
                    if (board[4, 0] != "k" || board[7, 0] != "r")
                    {
                        error = "Castling right 'k' is invalid for the current board.";
                        return false;
                    }

                    blackKingMoved = false;
                    blackRookRightMoved = false;
                    break;

                case 'q':
                    if (board[4, 0] != "k" || board[0, 0] != "r")
                    {
                        error = "Castling right 'q' is invalid for the current board.";
                        return false;
                    }

                    blackKingMoved = false;
                    blackRookLeftMoved = false;
                    break;

                default:
                    error = $"Unsupported castling right '{symbol}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseEnPassantTarget(string field, bool whiteToMove, out string? enPassantTargetSquare, out string? error)
    {
        enPassantTargetSquare = null;
        error = null;

        if (field == "-")
        {
            return true;
        }

        if (field.Length != 2)
        {
            error = "En passant target must be '-' or a square like 'e3'.";
            return false;
        }

        char file = char.ToLowerInvariant(field[0]);
        char rank = field[1];
        if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
        {
            error = $"Invalid en passant target square '{field}'.";
            return false;
        }

        char expectedRank = whiteToMove ? '6' : '3';
        if (rank != expectedRank)
        {
            error = $"En passant target '{field}' is invalid for side to move '{(whiteToMove ? "w" : "b")}'.";
            return false;
        }

        enPassantTargetSquare = $"{file}{rank}";
        return true;
    }

    private static string?[,] CloneBoard(string?[,] board)
    {
        string?[,] clone = new string?[8, 8];
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                clone[x, y] = board[x, y];
            }
        }

        return clone;
    }
}
