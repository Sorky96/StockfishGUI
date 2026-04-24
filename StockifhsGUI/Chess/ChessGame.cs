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
    private Point? enPassantTarget;
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
        enPassantTarget = null;
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

    public bool WhiteToMove => whiteToMove;
    public int FullmoveNumber => fullmoveNumber;
    public string? EnPassantTargetSquare => enPassantTarget is null ? null : ToSquare(enPassantTarget.Value);

    public void ApplyPgn(string pgn)
    {
        foreach (string san in ParsePgnMoves(pgn))
        {
            ApplySan(san);
        }
    }

    public void ApplySan(string san)
    {
        _ = ApplySanWithResult(san);
    }

    public AppliedMoveInfo ApplySanWithResult(string san)
    {
        string fenBefore = GetFen();
        string placementFenBefore = GetPlacementFen();
        bool whiteMoved = whiteToMove;
        int moveNumber = fullmoveNumber;
        string normalizedSan = SanNotation.NormalizeSan(san);
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);

        if (!TryResolveSan(san, legalMoves, out MoveCandidate move, out string? error))
        {
            throw new InvalidOperationException(error ?? $"Could not resolve SAN '{san}'.");
        }

        ExecuteMove(move);

        return CreateAppliedMoveInfo(move, san, normalizedSan, fenBefore, placementFenBefore, whiteMoved, moveNumber);
    }

    public IReadOnlyList<LegalMoveInfo> GetLegalMoves()
    {
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);
        return legalMoves
            .Select(move => new LegalMoveInfo(
                BuildUciMove(move),
                GenerateSan(move, legalMoves),
                ToSquare(move.From),
                ToSquare(move.To),
                move.Piece,
                move.PromotionPiece,
                move.IsCapture,
                move.IsEnPassant,
                move.Piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(move.To.X - move.From.X) == 2))
            .ToList();
    }

    public bool TryApplyUci(string uci, out AppliedMoveInfo? appliedMove, out string? error)
    {
        appliedMove = null;
        error = null;

        if (string.IsNullOrWhiteSpace(uci))
        {
            error = "Move UCI cannot be empty.";
            return false;
        }

        string fenBefore = GetFen();
        string placementFenBefore = GetPlacementFen();
        bool whiteMoved = whiteToMove;
        int moveNumber = fullmoveNumber;
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);
        List<MoveCandidate> matches = legalMoves
            .Where(move => string.Equals(BuildUciMove(move), uci, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            error = $"No legal move matches UCI '{uci}' in the current position.";
            return false;
        }

        if (matches.Count > 1)
        {
            error = $"UCI '{uci}' is ambiguous in the current position.";
            return false;
        }

        MoveCandidate move = matches[0];
        string san = GenerateSan(move, legalMoves);
        string normalizedSan = SanNotation.NormalizeSan(san);
        ExecuteMove(move);
        appliedMove = CreateAppliedMoveInfo(move, san, normalizedSan, fenBefore, placementFenBefore, whiteMoved, moveNumber);
        return true;
    }

    private AppliedMoveInfo CreateAppliedMoveInfo(
        MoveCandidate move,
        string san,
        string normalizedSan,
        string fenBefore,
        string placementFenBefore,
        bool whiteMoved,
        int moveNumber)
    {
        return new AppliedMoveInfo(
            san,
            normalizedSan,
            BuildUciMove(move),
            fenBefore,
            GetFen(),
            placementFenBefore,
            GetPlacementFen(),
            move.Piece,
            move.PromotionPiece,
            ToSquare(move.From),
            ToSquare(move.To),
            move.IsCapture,
            move.IsEnPassant,
            move.Piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(move.To.X - move.From.X) == 2,
            whiteMoved,
            moveNumber);
    }

    public bool TryLoadFen(string fen, out string? error)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out error) || position is null)
        {
            return false;
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
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
        enPassantTarget = TryParseSquare(position.EnPassantTargetSquare, out Point square) ? square : null;
        halfmoveClock = position.HalfmoveClock;
        fullmoveNumber = position.FullmoveNumber;
        return true;
    }

    public string GetFen()
    {
        return FenPosition.FromBoardState(
            board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            EnPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber).GetFen();
    }

    public string GetPlacementFen()
    {
        return FenPosition.FromBoardState(
            board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            EnPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber).GetPlacementFen();
    }

    public List<string> GetLegalSanMoves()
    {
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);
        return legalMoves
            .Select(move => GenerateSan(move, legalMoves))
            .OrderBy(move => move, StringComparer.Ordinal)
            .ToList();
    }

    public static List<string> ParsePgnMoves(string pgnText)
    {
        return SanNotation.ParsePgnMoves(pgnText);
    }

    private bool TryResolveSan(string san, out MoveCandidate candidate, out string? error)
    {
        return TryResolveSan(san, GetAllLegalMoves(whiteToMove), out candidate, out error);
    }

    private bool TryResolveSan(string san, List<MoveCandidate> legalMoves, out MoveCandidate candidate, out string? error)
    {
        string normalizedSan = SanNotation.NormalizeSan(san);
        string normalizedSanWithoutCheckSuffix = SanNotation.RemoveCheckSuffix(san);

        if (normalizedSanWithoutCheckSuffix == "O-O" || normalizedSanWithoutCheckSuffix == "O-O-O")
        {
            int rank = whiteToMove ? 7 : 0;
            int targetFile = normalizedSanWithoutCheckSuffix == "O-O" ? 6 : 2;

            foreach (MoveCandidate move in legalMoves)
            {
                if (move.Piece == (whiteToMove ? "K" : "k")
                    && move.From == new Point(4, rank)
                    && move.To == new Point(targetFile, rank))
                {
                    candidate = move;
                    error = null;
                    return true;
                }
            }

            candidate = default;
            error = "Castling is not legal in the current position.";
            return false;
        }

        Match destinationMatch = Regex.Match(normalizedSan, @"([a-h][1-8])", RegexOptions.IgnoreCase);
        if (!destinationMatch.Success)
        {
            candidate = default;
            error = $"Could not read target square from SAN '{san}'.";
            return false;
        }

        Point target = ParseSquare(destinationMatch.Groups[1].Value);
        string sanWithoutSuffix = Regex.Replace(normalizedSan, @"[+#]+$", string.Empty);
        string sanWithoutPromotion = Regex.Replace(sanWithoutSuffix, @"=([QRBN])", string.Empty);

        string promotionPiece = string.Empty;
        Match promotionMatch = Regex.Match(normalizedSan, @"=([QRBN])");
        if (promotionMatch.Success)
        {
            promotionPiece = whiteToMove ? promotionMatch.Groups[1].Value : promotionMatch.Groups[1].Value.ToLowerInvariant();
        }

        bool isCapture = sanWithoutSuffix.Contains('x');
        char firstChar = sanWithoutSuffix[0];
        bool hasExplicitPiecePrefix = "KQRBNkqrbn".Contains(firstChar);
        char pieceLetter = hasExplicitPiecePrefix ? char.ToUpperInvariant(firstChar) : 'P';
        string moverPiece = whiteToMove
            ? pieceLetter.ToString()
            : pieceLetter == 'P' ? "p" : pieceLetter.ToString().ToLowerInvariant();

        int destinationIndex = sanWithoutPromotion.IndexOf(destinationMatch.Groups[1].Value, StringComparison.Ordinal);
        string prefix = destinationIndex > 0 ? sanWithoutPromotion[..destinationIndex] : string.Empty;
        prefix = prefix.Replace("x", string.Empty, StringComparison.Ordinal);
        if (pieceLetter != 'P')
        {
            prefix = prefix.Replace(pieceLetter.ToString(), string.Empty, StringComparison.Ordinal);
        }

        char? disambiguationFile = prefix.FirstOrDefault(c => c is >= 'a' and <= 'h');
        char? disambiguationRank = prefix.FirstOrDefault(c => c is >= '1' and <= '8');

        List<MoveCandidate> matches = new();
        foreach (MoveCandidate move in legalMoves)
        {
            if (move.Piece != moverPiece || move.To != target || move.IsCapture != isCapture)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece != promotionPiece)
            {
                continue;
            }

            if (string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece is not null)
            {
                continue;
            }

            if (disambiguationFile.HasValue && move.From.X != disambiguationFile.Value - 'a')
            {
                continue;
            }

            if (disambiguationRank.HasValue && 8 - move.From.Y != disambiguationRank.Value - '0')
            {
                continue;
            }

            matches.Add(move);
        }

        if (matches.Count == 1)
        {
            candidate = matches[0];
            error = null;
            return true;
        }

        if (matches.Count == 0 && !hasExplicitPiecePrefix)
        {
            List<MoveCandidate> implicitPieceMatches = new();
            foreach (MoveCandidate move in legalMoves)
            {
                if (move.To != target || move.IsCapture != isCapture)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece != promotionPiece)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece is not null)
                {
                    continue;
                }

                if (disambiguationFile.HasValue && move.From.X != disambiguationFile.Value - 'a')
                {
                    continue;
                }

                if (disambiguationRank.HasValue && 8 - move.From.Y != disambiguationRank.Value - '0')
                {
                    continue;
                }

                implicitPieceMatches.Add(move);
            }

            if (implicitPieceMatches.Count == 1)
            {
                candidate = implicitPieceMatches[0];
                error = null;
                return true;
            }
        }

        if (TryResolveGeneratedSanFallback(san, normalizedSan, normalizedSanWithoutCheckSuffix, legalMoves, out candidate))
        {
            error = null;
            return true;
        }

        candidate = default;
        error = matches.Count == 0
            ? $"No legal move matches SAN '{san}' in the current position."
            : $"SAN '{san}' is ambiguous in the current position.";
        return false;
    }

    private bool TryResolveGeneratedSanFallback(
        string san,
        string normalizedSan,
        string normalizedSanWithoutCheckSuffix,
        List<MoveCandidate> legalMoves,
        out MoveCandidate candidate)
    {
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
            return true;
        }

        if (generatedMatches.Count == 0)
        {
            foreach (MoveCandidate move in legalMoves)
            {
                if (SanNotation.RemoveCheckSuffix(GenerateSan(move, legalMoves)) == normalizedSanWithoutCheckSuffix)
                {
                    generatedMatches.Add(move);
                }
            }

            if (generatedMatches.Count == 1)
            {
                candidate = generatedMatches[0];
                return true;
            }
        }

        candidate = default;
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
        string? capturedPiece = move.IsEnPassant
            ? board[move.To.X, move.To.Y + (IsPieceWhite(move.Piece) ? 1 : -1)]
            : board[move.To.X, move.To.Y];
        bool pawnMove = move.Piece.Equals("P", StringComparison.OrdinalIgnoreCase);

        ApplyMoveToBoard(move.From, move.To, move.Piece, move.PromotionPiece, move.IsEnPassant);
        UpdateCastlingRights(move.From, move.To, move.Piece, capturedPiece);
        enPassantTarget = GetEnPassantTargetAfterMove(move);

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

                bool isEnPassant = IsEnPassantCapture(from, to, piece);
                bool capture = !string.IsNullOrEmpty(board[to.X, to.Y]) || isEnPassant;
                if (NeedsPromotion(piece, to))
                {
                    foreach (string promotion in GetPromotionOptions(piece))
                    {
                        moves.Add(new MoveCandidate(from, to, piece, promotion, capture, isEnPassant));
                    }
                }
                else
                {
                    moves.Add(new MoveCandidate(from, to, piece, null, capture, isEnPassant));
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
                if (Math.Abs(dx) == 1 && dy == direction && IsEnPassantCapture(from, to, piece)) return true;
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
        ApplyMoveToBoard(from, to, piece, null, IsEnPassantCapture(from, to, piece));
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

    private void ApplyMoveToBoard(Point from, Point to, string piece, string? promotionPiece, bool isEnPassant)
    {
        board[from.X, from.Y] = null;

        if (isEnPassant)
        {
            int capturedPawnY = to.Y + (IsPieceWhite(piece) ? 1 : -1);
            board[to.X, capturedPawnY] = null;
        }

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

    private bool IsEnPassantCapture(Point from, Point to, string piece)
    {
        if (!piece.Equals("P", StringComparison.OrdinalIgnoreCase) || enPassantTarget is null || to != enPassantTarget.Value)
        {
            return false;
        }

        int direction = IsPieceWhite(piece) ? -1 : 1;
        if (Math.Abs(to.X - from.X) != 1 || to.Y - from.Y != direction || !string.IsNullOrEmpty(board[to.X, to.Y]))
        {
            return false;
        }

        int capturedPawnY = to.Y + (IsPieceWhite(piece) ? 1 : -1);
        string? capturedPawn = board[to.X, capturedPawnY];
        return capturedPawn == (IsPieceWhite(piece) ? "p" : "P");
    }

    private Point? GetEnPassantTargetAfterMove(MoveCandidate move)
    {
        if (!move.Piece.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int deltaY = move.To.Y - move.From.Y;
        if (Math.Abs(deltaY) != 2)
        {
            return null;
        }

        return new Point(move.From.X, move.From.Y + deltaY / 2);
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
    private static bool TryParseSquare(string? square, out Point point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(square) || square.Length != 2)
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

    private static string BuildUciMove(MoveCandidate move)
    {
        string uci = $"{ToSquare(move.From)}{ToSquare(move.To)}";
        if (!string.IsNullOrEmpty(move.PromotionPiece))
        {
            uci += move.PromotionPiece.ToLowerInvariant();
        }

        return uci;
    }

    private static Point ParseSquare(string square)
    {
        char file = char.ToLowerInvariant(square[0]);
        return new Point(file - 'a', 8 - (square[1] - '0'));
    }

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

        return new Snapshot(copy, whiteToMove, whiteKingMoved, blackKingMoved, whiteRookLeftMoved, whiteRookRightMoved, blackRookLeftMoved, blackRookRightMoved, enPassantTarget, halfmoveClock, fullmoveNumber);
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
        enPassantTarget = snapshot.EnPassantTarget;
        halfmoveClock = snapshot.HalfmoveClock;
        fullmoveNumber = snapshot.FullmoveNumber;
    }

    private readonly record struct MoveCandidate(Point From, Point To, string Piece, string? PromotionPiece, bool IsCapture, bool IsEnPassant);
    private readonly record struct Snapshot(
        string?[,] Board,
        bool WhiteToMove,
        bool WhiteKingMoved,
        bool BlackKingMoved,
        bool WhiteRookLeftMoved,
        bool WhiteRookRightMoved,
        bool BlackRookLeftMoved,
        bool BlackRookRightMoved,
        Point? EnPassantTarget,
        int HalfmoveClock,
        int FullmoveNumber);

    private readonly record struct Point(int X, int Y);
}

public sealed record AppliedMoveInfo(
    string San,
    string NormalizedSan,
    string Uci,
    string FenBefore,
    string FenAfter,
    string PlacementFenBefore,
    string PlacementFenAfter,
    string MovingPiece,
    string? PromotionPiece,
    string FromSquare,
    string ToSquare,
    bool IsCapture,
    bool IsEnPassant,
    bool IsCastle,
    bool WhiteMoved,
    int MoveNumber);

public sealed record LegalMoveInfo(
    string Uci,
    string San,
    string FromSquare,
    string ToSquare,
    string MovingPiece,
    string? PromotionPiece,
    bool IsCapture,
    bool IsEnPassant,
    bool IsCastle);
