﻿using System;
using static System.Math;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
// very sorry for using an alias because it's just token optimization,
// but used for different things:
//     - direction options
//     - relative positions in a ray, increasing in distance
using Movement = System.Collections.Generic.IEnumerable<(int, int)>;

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
        // TODO search
        //     - search most forcing moves first
        //     - breath first?
        //     - alpha-beta?
        Move chosenMove =
            board.IsWhiteToMove ?
                board.GetLegalMoves().MaxBy(move => AfterMoveEvaluate(board, move))
            :
                board.GetLegalMoves().MinBy(move => AfterMoveEvaluate(board, move));
        Console.WriteLine("evaluation guess: " + AfterMoveEvaluate(board, chosenMove));
        return chosenMove;
    }

    double AfterMoveEvaluate(Board board, Move move)
    {
        board.MakeMove(move);
        double evalAfterMove = BoardEvaluate(board);
        board.UndoMove(move);
        return evalAfterMove;
    }

    double BoardEvaluate(Board board) =>
        board.IsInCheckmate() ?
            AsAdvantageForWhiteIf(board.IsWhiteToMove, double.NegativeInfinity)

        : board.IsDraw() ?
            0
        :
            // TODO past pawns
            // TODO prefer heavies and minors near opponent king
            // TODO encourage covering squares near king
            PiecesIn(board, BoardSquares)
                .Sum(piece => PieceIndependentEvaluate(board, piece))
                + BoardControlEvaluate(board);

    double PieceIndependentEvaluate(Board board, Piece piece) =>
        AsAdvantageForWhiteIf(piece.IsWhite, PieceAdvantage(piece));

    double BoardControlEvaluate(Board board)
    {
        var controlByPiece =
            PiecesIn(board, BoardSquares)
                .ToDictionary(
                    piece => piece,
                    piece => PieceControlAdvantage(board, piece)
                );
        return
        controlByPiece
            .SelectMany(piece =>
                piece.Value
                    .Select(advantageBySquare =>
                        (advantageBySquare.Key, (piece.Key, advantageBySquare.Value))
                    )
            )
            .ToLookup(s => s.Item1, s => s.Item2)
            .Sum(squareControl =>
            {
                // TODO attacking higher-advantage pieces → higher advantage
                // TODO defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
                // TODO distribute defense and attack for the x-rayed squares.
                // TODO if a piece is attacked more often than defended, don't distribute attack control and cover from it
                Piece pieceAtSquare =
                    board.GetPiece(squareControl.Key);
                bool pieceAtSquareIsWhite =
                    pieceAtSquare.IsWhite;
                var defenders =
                    squareControl.Where(control => control.Item1.IsWhite == pieceAtSquareIsWhite);
                var attackers =
                    squareControl.Where(control => control.Item1.IsWhite != pieceAtSquareIsWhite); //.Except(defenders);
                double defense =
                    defenders.Sum(s => s.Item2);
                double attack =
                    attackers.Sum(s => s.Item2);
                double defenseMinusAttack = defense - attack;
                // if (defenseMinusAttack < -0.5 && !pieceAtSquare.IsNull && pieceAtSquareIsWhite != board.IsWhiteToMove)
                // {
                //     Console.WriteLine("hanging piece " + pieceAtSquare + " attacked by " + String.Join(", ", attackers) + " defended by " + String.Join(", ", defenders));
                // }
                return
                AsAdvantageForWhiteIf(
                    pieceAtSquareIsWhite,
                    pieceAtSquare.IsNull ?
                        // we do kinda care for covered squares
                        defenseMinusAttack * 0.13
                    : attack == 0 ?
                        // we care just a little about defending non-attacked pieces
                        defenseMinusAttack * 0.16
                    : defenseMinusAttack >= -0.2 ?
                        // maybe increase factor based on how close defenseRemaining is to 0
                        defenseMinusAttack * 0.34
                    : // defenseMinusAttack < -0.2
                      // in other words attack >= defense
                        pieceAtSquareIsWhite != board.IsWhiteToMove ?
                            // TODO increase piece advantage by covered fields
                            Max(defenseMinusAttack, -1)
                                * PieceAdvantage(pieceAtSquare)
                                // TODO likewise, value covering and non-attacks less
                                - controlByPiece.GetValueOrDefault(pieceAtSquare).Values.Sum()
                        : // opponent piece is attacked
                            defenseMinusAttack
                );
            });
    }

    /// note that the resulting double values are positive and don't take Piece color into account
    Dictionary<Square, double> PieceControlAdvantage(Board board, Piece piece)
    {
        /// stability:
        /// How sure you are that this cover can hold up over time.
        /// For example a king covers with a low stability since king defense and attack is risky
        var (rays, stability) =
            new[]
            {
                // None =>
                    // crazy how this is even possible in c#
                    new(),
                // Pawn =>
                    // passant ignored
                    (Signs.Select(file => EnumerableOne((file, piece.IsWhite ? 1 : -1))), 1.31),
                // Knight =>
                    (
                    // new[]
                    //     {     (-1, 2),       (1, 2)
                    //     , (-2, 1),              (2, 1)
                    // 
                    //     , (-2, -1),             (2, -1)
                    //     ,     (-1, -2),     (1, -2)
                    //     }
                    movementDirectionsDiagonal
                        .SelectMany(movement =>
                        {
                            var (file, rank) = movement;
                            return
                                new[]
                                { (file, rank * 2)
                                , (2 * file, rank)
                                };
                        })
                        .Select(EnumerableOne)
                    ,
                    // knight protection nice because it can't be intercepted
                    1.13
                    ),
                // Bishop =>
                    (AlongDirections(movementDirectionsDiagonal), 1.01),
                // Rook =>
                    (AlongDirections(movementDirectionsStraight), 0.9),
                // Queen =>
                    // queen protection not as strong because it can be chased away more easily
                    (AlongDirections(movementNeighbors), 0.79),
                // King =>
                    // cause king defense is risky
                    (movementNeighbors.Select(EnumerableOne), 0.72)
            }
                [(int)piece.PieceType];
        return
        rays
            .SelectMany(ray =>
                // control along ray
                // btw I wish c# had a scan/foldMap/mapAccum
                MovementSquaresFrom(piece.Square, ray)
                    .Aggregate(
                        // ( current index: int
                        // , how blocking pieces in the way are: double
                        // , resulting control by square in the ray: IEnumerable<(Square, double)>
                        // )
                        (1, 0.0, Enumerable.Empty<(Square, double)>()),
                        (soFar, square) =>
                            (soFar.Item1 + 1
                            , soFar.Item2
                                // increase depending on piece immobility
                                + new[]
                                    {
                                        // None =>
                                        0,
                                        // Pawn =>
                                        1.9,
                                        // Knight =>
                                        0.6,
                                        // Bishop =>
                                        0.9,
                                        // Rook =>
                                        1,
                                        // Queen =>
                                        0.8,
                                        // King =>
                                        0.88
                                    }
                                    [(int)board.GetPiece(square).PieceType]
                            , soFar.Item3.Append(
                                (square
                                , // decrease control by count of blocking pieces
                                  Pow(1 + soFar.Item2 * 2, -1.2)
                                  // decrease stability by square-distance from origin → interception tactics
                                  * Pow(1 + soFar.Item1, -0.45)
                                )
                              )
                            )
                    )
                    .Item3
            )
            .ToDictionary(s => s.Item1, s => s.Item2 * stability);
    }

    double AsAdvantageForWhiteIf(bool isWhite, double advantage) =>
        isWhite ? advantage : -advantage;

    /// Convert relative coordinates from a given Square to an absolute square
    IEnumerable<Square> MovementSquaresFrom(Square from, Movement ray) =>
        // saves literally 1 token compared to
        // ray
        //     .Select(movement =>
        //         (from.File + movement.Item1, from.Rank + movement.Item1)
        //     )
        //     .Where(square =>
        //         (new[] { square.Item1, square.Item2 }.All(coordinate => coordinate is >= 0 and <= 7))
        //     )
        //     .Select(square => new Square(square.Item1, square.Item2));
        ray
            .SelectMany(movement =>
            {
                int file = from.File + movement.Item1;
                int rank = from.Rank + movement.Item2;
                return
                    // new[] { file, rank }.All(coordinate => coordinate is >= 0 and <= 7)
                    file is >= 0 and <= 7 && rank is >= 0 and <= 7
                    ?
                        EnumerableOne(new Square(file, rank))
                    :
                        // Enumerable.Empty<Square>();
                        new Square[] { };
            });

    static int[] Signs =
        new[] { -1, 1 };

    static Movement movementDirectionsDiagonal =
        // new[]
        //     { (1, 1) , (-1, 1)
        //     , (1, -1), (-1, -1)
        //     };
        Signs.SelectMany(file => new[] { (file, 1), (file, -1) });


    static Movement movementDirectionsStraight =
        // new[]
        //      {         (0,  1)
        //      , (-1, 0),        (1, 0)
        //      ,         (0, -1)
        //      };
        Signs.SelectMany(side => new[] { (side, 0), (0, side) });

    IEnumerable<Movement> AlongDirections(Movement directions) =>
        directions
            .Select(direction =>
                Enumerable.Range(1, 7)
                    .Select(distance =>
                        (distance * direction.Item1, distance * direction.Item2)
                    )
            );

    Movement movementNeighbors =
        // new[]
        //     { (-1, -1), (-1, 0), (-1, 1)
        //     , ( 0, -1),          ( 0, 1)
        //     , ( 1, -1), ( 1, 0), ( 1, 1)
        //     }
        movementDirectionsStraight.Concat(movementDirectionsDiagonal);

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
            // this number is pretty arbitrary, it just weighs king safety. Note that it doesn't weigh checkmate
            4
        }
            [(int)piece.PieceType];

    /// all pieces excluding those of PieceKind.None
    IEnumerable<Piece> PiecesIn(Board board, IEnumerable<Square> area) =>
        area
            .Select(square => board.GetPiece(square))
            .Where(piece => !piece.IsNull);

    /// all 64 Squares of the Board
    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));

    IEnumerable<A> EnumerableOne<A>(A onlyElement) =>
        new[] { onlyElement };
}
