// Nisa/EngineOptions.cs
using System;

namespace Nisa
{
    /// <summary>
    /// Which execution provider to use for neural inference.
    /// </summary>
    public enum GpuBackend
    {
        None,
        DirectML,
        Cuda
    }

    /// <summary>
    /// All tunables for Nisa’s search + inference stack.
    /// </summary>
    public sealed record EngineOptions
    {
        /// <summary>
        /// Number of parallel search threads (MCTS workers).
        /// </summary>
        public int Threads { get; init; } = Environment.ProcessorCount;

        /// <summary>
        /// Which ONNX Runtime execution provider to use.
        /// </summary>
        public GpuBackend Backend { get; init; } = GpuBackend.None;

        /// <summary>
        /// PUCT exploration constant ×10 (e.g. 15 → 1.5).
        /// </summary>
        public float Cpuct { get; init; } = 1.0f;

        /// <summary>
        /// Hard cap on the number of visits in fixed‐visit mode.
        /// </summary>
        public int VisitLimit { get; init; } = 4000;

        /// <summary>
        /// Path to your ONNX model file.
        /// </summary>
        public string ModelPath { get; init; } = "lc0.onnx";
    }
}
