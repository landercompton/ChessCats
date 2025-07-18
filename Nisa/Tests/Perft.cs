using System;
using System.Diagnostics;
using Nisa.Chess;

namespace Nisa.Tests
{
    /// <summary>
    /// Perft (performance test) for validating move generation correctness.
    /// </summary>
    public static class Perft
    {
        public static void RunTests()
        {
            Console.WriteLine("Running Perft tests...\n");

            // Standard test positions with known node counts
            var tests = new[]
            {
                // Starting position
                new { 
                    Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                    Depths = new[] { 20, 400, 8902, 197281, 4865609 }
                },
                
                // Kiwipete position (complex position with promotions, castling, ep)
                new { 
                    Fen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
                    Depths = new[] { 48, 2039, 97862, 4085603 }
                },
                
                // Position with many promotions
                new { 
                    Fen = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
                    Depths = new[] { 14, 191, 2812, 43238, 674624 }
                },
                
                // En passant test
                new { 
                    Fen = "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3",
                    Depths = new[] { 31, 868, 27336, 788456 }
                }
            };

            foreach (var test in tests)
            {
                Console.WriteLine($"Testing position: {test.Fen}");
                var board = new Board();
                Fen.ParseInto(test.Fen, board);

                for (int depth = 1; depth <= Math.Min(test.Depths.Length, 4); depth++)
                {
                    var sw = Stopwatch.StartNew();
                    long nodes = PerftCount(board, depth);
                    sw.Stop();

                    long expected = test.Depths[depth - 1];
                    bool correct = nodes == expected;
                    
                    Console.WriteLine($"  Depth {depth}: {nodes,10} nodes " +
                                    $"(expected {expected,10}) in {sw.ElapsedMilliseconds,5} ms " +
                                    $"[{(correct ? "PASS" : "FAIL")}]");

                    if (!correct)
                    {
                        // Run divide to find the problematic move
                        Console.WriteLine("\n  Running divide to find error:");
                        Divide(board, depth);
                        throw new Exception($"Perft failed at depth {depth}");
                    }
                }
                Console.WriteLine();
            }

            Console.WriteLine("All Perft tests passed!");
        }

        /// <summary>
        /// Count nodes at given depth using legal move generation.
        /// </summary>
        private static long PerftCount(Board board, int depth)
        {
            if (depth == 0) return 1;

            long nodes = 0;
            foreach (var move in Legality.GenerateLegal(board))
            {
                var undo = board.Make(move);
                nodes += PerftCount(board, depth - 1);
                board.Unmake(undo);
            }
            return nodes;
        }

        /// <summary>
        /// Divide function - shows node count for each root move.
        /// Useful for debugging move generation errors.
        /// </summary>
        public static void Divide(Board board, int depth)
        {
            long total = 0;
            
            foreach (var move in Legality.GenerateLegal(board))
            {
                var undo = board.Make(move);
                long nodes = depth > 1 ? PerftCount(board, depth - 1) : 1;
                board.Unmake(undo);
                
                Console.WriteLine($"  {move.ToUci()}: {nodes}");
                total += nodes;
            }
            
            Console.WriteLine($"  Total: {total}");
        }

        /// <summary>
        /// Performance test - measure move generation speed.
        /// </summary>
        public static void BenchmarkMoveGen()
        {
            Console.WriteLine("\nBenchmarking move generation speed...");
            
            var board = new Board();
            Fen.ParseInto(Fen.StartPos, board);
            
            // Warm up
            for (int i = 0; i < 1000; i++)
            {
                var moves = MoveGen.Generate(board);
            }
            
            // Benchmark pseudo-legal generation
            var sw = Stopwatch.StartNew();
            const int iterations = 100000;
            
            for (int i = 0; i < iterations; i++)
            {
                var moves = MoveGen.Generate(board);
            }
            
            sw.Stop();
            double pseudoLegalPerSec = iterations / (sw.ElapsedMilliseconds / 1000.0);
            
            // Benchmark legal generation
            sw.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                var moves = Legality.GenerateLegal(board).ToList();
            }
            
            sw.Stop();
            double legalPerSec = iterations / (sw.ElapsedMilliseconds / 1000.0);
            
            Console.WriteLine($"  Pseudo-legal: {pseudoLegalPerSec:F0} positions/sec");
            Console.WriteLine($"  Legal:        {legalPerSec:F0} positions/sec");
        }
    }
}