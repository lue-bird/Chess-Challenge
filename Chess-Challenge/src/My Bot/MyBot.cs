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


    public double BoardEvaluate(Board board)
    {
        return
            board.IsInCheckmate() ?
                board.IsWhiteToMove ?
                    double.NegativeInfinity
                :
                    double.PositiveInfinity
            : board.IsDraw() ?
                0
            :
                BoardPieces(board).Sum(PieceEvaluate);
    }
    public double PieceEvaluate(Piece piece) =>
        piece.IsWhite ?
            WhitePieceEvaluate(piece)
        :
            -WhitePieceEvaluate(piece);

    public double WhitePieceEvaluate(Piece piece) =>
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
