
using System;
using Nisa.Tests;

namespace NisaHost
{
    /// <summary>
    /// Simple test runner that can be added to Program.cs or run separately
    /// </summary>
    public static class TestRunner
    {
        public static void RunTests()
        {
            try
            {
                Console.WriteLine("=== Running Nisa Tests ===\n");
                
                // Run policy map tests
                PolicyMapTest.RunAllTests();
                
                Console.WriteLine("\n");
                
                // Run perft tests
                Perft.RunTests();
                
                Console.WriteLine("\n");
                
                // Benchmark move generation
                Perft.BenchmarkMoveGen();
                
                Console.WriteLine("\n=== All Tests Passed! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n!!! Test Failed !!!");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }
    }
}