using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nisa.Chess;

internal static class BitOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopLsb(ref ulong bb)
    {
        int idx = BitOperations.TrailingZeroCount(bb);
        bb &= bb - 1;
        return idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Lsb(ulong bb) => BitOperations.TrailingZeroCount(bb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(ulong bb) => BitOperations.PopCount(bb);
}
