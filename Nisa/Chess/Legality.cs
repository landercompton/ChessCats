using System.Collections.Generic;

namespace Nisa.Chess
{
    /// <summary>
    /// Efficient legal move filtering using the new attack detection.
    /// </summary>
    internal static class Legality
    {
        /// <summary>
        /// Returns true if the move leaves the side-to-move's king safe.
        /// </summary>
        public static bool IsLegal(Board b, Move m)
        {
            var u = b.MakeAndReturnUndo(m);
            bool legal = !Attacks.IsSquareAttacked(b, b.KingSquare(!b.WhiteToMove), b.WhiteToMove);
            b.Unmake(u);
            return legal;
        }

        /// <summary>
        /// Enumerates only king-safe moves.
        /// </summary>
        public static IEnumerable<Move> GenerateLegal(Board b)
        {
            foreach (var m in MoveGen.Generate(b))
            {
                if (IsLegal(b, m))
                    yield return m;
            }
        }

        /// <summary>
        /// Generate legal captures only (for quiescence search).
        /// </summary>
        public static IEnumerable<Move> GenerateLegalCaptures(Board b)
        {
            foreach (var m in MoveGen.GenerateCaptures(b))
            {
                if (IsLegal(b, m))
                    yield return m;
            }
        }

        /// <summary>
        /// Count legal moves without generating them all (for mate detection).
        /// </summary>
        public static int CountLegalMoves(Board b)
        {
            int count = 0;
            foreach (var m in MoveGen.Generate(b))
            {
                if (IsLegal(b, m))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Quick check if there's at least one legal move (for stalemate detection).
        /// </summary>
        public static bool HasLegalMove(Board b)
        {
            foreach (var m in MoveGen.Generate(b))
            {
                if (IsLegal(b, m))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Determine game status: ongoing, checkmate, or stalemate.
        /// </summary>
        public static GameStatus GetGameStatus(Board b)
        {
            bool inCheck = Attacks.InCheck(b);
            bool hasLegal = HasLegalMove(b);

            if (!hasLegal)
            {
                return inCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
            }

            // Could add draw by repetition, 50-move rule, insufficient material here
            if (b.HalfmoveClock >= 100)
            {
                return GameStatus.DrawByFiftyMove;
            }

            return GameStatus.Ongoing;
        }
    }

    public enum GameStatus
    {
        Ongoing,
        Checkmate,
        Stalemate,
        DrawByFiftyMove,
        DrawByRepetition,
        DrawByInsufficientMaterial
    }
}