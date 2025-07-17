using Microsoft.ML.OnnxRuntime.Tensors;
using Nisa.Chess;

namespace Nisa.Neural;

internal static class Encoder
{
    /// <summary>
    /// Fills the ONNX input tensor with board features.
    /// Supports 112-plane (historic) and 119-plane (current) nets.
    /// Extra planes remain zero if the net expects more.
    /// </summary>
    public static void Encode(Board b, DenseTensor<float> dst)
    {
        int planes = dst.Dimensions[1];

        // ------------------------------------------------------------------
        // 1) Piece planes for *current* position  (16 planes)
        //    Plane order (Lc0, white perspective):
        //      0-5  :   W  P,N,B,R,Q,K
        //      6-11 :   B  P,N,B,R,Q,K
        //      12-15:   unused in modern nets (reserved)
        // ------------------------------------------------------------------
        WritePieces(b, dst, b.WhiteToMove);

        // ------------------------------------------------------------------
        // 2) Extra 7 planes (if the net expects them)
        // ------------------------------------------------------------------
        if (planes >= 113) WriteSideToMove(b, dst, 112);
        if (planes >= 114) WriteCastling(b, dst, 113);
        if (planes >= 118) WriteHalfmoveClock(b, dst, 117);
        // plane 118 (repetition) stays 0 for now — not tracked yet
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────
    private static void WritePieces(Board b, DenseTensor<float> dst, bool whiteToMove)
    {
        static void SetSquare(DenseTensor<float> t, int plane, int sq, bool flip)
        {
            int r = sq >> 3, f = sq & 7;
            if (flip) { r = 7 - r; f = 7 - f; }
            t[0, plane, r, f] = 1f;
        }

        bool flip = !whiteToMove;     // rotate board 180° for Black

        // White pieces
        FillBitboard(dst, 0,  b[Board.WP], flip);
        FillBitboard(dst, 1,  b[Board.WN], flip);
        FillBitboard(dst, 2,  b[Board.WB], flip);
        FillBitboard(dst, 3,  b[Board.WR], flip);
        FillBitboard(dst, 4,  b[Board.WQ], flip);
        FillBitboard(dst, 5,  b[Board.WK], flip);

        // Black pieces
        FillBitboard(dst, 6,  b[Board.BP], flip);
        FillBitboard(dst, 7,  b[Board.BN], flip);
        FillBitboard(dst, 8,  b[Board.BB], flip);
        FillBitboard(dst, 9,  b[Board.BR], flip);
        FillBitboard(dst, 10, b[Board.BQ], flip);
        FillBitboard(dst, 11, b[Board.BK], flip);

        static void FillBitboard(DenseTensor<float> t, int plane, ulong bb, bool flip)
        {
            while (bb != 0)
            {
                int sq = BitOps.PopLsb(ref bb);
                int r = sq >> 3, f = sq & 7;
                if (flip) { r = 7 - r; f = 7 - f; }
                t[0, plane, r, f] = 1f;
            }
        }
    }

    private static void WriteSideToMove(Board b, DenseTensor<float> dst, int plane)
    {
        float value = b.WhiteToMove ? 1f : 0f;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                dst[0, plane, r, f] = value;
    }

    private static void WriteCastling(Board b, DenseTensor<float> dst, int basePlane)
    {
        bool[] rights = {
            b.WCK,  // kingside white
            b.WCQ,  // queenside white
            b.BCK,  // kingside black
            b.BCQ   // queenside black
        };
        for (int p = 0; p < 4; p++)
        {
            float v = rights[p] ? 1f : 0f;
            int plane = basePlane + p;
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                    dst[0, plane, r, f] = v;
        }
    }

    private static void WriteHalfmoveClock(Board b, DenseTensor<float> dst, int plane)
    {
        float v = b.HalfmoveClock / 100f;          // normalise 0-1
        if (v > 1f) v = 1f;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                dst[0, plane, r, f] = v;
    }
}
