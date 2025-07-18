using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nisa.Chess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nisa.Neural
{
    /// <summary>Thread-safe Leela-style network wrapper with position history support.</summary>
    internal sealed class Lc0Session : IDisposable
    {
        // ─── ctor-time invariants ────────────────────────────────────────────────
        private readonly InferenceSession _sess;
        private readonly string _inputName;
        private readonly string _policyName;
        private readonly string _wdlName;
        private readonly string _valueName;
        private readonly int _planes;

        // ─── position cache ─────────────────────────────────────────────────────
        private readonly EvalCache _cache = new();

        // ─── producer-consumer infrastructure ───────────────────────────────────
        private readonly BlockingCollection<Request> _queue = new();
        private readonly Thread _worker;
        private readonly CancellationTokenSource _cts = new();

        private const int _maxBatch = 16;
        private const int _maxDelayMs = 2;

        private sealed record Request(
            GameState GameState,
            TaskCompletionSource<(float value, float[] policy)> Tcs);

        public Lc0Session(string modelPath, EngineOptions opt)
        {
            // 1) choose execution provider
            var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            switch (opt.Backend)
            {
                case GpuBackend.DirectML: 
                    TryAppend(() => so.AppendExecutionProvider_DML(0), "DirectML"); 
                    break;
                case GpuBackend.Cuda: 
                    TryAppend(() => so.AppendExecutionProvider_CUDA(0), "CUDA"); 
                    break;
                default: 
                    Console.Error.WriteLine("info string CPU provider selected"); 
                    break;
            }

            // 2) open the network
            _sess = new InferenceSession(modelPath, so);

            foreach (var (k, v) in _sess.OutputMetadata)
                Console.Error.WriteLine($"info string Output `{k}` dims: {string.Join('×', v.Dimensions)}");

            // 3) discover I/O names
            _inputName = _sess.InputMetadata.First().Key;
            _planes = _sess.InputMetadata.First().Value.Dimensions[1] is -1 ? 112
                : _sess.InputMetadata.First().Value.Dimensions[1];

            (_policyName, _wdlName, _valueName) = ResolveOutputs(_sess);

            Console.Error.WriteLine($"info string Network expects {_planes} input planes");

            // 4) spawn batching thread
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Lc0Batcher" };
            _worker.Start();
        }

        // ─── public API ──────────────────────────────────────────────────────────
        public (float value, float[] policy) Evaluate(GameState gameState)
        {
            // Use history-aware hash for caching
            ulong cacheKey = gameState.GetHistoryAwareHash();
            
            if (_cache.TryGet(gameState.Board, out var hit)) 
                return hit;

            var tcs = new TaskCompletionSource<(float, float[])>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(new Request(gameState, tcs), _cts.Token);
            return tcs.Task.GetAwaiter().GetResult();
        }

        // ─── mini-batch worker ───────────────────────────────────────────────────
        private void WorkerLoop()
        {
            var batch = new List<Request>(_maxBatch);

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    // 1) block for first job
                    if (!_queue.TryTake(out var first, Timeout.Infinite, _cts.Token))
                        continue;
                    batch.Add(first);

                    // 2) fill batch opportunistically
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (batch.Count < _maxBatch &&
                           sw.ElapsedMilliseconds < _maxDelayMs &&
                           _queue.TryTake(out var next))
                    {
                        batch.Add(next);
                    }

                    // 3) encode [B,P,8,8] tensor with history
                    int B = batch.Count;
                    int stride = _planes * 8 * 8;
                    var raw = new float[B * stride];

                    for (int i = 0; i < B; i++)
                    {
                        var slice = new DenseTensor<float>(
                            raw.AsMemory(i * stride, stride),
                            new[] { 1, _planes, 8, 8 });
                        
                        // Use new encoder with position history
                        Encoder.Encode(batch[i].GameState.History, slice);
                    }

                    var inputTensor = new DenseTensor<float>(raw, new[] { B, _planes, 8, 8 });
                    using var results = _sess.Run(
                        new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) },
                        new[] { _policyName, _wdlName, _valueName }
                    );

                    var policyFlat = results.First(r => r.Name == _policyName).AsEnumerable<float>().ToArray();
                    var wdlFlat = results.First(r => r.Name == _wdlName).AsEnumerable<float>().ToArray();
                    var valueFlat = results.First(r => r.Name == _valueName).AsEnumerable<float>().ToArray();

                    int policyStride = policyFlat.Length / B;

                    for (int i = 0; i < B; i++)
                    {
                        // Policy soft-max
                        var logits = policyFlat.Skip(i * policyStride).Take(policyStride).ToArray();
                        double m = logits.Max();
                        double sum = 0;
                        var exp = new double[policyStride];
                        for (int j = 0; j < policyStride; j++) 
                        { 
                            exp[j] = Math.Exp(logits[j] - m); 
                            sum += exp[j]; 
                        }
                        var p = new float[policyStride];
                        for (int j = 0; j < policyStride; j++) 
                            p[j] = (float)(exp[j] / sum);

                        // Value: prefer WDL -> scalar, else fall back to 1-value head
                        float v;
                        if (wdlFlat.Length >= 3 * (i + 1))
                        {
                            float pw = wdlFlat[3 * i + 0];
                            float pd = wdlFlat[3 * i + 1];
                            float pl = wdlFlat[3 * i + 2];
                            v = pw - pl; // Win minus loss probability
                        }
                        else
                        {
                            v = (float)Math.Tanh(valueFlat[i]);
                        }

                        Console.Error.WriteLine($"info string NN eval → v={v:F3}, top1P={p.Max():F3}");

                        _cache.Add(batch[i].GameState.Board, (v, p));
                        batch[i].Tcs.TrySetResult((v, p));
                    }

                    batch.Clear();
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                foreach (var req in batch) req.Tcs.TrySetException(ex);
                while (_queue.TryTake(out var req)) req.Tcs.TrySetException(ex);
            }
        }

        // ─── IDisposable ────────────────────────────────────────────────────────
        public void Dispose()
        {
            _cts.Cancel(); 
            _worker.Join();
            _sess.Dispose(); 
            _queue.Dispose(); 
            _cts.Dispose();
        }

        // ─── helpers ────────────────────────────────────────────────────────────
        private static void TryAppend(Action append, string tag)
        {
            try 
            { 
                append(); 
                Console.Error.WriteLine($"info string {tag} provider loaded"); 
            }
            catch (Exception ex) 
            { 
                Console.Error.WriteLine($"info string {tag} unavailable: {ex.Message} (fallback to CPU)"); 
            }
        }

        /// <summary>Identify the three heads (policy / wdl / scalar-value)</summary>
        private static (string policy, string wdl, string value) ResolveOutputs(InferenceSession s)
        {
            string? pol = null, wdl = null, val = null;

            foreach (var (name, meta) in s.OutputMetadata)
            {
                long elems = meta.Dimensions.Aggregate(1L, (a, d) => a * (d == -1 ? 1 : d));
                if (elems == 1858)       pol ??= name;     // policy
                else if (elems == 3)     wdl ??= name;     // win-draw-loss
                else if (elems == 1)     val ??= name;     // scalar value
            }

            // Fallbacks for 2-head nets: use scalar for both roles
            wdl ??= val ?? throw new InvalidOperationException("No value/WDL head found");
            val ??= wdl;

            return (pol!, wdl, val);
        }
    }
}