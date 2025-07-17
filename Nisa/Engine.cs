using System;
using Nisa.Chess;
using Nisa.Neural;
using Nisa.Search;

namespace Nisa
{
    public sealed class Engine : IDisposable
    {
        private readonly Board _board = new();
        private readonly Lc0Session _net;
        private readonly Mcts _mcts;

        /// <summary>
        /// Exposes the internal board state for UCI position handling.
        /// </summary>
        public Board Board => _board;

        public Engine(string modelPath, EngineOptions? opts = null)
        {
            opts ??= new EngineOptions();
            _net  = new Lc0Session(modelPath, opts);
            _mcts = new Mcts(_net, opts);
        }

        /// <summary>
        /// Start a fresh game: clear board and MCTS table.
        /// </summary>
        public void NewGame()
        {
            _board.Clear();
            _mcts.Clear();
        }

        /// <summary>
        /// Set up the board from a FEN string or "startpos", then clear search state.
        /// </summary>
        public void SetPosition(string fenOrKeyword)
        {
            if (fenOrKeyword.Equals("startpos", StringComparison.OrdinalIgnoreCase))
                fenOrKeyword = Fen.StartPos;
            Fen.ParseInto(fenOrKeyword, _board);
            _mcts.Clear();
        }

        /// <summary>
        /// Search for the best move using a fixed visit count.
        /// </summary>
        public string GetBestMove(int maxVisits = 800)
        {
            var move = _mcts.Search(_board, maxVisits);
            _board.Make(move);
            return move.ToUci();
        }

        /// <summary>
        /// Search for the best move within a time budget (milliseconds).
        /// </summary>
        public string GetBestMoveByTime(int timeMs)
        {
            var move = _mcts.SearchByTime(_board, timeMs, Environment.ProcessorCount);
            _board.Make(move);
            return move.ToUci();
        }

        /// <summary>
        /// Release the neural-network resources.
        /// </summary>
        public void Dispose()
        {
            _net.Dispose();
        }
    }
}
