

using System;


namespace Nisa.Chess
{
    internal static class Zobrist
    {
        // [piece 0-11][square 0-63]
        private static readonly ulong[,] PieceKeys = new ulong[12, 64];
        private static readonly ulong SideKey;
        private static readonly ulong[] CastleKeys = new ulong[4]; // WK,WQ,BK,BQ
        private static readonly ulong[] EpFileKeys = new ulong[8]; // file a-h

        static Zobrist()
        {
            var rng = new Random(20250714);        // deterministic seed
            Span<byte> buf = stackalloc byte[8];

            ulong NextRand()
            {
                Span<byte> buf = stackalloc byte[8];
                rng.NextBytes(buf);
                return BitConverter.ToUInt64(buf);
            }

            for (int p = 0; p < 12; p++)
                for (int sq = 0; sq < 64; sq++)
                    PieceKeys[p, sq] = NextRand();

            for (int i = 0; i < 4; i++) CastleKeys[i] = NextRand();
            for (int i = 0; i < 8; i++) EpFileKeys[i] = NextRand();
            SideKey = NextRand();
        }

        /// <summary>Compute 64-bit hash of the current board.</summary>
        public static ulong Hash(Board b)
        {
            ulong h = 0;

            for (int p = 0; p < 12; p++)
            {
                ulong bb = b[p];
                while (bb != 0)
                {
                    int sq = BitOps.PopLsb(ref bb);
                    h ^= PieceKeys[p, sq];
                }
            }

            if (!b.WhiteToMove) h ^= SideKey;
            if (b.WCK) h ^= CastleKeys[0];
            if (b.WCQ) h ^= CastleKeys[1];
            if (b.BCK) h ^= CastleKeys[2];
            if (b.BCQ) h ^= CastleKeys[3];
            if (b.EpSq != -1) h ^= EpFileKeys[b.EpSq & 7];

            return h;
        }
    }
}
