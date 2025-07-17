using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Nisa;
using Nisa.Chess;

string modelPath = Path.Combine(AppContext.BaseDirectory, "lc0.onnx");

// ────────────────────────────────────────────────────────────────────────────
// Global engine options / instance
// ────────────────────────────────────────────────────────────────────────────
EngineOptions opts = new() { Backend = GpuBackend.DirectML };
Engine? engine = null;

void RecreateEngine()
{
    engine?.Dispose();
    engine = new Engine(modelPath, opts);
}

// ────────────────────────────────────────────────────────────────────────────
// Console I/O helpers
// ────────────────────────────────────────────────────────────────────────────
ConcurrentQueue<string> gui2eng    = new();
CancellationTokenSource? searchCts = null;
Task?                    searchTask = null;
object                   ioLock     = new();

void WriteLine(string txt)
{
    lock (ioLock)
        Console.WriteLine(txt);
}

// ────────────────────────────────────────────────────────────────────────────
// Position handling
// ────────────────────────────────────────────────────────────────────────────
void ApplyMoves(string[] moves)
{
    foreach (var m in moves)
    {
        var legal = MoveGen.Generate(engine!.Board)
                           .FirstOrDefault(x => x.ToUci() == m);
        if (legal.To == 0 && legal.From == 0) continue;
        engine!.Board.Make(legal);
    }
}

void SetPosition(string line)
{
    RecreateEngine();

    var parts    = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    int idxMoves = Array.FindIndex(parts, p => p == "moves");

    string fen = parts[1] == "startpos"
        ? Fen.StartPos
        : string.Join(' ', parts[1..(idxMoves == -1 ? parts.Length : idxMoves)]);

    engine!.SetPosition(fen);

    if (idxMoves != -1)
        ApplyMoves(parts[(idxMoves + 1)..]);
}

// ────────────────────────────────────────────────────────────────────────────
// Search control (timed + fixed visits fallback)
// ────────────────────────────────────────────────────────────────────────────
void StartSearch(Dictionary<string, int> go)
{
    searchCts?.Cancel();
    searchCts = new();

    // 1) exact movetime if provided
    if (go.TryGetValue("movetime", out int mv))
    {
        Console.WriteLine($"info string movetime={mv}");
        searchTask = Task.Run(() =>
        {
            string best = engine!.GetBestMoveByTime(mv - 50);
            WriteLine($"bestmove {best}");
        }, searchCts.Token);
        return;
    }

    // 2) wtime/btime → derive per-move ms budget
    if (go.TryGetValue("wtime", out int wt) &&
        go.TryGetValue("btime", out int bt))
    {
        int t         = engine!.Board.WhiteToMove ? wt : bt;
        int inc       = engine.Board.WhiteToMove
                          ? go.GetValueOrDefault("winc")
                          : go.GetValueOrDefault("binc");
        int movesToGo = go.GetValueOrDefault("movestogo", 30);
        int spendMs   = (int)(t / (movesToGo + 2.5) + inc * 0.8);

        // leave a small safety margin
        int timeMs = Math.Max(1, spendMs - 50);

        Console.WriteLine($"info string per-move time={timeMs}ms");
        searchTask = Task.Run(() =>
        {
            string best = engine!.GetBestMoveByTime(timeMs);
            WriteLine($"bestmove {best}");
        }, searchCts.Token);
        return;
    }

    // 3) fallback to fixed visits
    int visits = go.TryGetValue("visits", out int v) ? v : opts.VisitLimit;
    visits = Math.Min(visits, opts.VisitLimit);
    Console.WriteLine($"info string visits={visits}");

    searchTask = Task.Run(() =>
    {
        string best = engine!.GetBestMove(visits);
        WriteLine($"bestmove {best}");
    }, searchCts.Token);
}


void StopSearch()
{
    if (searchTask == null) return;
    searchCts!.Cancel();
    searchTask.Wait();
    searchTask = null;
}

// ────────────────────────────────────────────────────────────────────────────
// Main UCI loop
// ────────────────────────────────────────────────────────────────────────────
// right after you do: EngineOptions opts = new() { Backend = GpuBackend.DirectML };
WriteLine("id name NisaTheCat");
WriteLine("id author Lander Compton");

// reflect the actual defaults from opts:
WriteLine($"option name Threads     type spin   default {opts.Threads}    min 1  max {Environment.ProcessorCount * 2}");
WriteLine($"option name UseGPU      type check  default {(opts.Backend != GpuBackend.None).ToString().ToLowerInvariant()}");
WriteLine($"option name CPuct       type spin   default {(int)(opts.Cpuct * 10)}   min 1  max 100");
WriteLine($"option name VisitLimit  type spin   default {opts.VisitLimit}  min 1  max 20000");

WriteLine("uciok");

while (true)
{
    string? line = Console.ReadLine();
    if (line == null) break;
    line = line.Trim();

    switch (line)
    {
        case "quit":
            StopSearch();
            goto Exit;

        case "uci":
            WriteLine("uciok");
            continue;

        case "isready":
            WriteLine("readyok");
            continue;

        case "ucinewgame":
            RecreateEngine();
            continue;
    }

    if (line.StartsWith("setoption", StringComparison.Ordinal))
    {
        var tok  = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int vidx = Array.IndexOf(tok, "value");
        string name = string.Join(' ', tok[2..vidx]);
        string val  = tok[vidx + 1];

        switch (name.ToLowerInvariant())
        {
            case "threads":
                opts = opts with { Threads = int.Parse(val) };
                RecreateEngine();
                break;

            case "usegpu":
                opts = opts with
                {
                    Backend = val == "true"
                              ? GpuBackend.DirectML
                              : GpuBackend.None
                };
                RecreateEngine();
                break;

            case "cpuct":
                opts = opts with { Cpuct = int.Parse(val) / 10f };
                RecreateEngine();
                break;

            case "visitlimit":
                opts = opts with { VisitLimit = int.Parse(val) };
                break;
        }
        continue;
    }

    if (line.StartsWith("position", StringComparison.Ordinal))
    {
        SetPosition(line);
        continue;
    }

    if (line.StartsWith("go", StringComparison.Ordinal))
    {
        StopSearch();
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var goDict = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 1; i < parts.Length - 1; i += 2)
            goDict[parts[i]] = int.Parse(parts[i + 1], CultureInfo.InvariantCulture);
        StartSearch(goDict);
        continue;
    }

    if (line == "stop" || line == "ponderhit")
    {
        StopSearch();
        continue;
    }

    // ignore unknown commands
}

Exit:
engine?.Dispose();
