using System;
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
        //     - breath first?
        //     - search most forcing moves first
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
            AsAdvantageIfWhite(board.IsWhiteToMove, double.NegativeInfinity)

        : board.IsDraw() ?
            0
        :
            // TODO past pawns
            PiecesIn(board, BoardSquares)
                .Sum(piece => PieceIndependentEvaluate(board, piece))
                + BoardControlEvaluate(board);

    double PieceIndependentEvaluate(Board board, Piece piece) =>
        AsAdvantageIfWhite(piece.IsWhite, PieceAdvantage(piece));

    double BoardControlEvaluate(Board board)
    {
        var controlByPiece =
            PiecesIn(board, BoardSquares)
                .ToDictionary(piece => piece, piece => PieceControl(board, piece));
        return
        controlByPiece
            .SelectMany(piece => piece.Value)
            .ToLookup(s => s.Key, s => s.Value)
            .Sum(s =>
            {
                // TODO attacking higher-advantage pieces → higher advantage
                // TODO defending higher-advantage pieces → slightly higher advantage. Especially for knight, bishop, rook
                // TODO distribute defense and attack for the x-rayed squares.
                var whites =
                    s.Where(advantage => advantage >= 0);
                var blacks =
                    s.Where(advantage => advantage < 0);
                Piece pieceAtSquare =
                    board.GetPiece(s.Key);
                double defense =
                    (pieceAtSquare.IsWhite ? whites : blacks).Sum();
                double attack =
                    (pieceAtSquare.IsWhite ? blacks : whites).Sum();
                return
                pieceAtSquare.IsNull ?
                    defense * 0.1
                : attack == 0 ?
                    defense * 0.21
                : Abs(defense) >= Abs(attack) ?
                    (defense + attack) * 0.3
                : // Abs(attack) >= Abs(defense)
                    pieceAtSquare.IsWhite == board.IsWhiteToMove ?
                        // TODO increase piece advantage by covered fields
                        Clamp(defense + attack, -1, 1)
                            * PieceAdvantage(pieceAtSquare)
                    : // opponent piece is attacked
                        defense + attack;
            });
    }

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
    Dictionary<Square, double> WalkRays(Board board, Square from, (IEnumerable<Movement>, double) config) =>
        config.Item1
            .SelectMany(ray => WalkRay(board, from, ray))
            .ToDictionary(s => s.Key, s => s.Value * config.Item2);

    IEnumerable<KeyValuePair<Square, double>> WalkRay(Board board, Square from, Movement ray) =>
        // btw I wish c# had a scan/foldMap
        MovementSquaresFrom(from, ray)
            .Aggregate(
                (0, Enumerable.Empty<KeyValuePair<Square, double>>()),
                (soFar, square) =>
                    (board.GetPiece(square).IsNull ? soFar.Item1 : soFar.Item1 + 1
                    , soFar.Item2.Append(new(square, 1 / (1 + soFar.Item1)))
                    )
            )
            .Item2;

    double XRay(Board board, IEnumerable<Square> squares) =>
        Pow(1.0 + (PiecesIn(board, squares)).Count(), 0.5);

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
                    file is >= 0 and <= 7 && rank is >= 0 and <= 7
                    // saves 2 tokens over
                    // new[] { file, rank }.All(coordinate => coordinate is >= 0 and <= 7)
                    ?
                        EnumerableOne(new Square(file, rank))
                    :
                        // 2 tokens less than Enumerable.Empty<Square>();
                        new Square[] { };
            });

    static int[] Signs =
        new[] { -1, 1 };

    static Movement movementDirectionsDiagonal =
        // new[]
        //     { (1, 1) , (-1, 1)
        //     , (1, -1), (-1, -1)
        //     };
        Signs.SelectMany(file => Signs.Select(rank => (file, rank)));

    IEnumerable<Movement> movementDiagonal =
        AlongDirections(movementDirectionsDiagonal);

    static Movement movementDirectionsStraight =
         // Signs.Select(file => (file, 0))
         //     .Concat(Signs.Select(rank => (0, rank)));
         new[]
             {         (0,  1)
             , (-1, 0),        (1, 0)
             ,         (0, -1)
             };

    IEnumerable<Movement> movementStraight =
        AlongDirections(movementDirectionsStraight);

    static IEnumerable<Movement> AlongDirections(Movement directions) =>
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
    Movement SquaresForwardLeftAndRight(Piece pawn) =>
        Signs.Select(file => (file, pawn.IsWhite ? 1 : -1));

    Movement movementL =
        //new[]
        //    {     (-1, 2),       (1, 2)
        //    , (-2, 1),              (2, 1)
        //
        //    , (-2, -1),             (2, -1)
        //    ,     (-1, -2),     (1, -2)
        //    };
        movementDirectionsDiagonal
            .Select(movement => (movement.Item1, 2 * movement.Item2))
            .Concat(
                movementDirectionsDiagonal
                    .Select(movement => (2 * movement.Item1, movement.Item2))
            );

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

    IEnumerable<Piece> PiecesIn(Board board, IEnumerable<Square> area) =>
        area
            .Select(square => board.GetPiece(square))
            .Where(piece => !piece.IsNull);

    IEnumerable<Square> BoardSquares =
        Enumerable.Range(0, 64).Select(i => new Square(i));

    IEnumerable<A> EnumerableOne<A>(A onlyElement) =>
        new[] { onlyElement };
}
