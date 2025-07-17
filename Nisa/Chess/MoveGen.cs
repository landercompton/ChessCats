using System;
using System.Collections.Generic;

namespace Nisa.Chess;

/// <summary>
/// Pseudo-legal move generator valid to perft depth 3 (8 902 nodes in the
/// standard start position).  The code is compact—not magic-bitboard speed,
/// but perfectly adequate for testing the neural network plumbing.
/// </summary>
public static class MoveGen
{
    private static readonly ulong[] KnightAtt  = new ulong[64];
    private static readonly ulong[] KingAtt    = new ulong[64];
    private static readonly ulong[][] RookRays = new ulong[64][];   // 4 dirs each
    private static readonly ulong[][] BishRays = new ulong[64][];   // 4 dirs each

    //------------------------------------------------------------
    //  Static constructor – pre-compute attack tables & rays
    //------------------------------------------------------------
    static MoveGen()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            int f = sq & 7;               // file (0=A … 7=H)
            int r = sq >> 3;              // rank (0=1 … 7=8)

            KnightAtt[sq] =
                Mask(f + 1, r + 2) | Mask(f + 2, r + 1) | Mask(f + 2, r - 1) |
                Mask(f + 1, r - 2) | Mask(f - 1, r - 2) | Mask(f - 2, r - 1) |
                Mask(f - 2, r + 1) | Mask(f - 1, r + 2);

            KingAtt[sq] =
                Mask(f + 1, r    ) | Mask(f + 1, r + 1) | Mask(f    , r + 1) |
                Mask(f - 1, r + 1) | Mask(f - 1, r    ) | Mask(f - 1, r - 1) |
                Mask(f    , r - 1) | Mask(f + 1, r - 1);

            RookRays[sq] = BuildRaySet(sq, new[] { ( 1, 0), (-1, 0), (0, 1), (0,-1) });
            BishRays[sq] = BuildRaySet(sq, new[] { ( 1, 1), ( 1,-1), (-1, 1), (-1,-1) });
        }

        //--------------------------------------------------------
        // Local helpers used only while building tables
        //--------------------------------------------------------
        static ulong Mask(int file, int rank)
            => (file is < 0 or > 7 || rank is < 0 or > 7)
               ? 0
               : 1UL << (rank * 8 + file);

        static ulong[] BuildRaySet(int sq, (int df,int dr)[] dirs)
        {
            int f0 = sq & 7, r0 = sq >> 3;
            var rays = new ulong[dirs.Length];

            for (int d = 0; d < dirs.Length; d++)
            {
                int f = f0 + dirs[d].df;
                int r = r0 + dirs[d].dr;

                while (f is >= 0 and <= 7 && r is >= 0 and <= 7)
                {
                    rays[d] |= 1UL << (r * 8 + f);
                    f += dirs[d].df;
                    r += dirs[d].dr;
                }
            }
            return rays;
        }
    }

    //------------------------------------------------------------
    //  Public API
    //------------------------------------------------------------
    /// <summary>
    /// Generates **pseudo-legal** moves for the side to move.
    /// No check detection yet—adequate for neural-network guidance.
    /// </summary>
    public static List<Move> Generate(Board b)
    {
        var moves = new List<Move>(128);

        bool white = b.WhiteToMove;
        ulong own   = white ? b.OccupancyWhite : b.OccupancyBlack;
        ulong enemy = white ? b.OccupancyBlack : b.OccupancyWhite;

        int P = white ? Board.WP : Board.BP;
        int N = white ? Board.WN : Board.BN;
        int B = white ? Board.WB : Board.BB;
        int R = white ? Board.WR : Board.BR;
        int Q = white ? Board.WQ : Board.BQ;
        int K = white ? Board.WK : Board.BK;

        int forward    = white ?  8 : -8;
        int startRank  = white ?  1 :  6;
        int promoRank  = white ?  6 :  1;
        int epCaptureRank = white ? 4 : 3; // rank index (0-7) where EP capture originates

        //--------------------------------------------------------
        //  1) Pawns
        //--------------------------------------------------------
        ulong pawns = b[P];
        while (pawns != 0)
        {
            int from = BitOps.PopLsb(ref pawns);
            int fr   = from >> 3;
            int to   = from + forward;

            // Single push
            if (((b.OccupancyAll >> to) & 1) == 0)
            {
                if (fr == promoRank)
                    AddPromos(from, to, moves);
                else
                    moves.Add(Move.Create(from, to));

                // Double push
                if (fr == startRank &&
                    ((b.OccupancyAll >> (to + forward)) & 1) == 0)
                {
                    moves.Add(Move.Create(from, to + forward, flags: 1)); // flag 1 = double
                }
            }

            // Captures (including en-passant)
            foreach (int df in new[] { -1, 1 })
            {
                int tf = (from & 7) + df;
                if (tf is < 0 or > 7) continue;
                int t = to + df;

                // Regular capture
                if ((enemy >> t & 1) != 0)
                {
                    if (fr == promoRank)
                        AddPromos(from, t, moves);
                    else
                        moves.Add(Move.Create(from, t));
                }
                // En-passant
                else if (t == b.EpSq && fr == epCaptureRank)
                {
                    moves.Add(Move.Create(from, t, flags: 2)); // flag 2 = ep
                }
            }
        }

        //--------------------------------------------------------
        //  2) Knights
        //--------------------------------------------------------
        GenNonSlider(N, KnightAtt);

        //--------------------------------------------------------
        //  3) Bishops, Rooks, Queens (sliders)
        //--------------------------------------------------------
GenSlider(B, new[]{(1,1),(1,-1),(-1,1),(-1,-1)});   // bishops
GenSlider(R, new[]{(1,0),(-1,0),(0,1),(0,-1)});     // rooks
GenSlider(Q, new[]{(1,1),(1,-1),(-1,1),(-1,-1)});   // queen diagonals
GenSlider(Q, new[]{(1,0),(-1,0),(0,1),(0,-1)});     // queen orthogonals

        //--------------------------------------------------------
        //  4) King moves (plus basic castling emptiness test)
        //--------------------------------------------------------
        GenNonSlider(K, KingAtt);

        if (white)
        {
            if (b.WCK && (b.OccupancyAll & 0x60UL) == 0)      // e-g squares empty
                moves.Add(Move.Create(4, 6, flags: 4));       // flag 4 = castle
            if (b.WCQ && (b.OccupancyAll & 0x0EUL) == 0)      // b-d squares empty
                moves.Add(Move.Create(4, 2, flags: 4));
        }
        else
        {
            if (b.BCK && (b.OccupancyAll & (0x60UL << 56)) == 0)
                moves.Add(Move.Create(60, 62, flags: 4));
            if (b.BCQ && (b.OccupancyAll & (0x0EUL << 56)) == 0)
                moves.Add(Move.Create(60, 58, flags: 4));
        }

        return moves;

        //====================================================
        //  Local helpers
        //====================================================
        static void AddPromos(int from, int to, List<Move> list)
        {
            // Promotion piece codes: 1=n, 2=b, 3=r, 4=q
            for (int p = 1; p <= 4; p++)
                list.Add(Move.Create(from, to, promotion: p));
        }

        void GenNonSlider(int piece, ulong[] attTable)
        {
            ulong bb = b[piece];
            while (bb != 0)
            {
                int from = BitOps.PopLsb(ref bb);
                ulong targets = attTable[from] & ~own;
                while (targets != 0)
                {
                    int to = BitOps.PopLsb(ref targets);
                    moves.Add(Move.Create(from, to));
                }
            }
        }

void GenSlider(int piece, (int df,int dr)[] dirs)
{
    ulong bb = b[piece];
    while (bb != 0)
    {
        int from = BitOps.PopLsb(ref bb);
        int f0 = from & 7;
        int r0 = from >> 3;

        foreach (var (df, dr) in dirs)
        {
            int f = f0 + df;
            int r = r0 + dr;

            while (f is >= 0 and <= 7 && r is >= 0 and <= 7)
            {
                int to = r * 8 + f;

                // own piece blocks; stop and don’t add
                if (((own >> to) & 1) != 0) break;

                moves.Add(Move.Create(from, to));

                // enemy piece captured; square included, then stop
                if (((enemy >> to) & 1) != 0) break;

                f += df;
                r += dr;
            }
        }
    }
}

    }
}
