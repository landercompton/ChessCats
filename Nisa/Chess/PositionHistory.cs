using System;
using Nisa.Chess;

namespace Nisa.Neural
{
    /// <summary>
    /// Maintains a rolling history of the last 8 board positions for neural network encoding.
    /// Lc0 networks expect 8 positions of history to understand position repetition and dynamics.
    /// </summary>
    public sealed class PositionHistory
    {
        private readonly Board[] _history;
        private readonly ulong[] _hashes;
        private int _currentIndex;
        private int _totalMoves;

        public PositionHistory()
        {
            _history = new Board[8];
            _hashes = new ulong[8];
            for (int i = 0; i < 8; i++)
            {
                _history[i] = new Board();
            }
            Clear();
        }

        /// <summary>
        /// Clear history and set starting position
        /// </summary>
        public void Clear()
        {
            _currentIndex = 0;
            _totalMoves = 0;
            
            // Initialize all history slots with empty board
            for (int i = 0; i < 8; i++)
            {
                _history[i].Clear();
                _hashes[i] = 0;
            }
        }

        /// <summary>
        /// Add a new position to history after a move is made
        /// </summary>
        public void AddPosition(Board board)
        {
            _currentIndex = (_currentIndex + 1) % 8;
            _totalMoves++;
            
            // Deep copy the board state
            CopyBoard(board, _history[_currentIndex]);
            _hashes[_currentIndex] = Zobrist.Hash(board);
        }

        /// <summary>
        /// Get position from history. Index 0 = current, 1 = one move ago, etc.
        /// Returns null if requesting beyond available history.
        /// </summary>
        public Board? GetHistoryPosition(int movesAgo)
        {
            if (movesAgo < 0 || movesAgo >= 8) return null;
            if (movesAgo > _totalMoves) return null;
            
            int idx = (_currentIndex - movesAgo + 8) % 8;
            return _history[idx];
        }

        /// <summary>
        /// Get current position (convenience method)
        /// </summary>
        public Board GetCurrent()
        {
            return _history[_currentIndex];
        }

        /// <summary>
        /// Check if current position is repeated (for draw detection and neural net features)
        /// </summary>
        public int CountRepetitions(Board currentBoard)
        {
            ulong currentHash = Zobrist.Hash(currentBoard);
            int count = 0;
            
            // Check last 7 positions (8th would be too far for repetition)
            int positionsToCheck = Math.Min(7, _totalMoves);
            for (int i = 1; i <= positionsToCheck; i++)
            {
                int idx = (_currentIndex - i + 8) % 8;
                if (_hashes[idx] == currentHash)
                {
                    count++;
                }
            }
            
            return count;
        }

        /// <summary>
        /// Initialize history from a sequence of moves starting from a base position
        /// </summary>
        public void InitializeFromMoves(Board startPosition, Move[] moves)
        {
            Clear();
            
            var workBoard = new Board(startPosition);
            
            // If we have many moves, skip early ones and only keep last 7
            int startIdx = Math.Max(0, moves.Length - 7);
            
            // Add the position before any moves as history
            if (startIdx == 0)
            {
                AddPosition(workBoard);
            }
            
            // Play through the moves
            for (int i = startIdx; i < moves.Length; i++)
            {
                workBoard.Make(moves[i]);
                AddPosition(workBoard);
            }
        }

        /// <summary>
        /// Get hash for transposition table that includes recent history
        /// </summary>
        public ulong GetHistoryHash()
        {
            // Combine hashes of recent positions to make history-aware hash
            ulong hash = 0;
            int positions = Math.Min(4, _totalMoves + 1); // Use up to 4 recent positions
            
            for (int i = 0; i < positions; i++)
            {
                int idx = (_currentIndex - i + 8) % 8;
                hash ^= _hashes[idx] * (ulong)(i + 1); // Weight by recency
            }
            
            return hash;
        }

        /// <summary>
        /// Deep copy board state
        /// </summary>
        private static void CopyBoard(Board src, Board dst)
        {
            for (int i = 0; i < 12; i++)
            {
                dst[i] = src[i];
            }
            dst.WhiteToMove = src.WhiteToMove;
            dst.WCK = src.WCK;
            dst.WCQ = src.WCQ;
            dst.BCK = src.BCK;
            dst.BCQ = src.BCQ;
            dst.EpSq = src.EpSq;
            dst.HalfmoveClock = src.HalfmoveClock;
            dst.Fullmove = src.Fullmove;
        }
    }
}