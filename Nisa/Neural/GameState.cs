using Nisa.Chess;

namespace Nisa.Neural
{
    /// <summary>
    /// Combines a chess board with its position history for neural network evaluation.
    /// This ensures board and history stay synchronized.
    /// </summary>
    public sealed class GameState
    {
        public Board Board { get; }
        public PositionHistory History { get; }

        public GameState()
        {
            Board = new Board();
            History = new PositionHistory();
        }

        /// <summary>
        /// Copy constructor for search tree exploration
        /// </summary>
        public GameState(GameState source)
        {
            Board = new Board(source.Board);
            History = new PositionHistory();
            
            // Copy history by replaying positions
            for (int i = 7; i >= 0; i--)
            {
                var pos = source.History.GetHistoryPosition(i);
                if (pos != null && pos.OccupancyAll != 0)
                {
                    History.AddPosition(pos);
                }
            }
        }

        /// <summary>
        /// Clear both board and history
        /// </summary>
        public void Clear()
        {
            Board.Clear();
            History.Clear();
            History.AddPosition(Board); // Add starting position to history
        }

        /// <summary>
        /// Make a move and update history
        /// </summary>
        public Board.Undo Make(Move move)
        {
            var undo = Board.Make(move);
            History.AddPosition(Board);
            return undo;
        }

        /// <summary>
        /// Set position from FEN and clear history
        /// </summary>
        public void SetPosition(string fen)
        {
            Fen.ParseInto(fen, Board);
            History.Clear();
            History.AddPosition(Board);
        }

        /// <summary>
        /// Set position and apply a sequence of moves
        /// </summary>
        public void SetPositionWithMoves(string fen, Move[] moves)
        {
            Fen.ParseInto(fen, Board);
            History.Clear();
            History.AddPosition(Board);
            
            foreach (var move in moves)
            {
                Board.Make(move);
                History.AddPosition(Board);
            }
        }

        /// <summary>
        /// Get a hash that includes position history for transposition table
        /// </summary>
        public ulong GetHistoryAwareHash()
        {
            return Zobrist.Hash(Board) ^ History.GetHistoryHash();
        }
    }
}