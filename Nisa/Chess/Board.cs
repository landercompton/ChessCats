using System;
using System.Numerics;

namespace Nisa.Chess
{
    /// <summary>
    /// Bit-board position plus state flags.  Includes full Make / Unmake.
    /// </summary>
    public sealed class Board
    {
        // piece indices (white 0-5, black 6-11)
        public const int WP = 0, WN = 1, WB = 2, WR = 3, WQ = 4, WK = 5,
                         BP = 6, BN = 7, BB = 8, BR = 9, BQ = 10, BK = 11;

        // ---------------- position data ----------------
        private readonly ulong[] _bb = new ulong[12];

        public bool WhiteToMove { get; set; } = true;
        public bool WCK = true, WCQ = true, BCK = true, BCQ = true;
        public int EpSq = -1;                 // 0-63 or –1
        public int HalfmoveClock;
        public int Fullmove = 1;

        // --------------- access helpers ----------------
        public ref ulong this[int p] => ref _bb[p];

        public ulong OccupancyWhite => _bb[WP] | _bb[WN] | _bb[WB] | _bb[WR] | _bb[WQ] | _bb[WK];
        public ulong OccupancyBlack => _bb[BP] | _bb[BN] | _bb[BB] | _bb[BR] | _bb[BQ] | _bb[BK];
        public ulong OccupancyAll => OccupancyWhite | OccupancyBlack;

        public Board() { }
        // ---------------- copy-constructor -------------
        public Board(Board src)
        {
            Array.Copy(src._bb, _bb, 12);

            WhiteToMove = src.WhiteToMove;
            WCK = src.WCK; WCQ = src.WCQ;
            BCK = src.BCK; BCQ = src.BCQ;
            EpSq = src.EpSq;
            HalfmoveClock = src.HalfmoveClock;
            Fullmove = src.Fullmove;
        }

        // --------------- convenience -------------------
        public void Clear()
        {
            Array.Clear(_bb);
            WhiteToMove = true;
            WCK = WCQ = BCK = BCQ = true;
            EpSq = -1; HalfmoveClock = 0; Fullmove = 1;
        }

        public Undo MakeAndReturnUndo(Move mv) => Make(mv);

        // ---------------- Undo struct ------------------
        public readonly struct Undo
        {
            public readonly ulong[] Bb;
            public readonly bool WhiteToMove;
            public readonly bool WCK, WCQ, BCK, BCQ;
            public readonly int EpSq, HalfmoveClock, Fullmove;

            public Undo(Board src)
            {
                Bb = (ulong[])src._bb.Clone();
                WhiteToMove = src.WhiteToMove;
                WCK = src.WCK; WCQ = src.WCQ; BCK = src.BCK; BCQ = src.BCQ;
                EpSq = src.EpSq;
                HalfmoveClock = src.HalfmoveClock;
                Fullmove = src.Fullmove;
            }

            public void Restore(Board dst)
            {
                for (int i = 0; i < 12; i++) dst._bb[i] = Bb[i];
                dst.WhiteToMove = WhiteToMove;
                dst.WCK = WCK; dst.WCQ = WCQ; dst.BCK = BCK; dst.BCQ = BCQ;
                dst.EpSq = EpSq;
                dst.HalfmoveClock = HalfmoveClock;
                dst.Fullmove = Fullmove;
            }
        }

        // ---------------- Make / Unmake ----------------
        public Undo Make(Move mv)
        {
            var undo = new Undo(this);

            int from = mv.From;
            int to = mv.To;
            bool white = WhiteToMove;
            int dir = white ? 1 : -1;

            int movingPiece = FindPieceAt(from);
            int capturedPiece = FindPieceAt(to);        // –1 if empty

            // en-passant capture
            if ((mv.Flags & 2) != 0)
            {
                int capSq = to - 8 * dir;
                capturedPiece = FindPieceAt(capSq);
                if (capturedPiece != -1) this[capturedPiece] &= ~(1UL << capSq);
            }

            // move piece
            this[movingPiece] &= ~(1UL << from);
            this[movingPiece] |= 1UL << to;

            // promotion
            if (mv.Promotion != 0)
            {
                this[movingPiece] &= ~(1UL << to);
                int baseIdx = white ? 0 : 6;
                int promo = mv.Promotion switch
                {
                    1 => baseIdx + WN,
                    2 => baseIdx + WB,
                    3 => baseIdx + WR,
                    4 => baseIdx + WQ,
                    _ => baseIdx + WQ
                };
                this[promo] |= 1UL << to;
            }

            // normal capture
            if (capturedPiece != -1)
                this[capturedPiece] &= ~(1UL << to);

            // castling rook move
            if ((mv.Flags & 4) != 0)
            {
                if (to == 6) MoveRook(7, 5, WR);
                if (to == 2) MoveRook(0, 3, WR);
                if (to == 62) MoveRook(63, 61, BR);
                if (to == 58) MoveRook(56, 59, BR);
            }

            UpdateCastlingRights(movingPiece, from, to, capturedPiece, to);

            EpSq = -1;
            if (movingPiece == (white ? WP : BP) && Math.Abs(to - from) == 16)
                EpSq = from + 8 * dir;

            HalfmoveClock = (movingPiece == WP || movingPiece == BP || capturedPiece != -1)
                          ? 0 : HalfmoveClock + 1;

            WhiteToMove = !WhiteToMove;
            if (WhiteToMove) Fullmove++;

            return undo;
        }

        public void Unmake(in Undo undo) => undo.Restore(this);

        // ---------------- helpers ----------------------
        private int FindPieceAt(int sq)
        {
            ulong mask = 1UL << sq;
            for (int p = 0; p < 12; p++)
                if ((_bb[p] & mask) != 0) return p;
            return -1;
        }

        private void MoveRook(int from, int to, int rookIndex)
        {
            this[rookIndex] &= ~(1UL << from);
            this[rookIndex] |= 1UL << to;
        }

        private void UpdateCastlingRights(int moving, int from, int to, int capt, int capSq)
        {
            if (moving == WK) { WCK = WCQ = false; }
            if (moving == BK) { BCK = BCQ = false; }

            if (moving == WR)
            {
                if (from == 0) WCQ = false;
                if (from == 7) WCK = false;
            }
            if (moving == BR)
            {
                if (from == 56) BCQ = false;
                if (from == 63) BCK = false;
            }

            if (capt == WR)
            {
                if (capSq == 0) WCQ = false;
                if (capSq == 7) WCK = false;
            }
            if (capt == BR)
            {
                if (capSq == 56) BCQ = false;
                if (capSq == 63) BCK = false;
            }
        }

        // fastest king-lookup: trailing-zero on the single-bit king board
        public int KingSquare(bool white) =>
            BitOperations.TrailingZeroCount(_bb[white ? WK : BK]);

        // naive attack detector: iterate enemy piece lists (replace with your own fast mask if you have one already)
        public bool SquareAttackedBy(bool attackerWhite, int sq)
        {
            // very quick & dirty: reuse MoveGen to see if any enemy move hits 'sq'
            // (good enough for legality; you can optimise later)
            bool saveSide = WhiteToMove;
            WhiteToMove = attackerWhite;
            foreach (var m in MoveGen.Generate(this))
                if (m.To == sq) { WhiteToMove = saveSide; return true; }
            WhiteToMove = saveSide;
            return false;
        }
    }
}
