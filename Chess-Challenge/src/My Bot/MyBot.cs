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
        //     - search most forcing moves first
        //     - breath first?
        //     - alpha-beta?
        Move chosenMove =
            board.GetLegalMoves()
                .MaxBy(move =>
                    board.IsWhiteToMove ?
                        AfterMoveEvaluate(move)
                    :
                        -AfterMoveEvaluate(move)
                );
        Console.WriteLine("evaluation guess: " + AfterMoveEvaluate(chosenMove));
        return chosenMove;

        double AfterMoveEvaluate(Move move)
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
                // TODO advanced pawns
                // TODO past pawns
                // TODO prefer heavies and minors near opponent king
                // TODO advantage when pawns near king, bishop and knight only give small advantage
                // TODO encourage covering squares near king
                PiecesIn(board)
                    .Sum(piece => AsAdvantageForWhiteIf(piece.IsWhite, PieceAdvantage(piece)))
                    + BoardControlEvaluate(board);
    }

    double BoardControlEvaluate(Board board)
    {
        var controlByPiece =
            PiecesIn(board)
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
                // TODO if a piece is attacked more often than defended, remove its attack control and cover from it
                // TODO: capture chain: order both by PieceAdvantageIndependent and zip them
                //       start with the PieceAdvantageIndependent(first attacker) - PieceAdvantageIndependent(pieceAtSquare)
                //       if at any point the balance is negative, save and restart
                //       |> take the biggest negative balance
                Piece pieceAtSquare =
                    board.GetPiece(squareControl.Key);
                bool pieceAtSquareIsWhite =
                    pieceAtSquare.IsWhite;
                var defense =
                    squareControl.Where(control => control.Item1.IsWhite == pieceAtSquareIsWhite);
                var attack =
                    squareControl.Except(defense);
                IEnumerable<double> DirectOrdered(IEnumerable<(Piece, double)> controlByPieces) =>
                    controlByPieces
                        .Where(cover => cover.Item2 >= 0.55)
                        .Select(cover => PieceAdvantage(cover.Item1))
                        .OrderBy(value => value);
                var defenders =
                    DirectOrdered(defense);
                var attackers =
                    DirectOrdered(attack);
                double
                    defenseAdvantage =
                        defense.Sum(s => s.Item2),
                    attackAdvantage =
                        attack.Sum(s => s.Item2),
                    defenseMinusAttack =
                        defenseAdvantage - attackAdvantage,
                    // direct captures, ignoring x-raying attackers and defenders
                    // positive means the defense always has an advantage after capturing back
                    captureChainBestAttack =
                        Enumerable.Range(0, Max(defenders.Count(), attackers.Count()))
                            .Aggregate(
                                // ( material value of the piece on the focused square: double
                                // , current material balance (positive = better for defenders): double
                                // , best material balance found for attack capture chain: double
                                // )
                                (PieceAdvantage(pieceAtSquare), 0.0, 0.0),
                                (soFar, i) =>
                                {
                                    double
                                        focusValue = soFar.Item1,
                                        attackerValue = attackers.ElementAtOrDefault(i),
                                        defenderValue = defenders.ElementAtOrDefault(i),
                                        newBalanceIfAttackerExists =
                                            soFar.Item2 - focusValue
                                            +
                                            (defenderValue is 0 ? 0 : attackerValue);
                                    return
                                        attackerValue is 0 ?
                                            (0
                                            , soFar.Item2 + focusValue
                                            , soFar.Item3 + focusValue
                                            )
                                        :
                                            // attackerValue > 0
                                            (defenderValue
                                            , newBalanceIfAttackerExists
                                            , Min(soFar.Item3, newBalanceIfAttackerExists)
                                            );
                                }
                            )
                            .Item3;
                return
                AsAdvantageForWhiteIf(
                    pieceAtSquareIsWhite,
                    pieceAtSquare.IsNull ?
                        // we do kinda care for covered squares
                        defenseMinusAttack * 0.13
                    : attackAdvantage <= 0.16 ?
                        // we care just a little about defending non-attacked pieces
                        defenseMinusAttack * 0.16
                    : captureChainBestAttack >= 0 ?
                        // TODO why does that not work but -captureChainBestAttack does??
                        defenseMinusAttack * 0.34
                    : // captureChainBestAttack < 0
                        pieceAtSquareIsWhite != board.IsWhiteToMove ?
                            // TODO erase control of captured pieces
                            captureChainBestAttack
                        : // opponent piece is attacked
                            defenseMinusAttack
                );
            });
    }

    /// note that the resulting double values are positive and don't take Piece color into account
    Dictionary<Square, double> PieceControlAdvantage(Board board, Piece piece)
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
                                        piece.IsQueen ? 0 : 0.9,
                                        // Rook =>
                                        // rook can look through rook and queen
                                        piece.IsRook || piece.IsQueen ? 0 : 1,
                                        // Queen =>
                                        piece.IsBishop || piece.IsRook ? 0 : 0.8,
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
                                        1.1,
                                        // Bishop =>
                                        1.1,
                                        // Rook =>
                                        1,
                                        // Queen =>
                                        0.8,
                                        // King =>
                                        -0.1
                                    }
                            )
                                [(int)pieceAtSquare.PieceType];
                    }
                );
        return controlBySquare;
    }

    double AsAdvantageForWhiteIf(bool isWhite, double advantage) =>
        isWhite ? advantage : -advantage;

    static int[] Signs = { -1, 1 };

    static Movement movementDirectionsDiagonal =
        // { (1, 1) , (-1, 1)
        // , (1, -1), (-1, -1)
        // };
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
