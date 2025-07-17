using System.Collections.Concurrent;
using Nisa.Chess;

namespace Nisa.Neural;

/// <summary>Thread-safe LRU-ish cache for net evaluations.</summary>
internal sealed class EvalCache
{
    private readonly ConcurrentDictionary<ulong, (float value, float[] pi)> _map = new();
    private readonly int _cap;
    private int _counter;

    public EvalCache(int capacity = 100_000) => _cap = capacity;

    public bool TryGet(Board b, out (float v, float[] pi) hit)
        => _map.TryGetValue(Zobrist.Hash(b), out hit);

    public void Add(Board b, (float v, float[] pi) entry)
    {
        if (_map.Count > _cap && _counter++ % 256 == 0)        // cheap trim
            foreach (var key in _map.Keys.Take(_map.Count / 4)) _map.TryRemove(key, out _);

        _map[Zobrist.Hash(b)] = entry;
    }
}
