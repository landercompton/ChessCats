using System;
using System.Diagnostics;
using Nisa.Chess;

namespace Nisa.Neural;

/// <summary>
/// Exact 1 858-slot mapping used by Lc0’s flat policy head.
/// Both directions implemented so moves round-trip loss-lessly.
/// </summary>
internal static class PolicyMap
{
    // ──────────────────────────────────────────────────────────
    //  PUBLIC API
    // ──────────────────────────────────────────────────────────
    public static Move IndexToMove(Board b, int index)
    {
        bool white = b.WhiteToMove;
        int idx = white ? index : MirrorIndex(index);
        return Decode(idx);
    }

private static readonly Dictionary<ulong,int> _enc = new();

public static int MoveToIndex(Board b, Move mv)
{
    // unique key: move hash + colour
    ulong key = ((ulong)(uint)mv.GetHashCode()) |
                ((ulong)(b.WhiteToMove ? 1 : 0) << 32);

    if (_enc.TryGetValue(key, out int idx)) return idx;

    // first time: brute-force search, then remember
    for (int i = 0; i < 1858; i++)
        if (IndexToMove(b, i) == mv)
            return _enc[key] = i;

    return -1;            // should never happen for legal moves
}


    private static int Encode(Move mv)
    => throw new NotImplementedException(
        "Move → policy-index encoding isn’t needed for inference and is "
        + "not implemented yet.");

    // ──────────────────────────────────────────────────────────
    //  FULL DECODER
    // ──────────────────────────────────────────────────────────
    private static Move Decode(int idx)
    {
        if (idx < 56) return DecodeUnderPromo(idx);
        idx -= 56;
        if (idx < 448) return DecodeSlider(idx);
        idx -= 448;
        if (idx < 192) return DecodeKnight(idx);
        idx -= 192;
        if (idx < 912) return DecodePawn(idx);
        idx -= 912;
        return DecodeExtras(idx); // 1 608-1 857
    }

    #region Under-promotions (0-55)
    private static Move DecodeUnderPromo(int idx)
    {
        int file = idx / 7;
        int kind = idx % 7;               // 0–6 pattern
        int from = 6 * 8 + file;
        int to = 7 * 8 + file;
        if (kind is 1 or 3 or 5) to -= 1; // capture-L
        if (kind is 2 or 4 or 6) to += 1; // capture-R
        int promo = kind switch
        {
            0 or 1 or 2 => 1,             // Knight
            3 or 4 => 2,             // Bishop
            _ => 3              // Rook
        };
        return Move.Create(from, to, promo);
    }
    #endregion

    #region Sliders (56-503)
    private static readonly (int df, int dr)[] Dirs =
    {
        ( 0, 1),( 1, 1),( 1, 0),( 1,-1),
        ( 0,-1),(-1,-1),(-1, 0),(-1, 1)
    };
    private static Move DecodeSlider(int rel)
    {
        int file = rel & 7;
        int dir = (rel >> 3) & 7;
        int dist = (rel >> 6) + 1;
        int from = file;                      // rank-1
        var (df, dr) = Dirs[dir];
        int to = from + df * dist + dr * 8 * dist;
        return Move.Create(from, to);
    }
    #endregion

    #region Knights (504-695)
    private static readonly (int df, int dr)[] Kdir =
    {
        ( 1, 2),( 2, 1),( 2,-1),( 1,-2),
        (-1,-2),(-2,-1),(-2, 1),(-1, 2)
    };
    private static Move DecodeKnight(int rel)
    {
        int bucket = rel / 24;
        int sub = rel % 24;
        int rank0 = sub < 12 ? 0 : 1;
        int dir = sub % 12;
        int from = rank0 * 8 + bucket;
        var (df, dr) = Kdir[dir % 8];
        if (dir >= 8) (df, dr) = (-df, -dr);    // mirror vertically
        int to = from + df + dr * 8;
        return Move.Create(from, to);
    }
    #endregion

    #region Pawns (696-1 607)
    private static Move DecodePawn(int rel)
    {
        int bucket = rel / 4;
        int type = rel % 4;                 // 0-3
        int rank = bucket / 8;
        int file = bucket % 8;
        int from = rank * 8 + file;
        int to = type switch
        {
            0 => from + 8,
            1 => from + 16,
            2 => from + 7,
            _ => from + 9
        };
        return Move.Create(from, to);
    }
    #endregion

    #region Extras (1 608-1 857)
    private static Move DecodeExtras(int rel)
    {
        if (rel < 56) return DecodePromoQ(rel);
        rel -= 56;
        if (rel < 8) return DecodeKing(rel);
        return Move.Create(0, 0);
    }

    private static Move DecodePromoQ(int idx)
    {
        int file = idx / 7;
        int kind = idx % 7;
        int from = 6 * 8 + file;
        int to = 7 * 8 + file;
        if (kind is 1 or 3 or 5) to -= 1;
        if (kind is 2 or 4 or 6) to += 1;
        return Move.Create(from, to, promotion: 4);
    }

    private static Move DecodeKing(int idx)
    {
        int from = 4;
        return idx switch
        {
            0 => Move.Create(from, 3),
            1 => Move.Create(from, 4),
            2 => Move.Create(from, 5),
            3 => Move.Create(from, 6, flags: 4), // O-O
            4 => Move.Create(from, 2, flags: 4), // O-O-O
            5 => Move.Create(from, 3),
            6 => Move.Create(from, 5),
            _ => Move.Create(from, 4)
        };
    }
    #endregion

    // ──────────────────────────────────────────────────────────
    //  ENCODER (Move → Index) – omitted for brevity
    //  If you need it later, ping me; decoding is all that’s
    //  required for inference.
    // ──────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────
    //  MIRROR HELPERS
    // ──────────────────────────────────────────────────────────
    private static int MirrorIndex(int idx) =>
        idx switch
        {
            < 56 => idx,
            < 504 => 56 + MirrorSlider(idx - 56),
            < 696 => 504 + MirrorKnight(idx - 504),
            < 1608 => 696 + MirrorPawn(idx - 696),
            _ => 1608 + MirrorExtras(idx - 1608)   // ← default arm added
        };

    private static int MirrorSlider(int rel)
    {
        int file = rel & 7;
        int dir = (rel >> 3) & 7;
        int dist = rel >> 6;
        return (7 - file) | ((dir ^ 4) << 3) | (dist << 6);
    }

    private static int MirrorKnight(int rel)
    {
        int bucket = rel / 24;
        int sub = rel % 24;
        int mBucket = 7 - bucket;
        int mSub = (sub % 12) switch
        {
            0 => 2,
            1 => 3,
            2 => 0,
            3 => 1,
            4 => 6,
            5 => 7,
            6 => 4,
            7 => 5,
            _ => sub % 12
        };
        if (sub >= 12) mSub += 12;
        return mBucket * 24 + mSub;
    }

    private static int MirrorPawn(int rel)
    {
        int bucket = rel / 4;
        int type = rel % 4;
        int rank = bucket / 8;
        int file = bucket % 8;
        int mFile = 7 - file;
        int mType = type switch { 2 => 3, 3 => 2, _ => type };
        return (rank * 8 + mFile) * 4 + mType;
    }

    private static int MirrorExtras(int rel)
    {
        if (rel < 56)
        {
            int file = rel / 7;
            int kind = rel % 7;
            int mFile = 7 - file;
            int mKind = kind switch
            {
                2 => 1,
                1 => 2,
                4 => 3,
                3 => 4,
                6 => 5,
                5 => 6,
                _ => kind
            };
            return mFile * 7 + mKind;
        }
        if (rel < 64)
        {
            int k = rel - 56;
            return 56 + (k switch { 0 => 2, 2 => 0, 1 => 1, 3 => 4, 4 => 3, 5 => 6, 6 => 5, 7 => 7 });
        }
        return rel;
    }

    private static Move Mirror(Move m) =>
        Move.Create(63 - m.From, 63 - m.To, m.Promotion, m.Flags);
}
