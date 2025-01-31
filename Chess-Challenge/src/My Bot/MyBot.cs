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
/// strategy: optimize activity/potential
/// author: lue-bird on github
/// 
/// ---- documentation ----
/// ## vocab used
/// 
/// ### Piece "controls" a Square
/// That square is reachable with a move of that piece. Pins etc are not regarded.
/// In practice there is
/// - "cover": An empty square is controlled
/// - "attack": An opponent piece is controlled
/// - "defense": One of your own pieces is controlled
/// 
/// #### ray
/// A directed movement axis that can be blocked by Pieces
/// Example: rook movement to the left.
/// Knights for example do not have rays because their movement can't be blocked/intercepted.
/// 
/// Whenever Pieces are in the way of movement to the desired square, we call the square in "x-ray"
/// 
/// #### cover stability:
/// How sure you are that this cover can hold up over time.
/// For example a king covers with a low stability since king defense and attack is risky
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
        //     - give more time to forcing moves (includes checks) and captures
        //     - breath first-like
        Move chosenMove =
            board.GetLegalMoves()
                .MaxBy(move =>
                    board.IsWhiteToMove ?
                        AfterMoveEvaluate(move)
                    :
                        -AfterMoveEvaluate(move)
                );
        Console.WriteLine("evaluation guess: " + AfterMoveEvaluate(chosenMove)); // #DEBUG
        return chosenMove;

        double AfterMoveEvaluate(Move move)
        {
            board.MakeMove(move);
            var legalMoves = board.GetLegalMoves();
            // piece control advantage
            // note that the resulting double values are positive and don't take Piece color into account
            var controlByPiece =
                PiecesIn(board)
                    .ToDictionary(
                        piece => piece,
                        piece =>
                        {
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
                                            movement switch { var (file,rank)
                                            =>
                                            new[]
                                            {
                                                (file, 2 * rank),
                                                (2 * file, rank)
                                            }}
                                        )
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
                                    (AlongDirections(movementNeighbors), 0.81),
                                // King =>
                                    // cause king defense is risky
                                    (movementNeighbors.Select(EnumerableOne), 0.72)
                                }
                                [(int)piece.PieceType];
                            Dictionary<Square, double> controlBySquare = new();
                            // rays.Select(ray => doesn't save a token
                            foreach (Movement ray in rays)
                                // control along ray
                                // I wish c# had a scan/foldMap/mapAccum
                                ray
                                    // Convert relative coordinates from a given Square to an absolute square
                                    .SelectMany(movement =>
                                        (piece.Square.File + movement.Item1, piece.Square.Rank + movement.Item2) is var (file, rank) &&
                                        // alternative with equal tokens file == Clamp(file, 0, 7) && rank == Clamp(rank, 0, 7)
                                        file is >= 0 and <= 7 && rank is >= 0 and <= 7 ?
                                            EnumerableOne(new Square(file, rank))
                                        :
                                            new Square[] { }
                                    )
                                    .Select((square, index) => (index, square))
                                    .Aggregate(
                                        // how blocking the pieces in the way are: double
                                        0.0,
                                        (soFar, square) =>
                                        {
                                            controlBySquare.Add(
                                                square.Item2,
                                                // decrease control by count of blocking pieces
                                                Pow(1 + soFar * 2.1, -1.3)
                                                    // decrease stability by square-distance from origin → interception tactics
                                                    * Pow(1.0 + square.Item1, -0.118)
                                                    * stability
                                            );
                                            var pieceAtSquare = board.GetPiece(square.Item2);
                                            return
                                            soFar
                                                // increase depending on piece immobility
                                                + (pieceAtSquare.IsWhite == piece.IsWhite ?
                                                    new[]
                                                        {
                                                            // None =>
                                                            0,
                                                            // Pawn =>
                                                            1.9,
                                                            // Knight =>
                                                            0.6,
                                                            // Bishop =>
                                                            // bishop can look through queen
                                                            piece.IsQueen ? 0.2 : 0.9,
                                                            // Rook =>
                                                            // rook can look through rook and queen
                                                            piece.IsRook || piece.IsQueen ? 0.2 : 1,
                                                            // Queen =>
                                                            piece.IsBishop || piece.IsRook ? 0.2 : 0.8,
                                                            // King =>
                                                            0.8
                                                        }
                                                :
                                                    // opposing color
                                                    new[]
                                                        {
                                                            // None =>
                                                            0,
                                                            // Pawn =>
                                                            1.7,
                                                            // Knight =>
                                                            1,
                                                            // Bishop =>
                                                            1.15,
                                                            // Rook =>
                                                            0.8,
                                                            // Queen =>
                                                            0.45,
                                                            // King =>
                                                            -0.1
                                                        }
                                                )
                                                    [(int)pieceAtSquare.PieceType];
                                        }
                                    );
                            return controlBySquare;
                        }
                    );
            double evalAfterMove =
                board.IsInCheckmate() ?
                    AsAdvantageForWhiteIf(board.IsWhiteToMove, double.NegativeInfinity)

                :
                  // board.IsDraw
                  board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.IsFiftyMoveDraw() || legalMoves.Any() ?
                    0
                :
                    // TODO pawns:
                    //   - rank advancement
                    //   - path to promotion square not blocked (by opponent)
                    //   - no pawns in neighboring files
                    // TODO prefer minors pieces near opponent king, rook and queen give smaller advantage
                    // TODO advantage when pawns near king, bishop and knight only give small advantage
                    PiecesIn(board)
                        .Sum(piece => AsAdvantageForWhiteIf(piece.IsWhite, PieceAdvantage(piece)))
                        + // board control
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
                                Piece pieceAtSquare =
                                    board.GetPiece(squareControl.Key);
                                var defense =
                                    squareControl.Where(control => control.Item1.IsWhite == pieceAtSquare.IsWhite);
                                var attack =
                                    squareControl.Except(defense);
                                double
                                    defenseAdvantage =
                                        defense.Sum(s => s.Item2),
                                    attackAdvantage =
                                        attack.Sum(s => s.Item2),
                                    defenseMinusAttack =
                                        defenseAdvantage - attackAdvantage;
                                return
                                // TODO
                                //   - attacking higher-advantage pieces → higher advantage
                                //   - defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
                                //   - factor in control of focused piece
                                AsAdvantageForWhiteIf(
                                    pieceAtSquare.IsWhite,
                                    pieceAtSquare.IsNull ?
                                        // we do kinda care for covered squares
                                        defenseMinusAttack * 0.13
                                    : attackAdvantage <= 0 ?
                                        // we care just a little about defending non-attacked pieces
                                        defenseMinusAttack * 0.16
                                    : // captureChainBestAttack < 0
                                        pieceAtSquare.IsWhite != board.IsWhiteToMove ?
                                            defenseMinusAttack
                                        : // opponent piece is attacked
                                            defenseMinusAttack * 0.5
                                );
                            });
            board.UndoMove(move);
            return evalAfterMove;
        }
    }

    double AsAdvantageForWhiteIf(bool isWhite, double advantage) =>
        isWhite ? advantage : -advantage;

    static int[] Signs = { -1, 1 };

    static Movement movementDirectionsDiagonal =
        // new[]
        // { (-1,  1), (1,  1)
        // , (-1, -1), (1, -1)
        // };
        Signs.SelectMany(file => Signs.Select(rank => (file, rank)));

    static Movement movementDirectionsStraight =
        // new[]
        // {         (0,  1)
        // , (-1, 0),        (1, 0)
        // ,         (0, -1)
        // };
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
            // This number is a bit special.
            // In effect it forces king captures to be last in a capture chain.
            // Note that it doesn't weigh checkmate or king safety
            100
        }
            [(int)piece.PieceType];

    /// all pieces excluding those of PieceKind.None
    IEnumerable<Piece> PiecesIn(Board board) =>
        Enumerable.Range(0, 64)
            .Select(squareIndex => board.GetPiece(new Square(squareIndex)))
            .Where(piece => !piece.IsNull);

    IEnumerable<A> EnumerableOne<A>(A onlyElement) =>
        new[] { onlyElement };
}
