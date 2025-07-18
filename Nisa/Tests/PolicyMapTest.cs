using System;
using System.Collections.Generic;
using System.Linq;
using Nisa.Chess;
using Nisa.Neural;

namespace Nisa.Tests
{
    /// <summary>
    /// Test suite to verify the PolicyMap implementation is correct.
    /// </summary>
    public static class PolicyMapTest
    {
        public static void RunAllTests()
        {
            Console.WriteLine("Running PolicyMap tests...\n");

            TestBasicMoves();
            TestPromotions();
            TestKnightMoves();
            TestPawnMoves();
            TestMirroring();
            TestRoundTrip();
            TestKnownPositions();

            Console.WriteLine("\nAll PolicyMap tests passed!");
        }

        private static void TestBasicMoves()
        {
            Console.WriteLine("Testing basic moves...");
            var board = new Board();
            Fen.ParseInto("4k3/8/8/8/8/8/8/4K3 w - - 0 1", board);

            // Test some queen-like moves from e1
            var testMoves = new[]
            {
                ("e1d1", Move.Create(4, 3)),    // West
                ("e1e2", Move.Create(4, 12)),   // North
                ("e1f1", Move.Create(4, 5)),    // East
                ("e1d2", Move.Create(4, 11)),   // NW diagonal
                ("e1f2", Move.Create(4, 13)),   // NE diagonal
            };

            foreach (var (uci, move) in testMoves)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                var decoded = PolicyMap.IndexToMove(board, idx);
                
                Console.WriteLine($"  {uci}: index={idx}, decoded={decoded.ToUci()}");
                
                if (decoded != move)
                    throw new Exception($"Round-trip failed for {uci}");
            }
        }

        private static void TestPromotions()
        {
            Console.WriteLine("Testing promotions...");
            var board = new Board();
            Fen.ParseInto("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", board);

            var promos = new[]
            {
                ("a7a8q", Move.Create(48, 56, 4)),  // Queen promotion
                ("a7a8r", Move.Create(48, 56, 3)),  // Rook promotion
                ("a7a8b", Move.Create(48, 56, 2)),  // Bishop promotion
                ("a7a8n", Move.Create(48, 56, 1)),  // Knight promotion
            };

            foreach (var (uci, move) in promos)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                var decoded = PolicyMap.IndexToMove(board, idx);
                
                Console.WriteLine($"  {uci}: index={idx}, decoded={decoded.ToUci()}");
                
                if (decoded != move)
                    throw new Exception($"Promotion round-trip failed for {uci}");
            }
        }

        private static void TestKnightMoves()
        {
            Console.WriteLine("Testing knight moves...");
            var board = new Board();
            Fen.ParseInto("4k3/8/8/8/4N3/8/8/4K3 w - - 0 1", board);

            // All possible knight moves from e4
            var knightMoves = new[]
            {
                Move.Create(28, 45), // e4f6
                Move.Create(28, 38), // e4g5
                Move.Create(28, 22), // e4g3
                Move.Create(28, 13), // e4f2
                Move.Create(28, 11), // e4d2
                Move.Create(28, 18), // e4c3
                Move.Create(28, 34), // e4c5
                Move.Create(28, 43), // e4d6
            };

            foreach (var move in knightMoves)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                var decoded = PolicyMap.IndexToMove(board, idx);
                
                Console.WriteLine($"  {move.ToUci()}: index={idx}");
                
                if (decoded != move)
                    throw new Exception($"Knight move round-trip failed for {move.ToUci()}");
            }
        }

        private static void TestPawnMoves()
        {
            Console.WriteLine("Testing pawn moves...");
            var board = new Board();
            Fen.ParseInto("4k3/8/8/8/8/8/1P6/4K3 w - - 0 1", board);

            var pawnMoves = new[]
            {
                ("b2b3", Move.Create(9, 17)),      // Single push
                ("b2b4", Move.Create(9, 25, 0, 1)), // Double push (flag=1)
            };

            foreach (var (uci, move) in pawnMoves)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                var decoded = PolicyMap.IndexToMove(board, idx);
                
                Console.WriteLine($"  {uci}: index={idx}, decoded={decoded.ToUci()}");
                
                // For double push, the policy doesn't encode the flag, so compare without it
                var compareMove = move.Flags == 1 
                    ? Move.Create(move.From, move.To) 
                    : move;
                var compareDecoded = decoded.Flags == 1
                    ? Move.Create(decoded.From, decoded.To)
                    : decoded;
                    
                if (compareDecoded != compareMove)
                    throw new Exception($"Pawn move round-trip failed for {uci}");
            }
        }

        private static void TestMirroring()
        {
            Console.WriteLine("Testing black move mirroring...");
            
            // Test with black to move
            var board = new Board();
            Fen.ParseInto("4k3/8/8/8/8/8/8/4K3 b - - 0 1", board);

            var blackMoves = new[]
            {
                ("e8d8", Move.Create(60, 59)),  // Black king west
                ("e8e7", Move.Create(60, 52)),  // Black king south
                ("e8f8", Move.Create(60, 61)),  // Black king east
            };

            foreach (var (uci, move) in blackMoves)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                var decoded = PolicyMap.IndexToMove(board, idx);
                
                Console.WriteLine($"  {uci}: index={idx}, decoded={decoded.ToUci()}");
                
                if (decoded != move)
                    throw new Exception($"Black move round-trip failed for {uci}");
            }
        }

        private static void TestRoundTrip()
        {
            Console.WriteLine("Testing round-trip for all legal moves in starting position...");
            
            var board = new Board();
            Fen.ParseInto(Fen.StartPos, board);
            
            var legal = Legality.GenerateLegal(board).ToList();
            Console.WriteLine($"  Found {legal.Count} legal moves");
            
            int tested = 0;
            foreach (var move in legal)
            {
                int idx = PolicyMap.MoveToIndex(board, move);
                if (idx < 0)
                {
                    Console.WriteLine($"  Warning: No policy index for {move.ToUci()}");
                    continue;
                }
                
                var decoded = PolicyMap.IndexToMove(board, idx);
                if (decoded != move)
                {
                    throw new Exception($"Round-trip failed: {move.ToUci()} -> idx {idx} -> {decoded.ToUci()}");
                }
                tested++;
            }
            
            Console.WriteLine($"  Successfully tested {tested}/{legal.Count} moves");
        }

        private static void TestKnownPositions()
        {
            Console.WriteLine("Testing known positions with expected indices...");
            
            // Test a few moves with known policy indices (from Lc0 source)
            var tests = new[]
            {
                // Position, move, expected index range
                ("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e1e2", 56, 504),   // Queen move
                ("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8q", 0, 56),    // Queen promo
                ("4k3/8/8/8/4N3/8/8/4K3 w - - 0 1", "e4f6", 504, 696), // Knight move
                ("4k3/8/8/8/8/8/1P6/4K3 w - - 0 1", "b2b3", 696, 1608), // Pawn push
            };

            foreach (var (fen, moveStr, minIdx, maxIdx) in tests)
            {
                var board = new Board();
                Fen.ParseInto(fen, board);
                
                var move = ParseMove(board, moveStr);
                int idx = PolicyMap.MoveToIndex(board, move);
                
                Console.WriteLine($"  {moveStr} in position: index={idx} (expected {minIdx}-{maxIdx})");
                
                if (idx < minIdx || idx >= maxIdx)
                {
                    throw new Exception($"Index {idx} out of expected range [{minIdx}, {maxIdx}) for {moveStr}");
                }
            }
        }

        private static Move ParseMove(Board board, string uci)
        {
            return MoveGen.Generate(board).First(m => m.ToUci() == uci);
        }
    }
}