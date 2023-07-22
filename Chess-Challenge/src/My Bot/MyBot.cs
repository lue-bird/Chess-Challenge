using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

/// <summary>
/// engine name: massage
/// 
/// ## vocab used
/// 
/// ### Piece "controls" a Square
/// That square is reachable with a move of that piece. Pins etc are not regarded.
/// In practice there is
/// - "cover": An empty square is controlled
/// - "attack": An opponent piece is controlled
/// - "defense": One of your own pieces is controlled
/// 
/// We call a directed movement axis that can be blocked by Pieces "ray".
/// Example: rook movement to the left.
/// Knights for example do not have rays because their movement can't be blocked/intercepted.
/// 
/// Whenever Pieces are in the way of movement to the desired square, we call the square in "x-ray"
/// 
/// ### advantage
/// evaluation of a thing, relative to its own color.
/// 
/// ### evaluation
/// positive = white is better, negative = black is better
/// </summary>
public class MyBot : IChessBot
{
    public Move Think(Board board, Timer timer)
    {
        Move chosenMove =
            board.IsWhiteToMove ?
                board.GetLegalMoves().MaxBy(move => MoveEvaluate(board, move))
            :
                board.GetLegalMoves().MinBy(move => MoveEvaluate(board, move));
        Console.WriteLine("evaluation guess: " + MoveEvaluate(board, chosenMove));
        return chosenMove;
    }

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
            PiecesIn(board, BoardSquares).Sum(piece => PieceIndependentEvaluate(board, piece))
                + BoardControlEvaluate(board);

    double PieceIndependentEvaluate(Board board, Piece piece) =>
        AsAdvantageIfWhite(piece.IsWhite, PieceAdvantage(piece));

    double BoardControlEvaluate(Board board) =>
        PiecesIn(board, BoardSquares)
            .SelectMany(piece => PieceControl(board, piece))
            .ToLookup(s => s.Key, s => s.Value)
            .Sum(s =>
            {
                Square square = s.Key;
                var whites =
                    s.Where(advantage => advantage >= 0);
                var blacks =
                    s.Where(advantage => advantage < 0);
                double defense =
                    board.GetPiece(square).IsWhite ? whites.Sum() : blacks.Sum();
                double attack =
                    board.GetPiece(square).IsWhite ? blacks.Sum() : whites.Sum();
                return
                board.GetPiece(s.Key).IsNull ?
                    defense * 0.1
                : attack == 0 ?
                    defense * 0.21
                : Math.Abs(defense) > Math.Abs(attack) ?
                    (defense + attack) * 0.3
                :
                    defense + attack;
            });

    Dictionary<Square, double> PieceControl(Board board, Piece piece) =>
        (piece.PieceType switch
        {
            PieceType.King =>
                // cause king defense is a bit risky
                WalkRays(board, SquaresAround(piece.Square).Select(square => new[] { square }), 0.72),
            PieceType.Queen =>
                // queen protection not as strong
                WalkRays(
                    board,
                    SquaresDiagonal(piece.Square)
                        .Concat(SquaresStraightLine(piece.Square)),
                    0.79
                ),
            PieceType.Knight =>
                // knight protection nice
                WalkRays(board, LSquaresFrom(piece.Square).Select(square => new[] { square }), 1.13),
            PieceType.Bishop =>
                WalkRays(board, SquaresDiagonal(piece.Square), 1.01),
            PieceType.Rook =>
                WalkRays(board, SquaresStraightLine(piece.Square), 0.9),
            PieceType.Pawn =>
                // passant ignored
                WalkRays(board, SquaresForwardLeftAndRight(piece).Select(square => new[] { square }), 1.31),
            PieceType.None =>
                new Dictionary<Square, double>()
        })
        .ToDictionary(s => s.Key, s => AsAdvantageIfWhite(piece.IsWhite, s.Value));

    double AsAdvantageIfWhite(bool isWhite, double advantage) =>
        isWhite ?
            advantage
        :
            -advantage;

    /// <summary>
    /// distribute control for all x-rayed squares of each ray
    /// </summary>
    /// <param name="board"></param>
    /// <param name="rays"></param>
    /// <param name="stability">
    /// How sure you are that this cover can hold up over time.
    /// For example a king covers with a low stability since king defense and attack is risky
    /// </param>
    /// <returns></returns>
    Dictionary<Square, double> WalkRays(Board board, IEnumerable<IEnumerable<Square>> rays, double stability) =>
        rays
            .SelectMany(ray => WalkRay(board, ray))
            .ToLookup(s => s.Key, s => s.Value)
            .ToDictionary(s => s.Key, s => s.Sum() * stability);

    IEnumerable<KeyValuePair<Square, double>> WalkRay(Board board, IEnumerable<Square> ray) =>
        // TODO attacking higher-advantage pieces → higher advantage
        // TODO defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
        // TODO distribute defense and attack for the x-rayed squares.
        ray
            .Aggregate(
                (0, Enumerable.Empty<KeyValuePair<Square, double>>()),
                (soFar, square) =>
                    (board.GetPiece(square).IsNull ? soFar.Item1 : soFar.Item1 + 1
                    , soFar.Item2.Append(new KeyValuePair<Square, double>(square, 1 / (1 + soFar.Item1)))
                    )
            )
            .Item2;

    double XRay(Board board, IEnumerable<Square> squares) =>
        Math.Pow(1.0 + (PiecesIn(board, squares)).Count(), 0.5);

    /// Convert relative coordinates from a given Square to an absolute square
    IEnumerable<Square> MovementSquaresFrom(Square from, IEnumerable<(int, int)> ray) =>
        ray
            .Select(movement =>
                (from.File + movement.Item1, from.Rank + movement.Item1)
            )
            .Where(square =>
                (square.Item1 >= 0 && square.Item1 <= 7)
                    && (square.Item2 >= 0 && square.Item2 <= 7)
            )
            .Select(square => new Square(square.Item1, square.Item2));

    IEnumerable<IEnumerable<Square>> SquaresDiagonal(Square from) =>
        new Func<int, (int, int)>[]
            { d => (d, d), d => (-d, d)
            , d => (d, -d), d => (-d, -d)
            }
            .Select(ray =>
                MovementSquaresFrom(from, Enumerable.Range(1, 7).Select(ray))
            );
    IEnumerable<IEnumerable<Square>> SquaresStraightLine(Square from) =>
        new Func<int, (int, int)>[]
            { d => (d, 0), d => (-d, 0)
            , d => (0, d), d => (0, -d)
            }
            .Select(ray =>
                MovementSquaresFrom(from, Enumerable.Range(1, 7).Select(ray))
            );

    IEnumerable<Square> SquaresAround(Square center) =>
        MovementSquaresFrom(center, new[]
            { (-1, -1), (-1, 0), (-1, 1)
            , (0, -1)          , (0, 1)
            , (1, -1) , (1, 0) , (1, 1)
            }
        );
    IEnumerable<Square> SquaresForwardLeftAndRight(Piece pawn) =>
        MovementSquaresFrom(pawn.Square,
            pawn.IsWhite ?
                new[] { (-1, 1), (1, 1) }
            :
                new[] { (-1, -1), (1, -1) }
        );
    IEnumerable<Square> LSquaresFrom(Square from) =>
        MovementSquaresFrom(from, new[]
            { (-1, -2), (-1, 2)
            , (1, -2), (1, 2)
            , (-2, -1), (-2, 1)
            , (2, -1), (2, 1)
            }
        );

    double PieceAdvantage(Piece piece) =>
         // piece.PieceType switch
         // {
         //     PieceType.None => 0,
         //     PieceType.Pawn => 1,
         //     PieceType.Knight => 2.9,
         //     PieceType.Bishop => 3.2,
         //     PieceType.Rook => 4.5,
         //     PieceType.Queen => 8.6,
         //     PieceType.King => 4,
         // }
         new[] { 0, 1, 2.9, 3.2, 4.5, 8.6, 4 }[(int)piece.PieceType];

    IEnumerable<Piece> PiecesIn(Board board, IEnumerable<Square> area) =>
        area
            .Select((square, i_) => board.GetPiece(square))
            .Where(piece => !piece.IsNull);


    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));
}
