using System.Collections.Generic;

namespace Nisa.Chess;

/// <summary>Pure helpers that turn pseudo-moves into fully legal moves.</summary>
internal static class Legality
{
    /// <returns>true if <paramref name="m"/> leaves side-to-move's king safe.</returns>
    public static bool IsLegal(Board b, Move m)
    {
        var u = b.MakeAndReturnUndo(m);
        bool ok = !b.SquareAttackedBy(b.WhiteToMove, b.KingSquare(!b.WhiteToMove));
        b.Unmake(u);
        return ok;
    }

    /// <summary>Enumerates only king-safe moves.</summary>
    public static IEnumerable<Move> GenerateLegal(Board b)
    {
        foreach (var m in MoveGen.Generate(b))            // pseudo-legal
            if (IsLegal(b, m))
                yield return m;
    }
}
