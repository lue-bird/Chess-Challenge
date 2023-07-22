using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();

        Move chosenMove =
            board.IsWhiteToMove ?
                moves.MaxBy(move => MoveEvaluate(board, move))
            :
                moves.MinBy(move => MoveEvaluate(board, move));
        Console.WriteLine("evaluation guess: " + MoveEvaluate(board, chosenMove));
        return chosenMove;
    }
    // evaluation:
    // positive = white is better, negative = black is better
    double MoveEvaluate(Board board, Move move)
    {
        board.MakeMove(move);
        double evalAfterMove = BoardEvaluate(board);
        board.UndoMove(move);
        return evalAfterMove;
    }

    double BoardEvaluate(Board board) =>
        board.IsInCheckmate() ?
            AsAdvantageIfWhite(board.IsWhiteToMove, double.NegativeInfinity)

        : board.IsDraw() ?
            0
        :
            // TODO past pawns
            BoardPieces(board).Sum(piece => SquareEvaluate(board, piece))
                + BoardSquares.Sum(square => ControlOver(board, square));

    double SquareEvaluate(Board board, Piece piece) =>
        AsAdvantageIfWhite(piece.IsWhite, PieceAdvantage(piece));

    // TODO: split protective power of piece
    // TODO attacking higher-advantage pieces → higher advantage
    // TODO defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
    double ControlOver(Board board, Square square)
    {
        IEnumerable<Piece> defenders =
            BoardPieces(board)
                .Where(piece => board.GetPiece(square).IsWhite == piece.IsWhite);
        double defense =
            AsAdvantageIfWhite(
                board.GetPiece(square).IsWhite,
                defenders
                    .Sum(piece => DefendsOrAttacksSquare(board, piece, square))
            );
        IEnumerable<Piece> attackers =
            BoardPieces(board)
                .Where(piece => board.GetPiece(square).IsWhite != piece.IsWhite);
        double attack =
            AsAdvantageIfWhite(
                !board.GetPiece(square).IsWhite,
                attackers
                    .Sum(piece => AsAdvantageIfWhite(piece.IsWhite, DefendsOrAttacksSquare(board, piece, square)))
            );

        return
            board.GetPiece(square).IsNull ?
                defense * 0.105
            : attack == 0 ?
                defense * 0.24
            : Math.Abs(defense) > Math.Abs(attack) ?
                (defense + attack) * 0.33
            :
                defense + attack;
    }

    double AsAdvantageIfWhite(bool isWhite, double advantage) =>
        isWhite ?
            advantage
        :
            -advantage;

    /// Piece with null type if no movement possible
    double DefendsOrAttacksSquare(Board board, Piece piece, Square endSquare) =>
        piece.PieceType switch
        {
            PieceType.King =>
                Math.Abs(endSquare.File - piece.Square.File) <= 1
                    && Math.Abs(endSquare.Rank - piece.Square.Rank) <= 1
                ?
                    // cause king defense is a bit risky
                    0.72
                :
                    0,
            PieceType.Queen =>
                AreInDiagonal(piece.Square, endSquare) || AreInStraightLine(piece.Square, endSquare) ?
                    // queen protection not as strong
                    0.79
                    * XRay(board,
                        SquaresInDiagonalBetween(piece.Square, endSquare)
                            .Concat(SquaresInStraightLineBetween(piece.Square, endSquare))
                        )
                :
                    0,
            PieceType.Knight =>
                AreL(piece.Square, endSquare) ?
                    // knight protection nice
                    1.13
                :
                    0,
            PieceType.Bishop =>
                AreInDiagonal(piece.Square, endSquare) ?
                    // bishop protection ok
                    1 * XRay(board, SquaresInDiagonalBetween(piece.Square, endSquare))
                :
                    0,
            PieceType.Rook =>
                AreInStraightLine(piece.Square, endSquare) ?
                    // rook protection not as strong
                    0.9 * XRay(board, SquaresInStraightLineBetween(piece.Square, endSquare))
                :
                    0,
            PieceType.Pawn =>
                // passant ignored for now
                endSquare.Rank == (piece.Square.Rank + 1)
                    && (Math.Abs(endSquare.File - piece.Square.File) == 1)
                ?
                    // pawn protection good protection
                    1.31
                :
                    0,
            PieceType.None => 0
        };

    double XRay(Board board, IEnumerable<Square> squares) =>
        Math.Pow(1.0 + (PiecesIn(board, squares)).Count(), 0.5);

    bool AreInDiagonal(Square a, Square b) =>
        (a.File + Math.Abs(b.Rank - a.Rank)
            == b.File
        )
            || (a.File - Math.Abs(b.Rank - a.Rank)
                    == b.File
                );

    IEnumerable<Square> SquaresInDiagonalBetween(Square a, Square b) =>
        RangeBetweenExclusive(0, Math.Abs(b.Rank - a.Rank))
            .Select(distance =>
                new Square(
                    a.File + distance * Math.Sign(b.File - a.File),
                    b.Rank + distance * Math.Sign(b.Rank - a.Rank)
                )
            )
            .Where(IsSquareInsideBoard);
    IEnumerable<Square> SquaresInStraightLineBetween(Square a, Square b) =>
        a.Rank == b.Rank ?
            RangeBetweenExclusive(0, Math.Abs(b.File - a.File))
                .Select(distance =>
                    new Square(
                        a.File + distance * Math.Sign(b.File - a.File),
                        b.Rank
                    )
                )
                .Where(IsSquareInsideBoard)
        :
            RangeBetweenExclusive(0, Math.Abs(b.Rank - a.Rank))
                .Select(distance =>
                    new Square(
                        a.File,
                        b.Rank + distance * Math.Sign(b.Rank - a.Rank)
                    )
                )
                .Where(IsSquareInsideBoard);
    bool IsSquareInsideBoard(Square square) =>
        (square.File >= 0 && square.File <= 7) && (square.Rank >= 0 && square.Rank <= 7);


    IEnumerable<int> RangeBetweenExclusive(int aExclusive, int bExclusive) =>
        bExclusive - aExclusive <= 2 ?
            Enumerable.Empty<int>()
        :
            Enumerable.Range(aExclusive + 1, (bExclusive - aExclusive) - 1);

    bool AreInStraightLine(Square a, Square b) =>
        (a.Rank == b.Rank) || (a.File == b.File);

    bool AreL(Square a, Square b) =>
        (Math.Abs(a.Rank - b.Rank) == 1 && Math.Abs(a.File - b.File) == 2)
            || (Math.Abs(a.Rank - b.Rank) == 2 && Math.Abs(a.File - b.File) == 1);

    double PieceAdvantage(Piece piece) =>
        piece.PieceType switch
        {
            PieceType.King => 4,
            PieceType.Queen => 8.6,
            PieceType.Knight => 2.9,
            PieceType.Bishop => 3.2,
            PieceType.Rook => 4.5,
            PieceType.Pawn => 1,
            PieceType.None => 0
        };

    IEnumerable<Piece> BoardPieces(Board board) =>
        PiecesIn(board, BoardSquares);

    IEnumerable<Piece> PiecesIn(Board board, IEnumerable<Square> area) =>
        area
            .Select((square, i_) => board.GetPiece(square))
            .Where(piece => !piece.IsNull);


    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));
}
