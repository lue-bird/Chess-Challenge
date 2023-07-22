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
    public double MoveEvaluate(Board board, Move move)
    {
        board.MakeMove(move);
        double evalAfterMove = BoardEvaluate(board);
        board.UndoMove(move);
        return evalAfterMove;
    }


    public double BoardEvaluate(Board board) =>
        board.IsInCheckmate() ?
            AsAdvantageIfWhite(board.IsWhiteToMove, double.NegativeInfinity)

        : board.IsDraw() ?
            0
        :
            BoardPieces(board).Sum(piece => SquareEvaluate(board, piece));

    public double SquareEvaluate(Board board, Piece piece) =>
        AsAdvantageIfWhite(piece.IsWhite, PieceAdvantage(piece));

    public double ControlOver(Board board, Square square) =>
        // TODO
        BoardPieces(board).Sum(boardPiece =>
            boardPiece.Square == square ?
                0
            :
                // TODO empty square * 0.14
                // TODO piece with same color * 0.5
                // TODO opponent piece * 1
                // TODO: split protective power of piece
                AsAdvantageIfWhite(boardPiece.IsWhite, DefendsOrAttacksSquare(board, boardPiece, square))

            );

    public double AsAdvantageIfWhite(bool isWhite, double advantage) =>
        isWhite ?
            advantage
        :
            -advantage;

    /// Piece with null type if no movement possible
    public double DefendsOrAttacksSquare(Board board, Piece piece, Square endSquare) =>
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
                // TODO divided by (pieces in between)^2
                AreInDiagonal(piece.Square, endSquare) || AreInStraightLine(piece.Square, endSquare) ?
                    // queen protection not as strong
                    0.79
                :
                    0,
            PieceType.Knight =>
                AreL(piece.Square, endSquare) ?
                    // knight protection nice
                    1.13
                :
                    0,
            PieceType.Bishop =>
                // TODO divided by (pieces in between)^2
                AreInDiagonal(piece.Square, endSquare) ?
                    // bishop protection ok
                    1
                :
                    0,
            PieceType.Rook =>
                // TODO divided by (pieces in between)^2
                AreInStraightLine(piece.Square, endSquare) ?
                    // rook protection not as strong
                    0.9
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

    public bool AreInDiagonal(Square a, Square b) =>
        (a.File + Math.Abs(b.Rank - a.Rank)
            == b.File
        )
            || (a.File - Math.Abs(b.Rank - a.Rank)
                    == b.File
                );

    public bool AreInStraightLine(Square a, Square b) =>
        (a.Rank == b.Rank) || (a.File == b.File);

    public bool AreL(Square a, Square b) =>
        (Math.Abs(a.Rank - b.Rank) == 1 && Math.Abs(a.File - b.File) == 2)
            || (Math.Abs(a.Rank - b.Rank) == 2 && Math.Abs(a.File - b.File) == 1);

    public double PieceAdvantage(Piece piece) =>
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
        BoardSquares.SelectMany<Square, Piece>((square, i_) =>
        {
            Piece atSquare = board.GetPiece(square);
            return
                atSquare.IsNull ?
                    Enumerable.Empty<Piece>()
                :
                    new[] { atSquare };
        });


    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));
}
