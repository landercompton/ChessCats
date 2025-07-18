using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Nisa.Chess;
using Nisa.Neural;

namespace Nisa.Search
{
    /// <summary>
    /// PUCT Monte-Carlo Tree Search with position history support and improved transposition handling.
    /// </summary>
    internal sealed class Mcts
    {
        // ───────────────────────────────────────────────────────────────────────────
        // Inner Node type
        // ───────────────────────────────────────────────────────────────────────────
        private sealed class Node
        {
            public readonly object Sync = new();
            public readonly Dictionary<Move, Node> Child = new();
            public float P, Q, W;
            public int N;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Fields & constants
        // ───────────────────────────────────────────────────────────────────────────
        private readonly Lc0Session _net;
        private readonly EngineOptions _o;
        private readonly ConcurrentDictionary<ulong, Node> _table = new();

        private static readonly ThreadLocal<Random> Rnd = new(() => new Random());

        private const int NoiseMoveThreshold = 20;
        private const float NoiseEpsilon = 0.25f;
        private const double NoiseAlpha = 0.3;
        private const float VirtualLoss = 0.3f; // Reduced from 1.0

        // ───────────────────────────────────────────────────────────────────────────
        // Constructor & Clear
        // ───────────────────────────────────────────────────────────────────────────
        public Mcts(Lc0Session net, EngineOptions o)
        {
            _net = net;
            _o = o;
        }

        /// <summary>Clear the global transposition table.</summary>
        public void Clear() => _table.Clear();

        // ───────────────────────────────────────────────────────────────────────────
        // Fixed-visit PUCT search
        // ───────────────────────────────────────────────────────────────────────────
        public Move Search(GameState root, int maxVisits)
        {
            // 1) root node setup with history-aware hash
            ulong rootKey = root.GetHistoryAwareHash();
            var rootNode = _table.GetOrAdd(rootKey, _ => new Node());
            if (rootNode.Child.Count == 0)
                Expand(root, rootNode);

            // inject Dirichlet noise at root if appropriate
            InjectRootNoise(root.Board, rootNode);

            // 2) launch threads for fixed visits
            int visitsPerThread = maxVisits / _o.Threads;
            var workers = Enumerable.Range(0, _o.Threads).Select(_ =>
            {
                var local = new GameState(root); // Copy with history
                return new Thread(() =>
                {
                    for (int i = 0; i < visitsPerThread; i++)
                        Simulate(local, rootNode);
                });
            }).ToArray();

            foreach (var t in workers) t.Start();
            foreach (var t in workers) t.Join();

            // 3) pick best by visit count
            return rootNode.Child
                .OrderByDescending(kv => kv.Value.N)
                .First().Key;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Timed PUCT search until timeMs expires
        // ───────────────────────────────────────────────────────────────────────────
        public Move SearchByTime(GameState root, int timeMs, int threads)
        {
            ulong rootKey = root.GetHistoryAwareHash();
            var rootNode = _table.GetOrAdd(rootKey, _ => new Node());
            if (rootNode.Child.Count == 0)
                Expand(root, rootNode);

            InjectRootNoise(root.Board, rootNode);

            var sw = Stopwatch.StartNew();
            var workers = Enumerable.Range(0, threads).Select(_ =>
            {
                var local = new GameState(root);
                return new Thread(() =>
                {
                    while (sw.ElapsedMilliseconds < timeMs)
                        Simulate(local, rootNode);
                });
            }).ToArray();

            foreach (var t in workers) t.Start();
            foreach (var t in workers) t.Join();

            return rootNode.Child
                .OrderByDescending(kv => kv.Value.N)
                .First().Key;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // One simulation: selection → expansion → back-propagation
        // ───────────────────────────────────────────────────────────────────────────
        private void Simulate(GameState gameState, Node node)
        {
            var path = new Stack<(Node parent, Move mv, Board.Undo undo)>();

            // 1) selection with virtual loss
            while (true)
            {
                lock (node.Sync)
                {
                    node.N++;
                    node.W -= VirtualLoss;
                }
                if (node.Child.Count == 0)
                    break;

                Move mv = PickChild(node);
                var undo = gameState.Make(mv); // This updates history
                path.Push((node, mv, undo));
                
                // Get child node with history-aware hash
                ulong childKey = gameState.GetHistoryAwareHash();
                node = _table.GetOrAdd(childKey, _ => node.Child[mv]);
            }

            // 2) expansion or terminal
            float value = (node.N == 1)
                ? Expand(gameState, node)
                : (BitOps.PopCount(gameState.Board[Board.BK]) == 0 ? 1f : 0f);

            // 3) back-propagate
            while (path.Count > 0)
            {
                var (parent, mv, undo) = path.Pop();
                gameState.Board.Unmake(undo);
                // Note: History is not unwound here - consider if this matters for your use case
                
                lock (parent.Sync)
                {
                    var child = parent.Child[mv];
                    child.W += value + VirtualLoss; // Remove virtual loss + add value
                    child.Q = child.W / child.N;
                }
                value = -value;
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Expansion: evaluate network, add/reuse child nodes, return leaf value
        // ───────────────────────────────────────────────────────────────────────────
        private float Expand(GameState gameState, Node node)
        {
            var (v, pi) = _net.Evaluate(gameState);
            var legal = Legality.GenerateLegal(gameState.Board).ToArray();
            float sumP = 1e-6f;

            foreach (var mv in legal)
            {
                var undo = gameState.Make(mv);
                ulong key = gameState.GetHistoryAwareHash();
                gameState.Board.Unmake(undo);

                var child = _table.GetOrAdd(key, _ => new Node());
                int idx = PolicyMap.MoveToIndex(gameState.Board, mv);
                child.P = (idx >= 0 && idx < pi.Length) ? pi[idx] : 0f;
                sumP += child.P;

                lock (node.Sync)
                {
                    node.Child[mv] = child;
                }
            }

            // normalize priors
            foreach (var c in node.Child.Values)
                c.P /= sumP;

            node.Q = v;
            node.W = v;
            node.N = 1;
            return v;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // PUCT selection over a snapshot of children under lock
        // ───────────────────────────────────────────────────────────────────────────
        private Move PickChild(Node p)
        {
            double c = _o.Cpuct;
            int totalN = p.N;

            Move bestMv = default!;
            double bestSc = double.NegativeInfinity;

            KeyValuePair<Move, Node>[] entries;
            lock (p.Sync)
            {
                entries = p.Child.ToArray();
            }

            foreach (var (mv, child) in entries)
            {
                double q = child.Q;
                double u = c * child.P * Math.Sqrt(totalN) / (1 + child.N);
                double score = q + u;
                if (score > bestSc)
                {
                    bestSc = score;
                    bestMv = mv;
                }
            }

            return bestMv;
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Inject dynamic Dirichlet noise at root
        // ───────────────────────────────────────────────────────────────────────────
        private void InjectRootNoise(Board board, Node rootNode)
        {
            var legal = Legality.GenerateLegal(board).ToArray();
            int m = legal.Length;
            if (m >= NoiseMoveThreshold || m == 0)
                return;

            // sample Dirichlet noise via Gamma(shape=α)
            var noise = new double[m];
            double sum = 0;
            for (int i = 0; i < m; i++)
            {
                noise[i] = SampleGamma(NoiseAlpha);
                sum += noise[i];
            }
            for (int i = 0; i < m; i++)
                noise[i] /= sum;

            lock (rootNode.Sync)
            {
                for (int i = 0; i < m; i++)
                {
                    var mv = legal[i];
                    if (rootNode.Child.TryGetValue(mv, out var child))
                    {
                        child.P = (1 - NoiseEpsilon) * child.P
                                + (float)(NoiseEpsilon * noise[i]);
                    }
                }
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Gamma sampling (Marsaglia & Tsang)
        // ───────────────────────────────────────────────────────────────────────────
        private static double SampleGamma(double shape)
        {
            var rnd = Rnd.Value!;
            if (shape < 1.0)
            {
                double u = rnd.NextDouble();
                return SampleGamma(shape + 1) * Math.Pow(u, 1.0 / shape);
            }

            // α ≥ 1 case
            double d = shape - 1.0 / 3.0;
            double c = 1.0 / Math.Sqrt(9 * d);
            while (true)
            {
                double x = SampleNormal(rnd);
                double v = 1 + c * x;
                if (v <= 0) continue;
                v = v * v * v;
                double u = rnd.NextDouble();
                if (u < 1 - 0.0331 * x * x * x * x) return d * v;
                if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                    return d * v;
            }
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Standard normal sampler via Box–Muller
        // ───────────────────────────────────────────────────────────────────────────
        private static double SampleNormal(Random rnd)
        {
            double u1 = rnd.NextDouble();
            double u2 = rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}