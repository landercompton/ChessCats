using System;
using System.Collections.Generic;
using System.Linq;
using Nisa.Chess;
using Nisa.Neural;
using Nisa.Search;

namespace Nisa
{
    public sealed class Engine : IDisposable
    {
        private readonly GameState _gameState = new();
        private readonly Lc0Session _net;
        private readonly Mcts _mcts;

        /// <summary>
        /// Exposes the internal board state for UCI position handling.
        /// </summary>
        public Board Board => _gameState.Board;

        /// <summary>
        /// Exposes the game state for advanced usage
        /// </summary>
        public GameState GameState => _gameState;

        public Engine(string modelPath, EngineOptions? opts = null)
        {
            opts ??= new EngineOptions();
            _net = new Lc0Session(modelPath, opts);
            _mcts = new Mcts(_net, opts);
        }

        /// <summary>
        /// Start a fresh game: clear board, history, and MCTS table.
        /// </summary>
        public void NewGame()
        {
            _gameState.Clear();
            _mcts.Clear();
        }

        /// <summary>
        /// Set up the board from a FEN string or "startpos", then clear search state.
        /// </summary>
        public void SetPosition(string fenOrKeyword)
        {
            if (fenOrKeyword.Equals("startpos", StringComparison.OrdinalIgnoreCase))
                fenOrKeyword = Fen.StartPos;
            
            _gameState.SetPosition(fenOrKeyword);
            _mcts.Clear();
        }

        /// <summary>
        /// Set position and apply a list of moves (for UCI position command)
        /// </summary>
        public void SetPositionWithMoves(string fenOrKeyword, string[] moveStrings)
        {
            if (fenOrKeyword.Equals("startpos", StringComparison.OrdinalIgnoreCase))
                fenOrKeyword = Fen.StartPos;

            // Parse the moves
            var moves = new List<Move>();
            var tempBoard = new Board();
            Fen.ParseInto(fenOrKeyword, tempBoard);
            
            foreach (var moveStr in moveStrings)
            {
                var legal = MoveGen.Generate(tempBoard)
                    .FirstOrDefault(m => m.ToUci() == moveStr);
                    
                if (legal.To == 0 && legal.From == 0) 
                    continue; // Skip invalid moves
                    
                moves.Add(legal);
                tempBoard.Make(legal);
            }

            _gameState.SetPositionWithMoves(fenOrKeyword, moves.ToArray());
            _mcts.Clear();
        }

        /// <summary>
        /// Search for the best move using a fixed visit count.
        /// </summary>
        public string GetBestMove(int maxVisits = 800)
        {
            var move = _mcts.Search(_gameState, maxVisits);
            _gameState.Make(move);
            return move.ToUci();
        }

        /// <summary>
        /// Search for the best move within a time budget (milliseconds).
        /// </summary>
        public string GetBestMoveByTime(int timeMs)
        {
            var move = _mcts.SearchByTime(_gameState, timeMs, Environment.ProcessorCount);
            _gameState.Make(move);
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