﻿using System;
using static System.Math;
using static ChessChallenge.API.PieceType;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
using Ray = System.Collections.Generic.IEnumerable<(int, int)>;

/// name: massage
/// strategy: optimize piece activity
/// author: lue-bird on github
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
                    (board.GetPiece(square).IsWhite ? whites : blacks).Sum();
                double attack =
                    (board.GetPiece(square).IsWhite ? blacks : whites).Sum();
                return
                board.GetPiece(s.Key).IsNull ?
                    defense * 0.1
                : attack == 0 ?
                    defense * 0.21
                : Abs(defense) > Math.Abs(attack) ?
                    (defense + attack) * 0.3
                :
                    defense + attack;
            });

    Dictionary<Square, double> PieceControl(Board board, Piece piece) =>
        WalkRays(
            board,
            piece.Square,
            new[]
            {
                // None =>
                    // crazy how this is even possible in c#
                    new(),
                // Pawn =>
                    // passant ignored
                    (SquaresForwardLeftAndRight(piece).Select(EnumerableOne), 1.31),
                // Knight =>
                    // knight protection nice
                    (movementL.Select(EnumerableOne), 1.13),
                // Bishop =>
                    (movementDiagonal, 1.01),
                // Rook =>
                    (movementStraight, 0.9),
                // Queen =>
                    // queen protection not as strong
                    (movementDiagonal.Concat(movementStraight), 0.79),
                // King =>
                    // cause king defense is a bit risky
                    (movementNeighbors.Select(EnumerableOne), 0.72)
            }
                [(int)piece.PieceType]
        )
            .ToDictionary(s => s.Key, s => AsAdvantageIfWhite(piece.IsWhite, s.Value));

    double AsAdvantageIfWhite(bool isWhite, double advantage) =>
        isWhite ? advantage : -advantage;

    /// distribute control for all x-rayed squares of each ray
    /// first tuple item is the rays,
    /// second Tuple item is the stability.
    /// How sure you are that this cover can hold up over time.
    /// For example a king covers with a low stability since king defense and attack is risky
    Dictionary<Square, double> WalkRays(Board board, Square from, (IEnumerable<Ray>, double) config) =>
        config.Item1
            .SelectMany(ray => WalkRay(board, from, ray))
            .ToLookup(s => s.Key)
            .ToDictionary(s => s.Key, squares => squares.Sum(square => square.Value) * config.Item2);

    IEnumerable<KeyValuePair<Square, double>> WalkRay(Board board, Square from, Ray ray) =>
        // TODO attacking higher-advantage pieces → higher advantage
        // TODO defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
        // TODO distribute defense and attack for the x-rayed squares.
        // TODO if Piece with current color is attacked, then multiply min 1 by attacked piece advantage
        // btw I wish c# had a scan/foldMap
        MovementSquaresFrom(from, ray)
            .Aggregate(
                (0, Enumerable.Empty<KeyValuePair<Square, double>>()),
                (soFar, square) =>
                    (board.GetPiece(square).IsNull ? soFar.Item1 : soFar.Item1 + 1
                    , soFar.Item2.Append(new KeyValuePair<Square, double>(square, 1 / (1 + soFar.Item1)))
                    )
            )
            .Item2;

    double XRay(Board board, IEnumerable<Square> squares) =>
        Pow(1.0 + (PiecesIn(board, squares)).Count(), 0.5);

    /// Convert relative coordinates from a given Square to an absolute square
    IEnumerable<Square> MovementSquaresFrom(Square from, Ray ray) =>
        ray
            .Select(movement =>
                (from.File + movement.Item1, from.Rank + movement.Item1)
            )
            .Where(square =>
                (new[] { square.Item1, square.Item2 }.All(coordinate => coordinate is >= 0 and <= 7))
            )
            .Select(square => new Square(square.Item1, square.Item2));

    IEnumerable<Ray> movementDiagonal =
        new[]
            { (1, 1), (-1, 1)
            , (1, -1), (-1, -1)
            }
            .Select(ray =>
                Enumerable.Range(1, 7)
                    .Select(distance => (distance * ray.Item1, distance * ray.Item2))
            );
    IEnumerable<Ray> movementStraight =
        new[]
            { (1, 0), (-1, 0)
            , (0, 1), (0, -1)
            }
            .Select(ray =>
                Enumerable.Range(1, 7)
                    .Select(distance => (distance * ray.Item1, distance * ray.Item2))
            );

    Ray movementNeighbors =
        new[]
            { (-1, -1), (-1, 0), (-1, 1)
            , (0, -1)          , (0, 1)
            , (1, -1) , (1, 0) , (1, 1)
            };
    Ray SquaresForwardLeftAndRight(Piece pawn) =>
        new[] { -1, 1 }.Select(file => (file, pawn.IsWhite ? 1 : -1));

    Ray movementL =
        new[]
            { (-1, -2), (-1, 2)
            , (1, -2), (1, 2)
            , (-2, -1), (-2, 1)
            , (2, -1), (2, 1)
            };

    double PieceAdvantage(Piece piece) =>
        new[]
        {
            //     None => 
            0,
            //     Pawn => 
            1,
            //     Knight =>
            2.9,
            //     Bishop =>
            3.2,
            //     Rook =>
            4.5,
            //     Queen => 
            8.6,
            //     King => 
            4
        }
            [(int)piece.PieceType];

    IEnumerable<Piece> PiecesIn(Board board, IEnumerable<Square> area) =>
        area
            .Select(square => board.GetPiece(square))
            .Where(piece => !piece.IsNull);

    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));

    IEnumerable<A> EnumerableOne<A>(A onlyElement) =>
        new[] { onlyElement };
}
