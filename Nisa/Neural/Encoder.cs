using Microsoft.ML.OnnxRuntime.Tensors;
using Nisa.Chess;
using System;

namespace Nisa.Neural
{
    internal static class Encoder
    {
        /// <summary>
        /// Fills the ONNX input tensor with board features including position history.
        /// Modern Lc0 networks expect:
        /// - 8 positions × 13 planes = 104 planes (P1-P6 pieces × 2 colors + repetitions)
        /// - Plus auxiliary planes (castling, rule50, etc.)
        /// Total: 112 planes minimum
        /// </summary>
        public static void Encode(PositionHistory history, DenseTensor<float> dst)
        {
            int planes = dst.Dimensions[1];
            Board current = history.GetCurrent();
            bool whiteToMove = current.WhiteToMove;

            // Clear the tensor first (important!)
            for (int p = 0; p < planes; p++)
                for (int r = 0; r < 8; r++)
                    for (int f = 0; f < 8; f++)
                        dst[0, p, r, f] = 0f;

            // ------------------------------------------------------------------
            // 1) Historic positions: 8 × 13 planes = 104 planes
            //    For each position T-0 through T-7:
            //    - 6 planes for our pieces (P,N,B,R,Q,K)
            //    - 6 planes for opponent pieces
            //    - 1 plane for repetition count
            // ------------------------------------------------------------------
            int planeIdx = 0;
            
            for (int t = 0; t < 8; t++)
            {
                Board? pos = history.GetHistoryPosition(t);
                
                if (pos != null && pos.OccupancyAll != 0) // Valid position
                {
                    // Write piece planes (12 planes per position)
                    WritePiecesAt(pos, dst, planeIdx, whiteToMove);
                    planeIdx += 12;
                    
                    // Write repetition plane
                    if (t == 0) // Only for current position
                    {
                        int reps = history.CountRepetitions(pos);
                        if (reps > 0)
                        {
                            float repValue = Math.Min(reps, 3) / 3.0f; // Normalize to [0,1]
                            FillPlane(dst, planeIdx, repValue);
                        }
                    }
                    planeIdx++;
                }
                else
                {
                    // Position doesn't exist (early in game) - skip 13 planes
                    planeIdx += 13;
                }
            }

            // ------------------------------------------------------------------
            // 2) Auxiliary planes (104 onwards)
            // ------------------------------------------------------------------
            // Plane 104-111: Castling rights (4), Rule50 progress (1), 
            //                Reserved (3) for older nets
            if (planes > 104)
            {
                WriteCastlingRights(current, dst, 104, whiteToMove);
            }
            
            if (planes > 108)
            {
                WriteRule50(current, dst, 108);
            }

            // Plane 112: Color (who is to move)
            if (planes > 112)
            {
                WriteSideToMove(current, dst, 112);
            }
            
            // Planes 113-116: Castling rights again (for newer nets)
            if (planes > 113)
            {
                WriteCastlingRights(current, dst, 113, whiteToMove);
            }
            
            // Plane 117: Rule50 count
            if (planes > 117)
            {
                WriteRule50(current, dst, 117);
            }
            
            // Plane 118: All ones (legacy)
            if (planes > 118)
            {
                FillPlane(dst, 118, 1.0f);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Helper: Write pieces for one position in history
        // ──────────────────────────────────────────────────────────────────
        private static void WritePiecesAt(Board b, DenseTensor<float> dst, 
                                         int basePlane, bool currentWhiteToMove)
        {
            bool flip = !currentWhiteToMove; // Flip board for black's perspective
            
            // Map pieces according to current player's perspective
            // From current player's view: planes 0-5 are "our" pieces, 6-11 are "their" pieces
            int[] pieceMap;
            
            if (currentWhiteToMove)
            {
                // White to move: white pieces in planes 0-5, black in 6-11
                pieceMap = new[] { 
                    Board.WP, Board.WN, Board.WB, Board.WR, Board.WQ, Board.WK,
                    Board.BP, Board.BN, Board.BB, Board.BR, Board.BQ, Board.BK 
                };
            }
            else
            {
                // Black to move: black pieces in planes 0-5, white in 6-11
                pieceMap = new[] { 
                    Board.BP, Board.BN, Board.BB, Board.BR, Board.BQ, Board.BK,
                    Board.WP, Board.WN, Board.WB, Board.WR, Board.WQ, Board.WK 
                };
            }

            for (int i = 0; i < 12; i++)
            {
                FillBitboard(dst, basePlane + i, b[pieceMap[i]], flip);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Original helper methods updated
        // ──────────────────────────────────────────────────────────────────
        private static void FillBitboard(DenseTensor<float> t, int plane, ulong bb, bool flip)
        {
            while (bb != 0)
            {
                int sq = BitOps.PopLsb(ref bb);
                int r = sq >> 3, f = sq & 7;
                if (flip) 
                { 
                    r = 7 - r; 
                    f = 7 - f; 
                }
                t[0, plane, r, f] = 1f;
            }
        }

        private static void FillPlane(DenseTensor<float> dst, int plane, float value)
        {
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                    dst[0, plane, r, f] = value;
        }

        private static void WriteSideToMove(Board b, DenseTensor<float> dst, int plane)
        {
            float value = b.WhiteToMove ? 1f : 0f;
            FillPlane(dst, plane, value);
        }

        private static void WriteCastlingRights(Board b, DenseTensor<float> dst, int basePlane, bool currentWhiteToMove)
        {
            // Order depends on perspective
            bool[] rights;
            
            if (currentWhiteToMove)
            {
                rights = new[] { b.WCK, b.WCQ, b.BCK, b.BCQ };
            }
            else
            {
                // From black's perspective, their castling rights come first
                rights = new[] { b.BCK, b.BCQ, b.WCK, b.WCQ };
            }
            
            for (int i = 0; i < 4; i++)
            {
                if (basePlane + i < dst.Dimensions[1])
                {
                    FillPlane(dst, basePlane + i, rights[i] ? 1f : 0f);
                }
            }
        }

        private static void WriteRule50(Board b, DenseTensor<float> dst, int plane)
        {
            // Normalize to [0, 1] with cap at 100 (draw at 100 half-moves)
            float value = Math.Min(b.HalfmoveClock / 99.0f, 1.0f);
            FillPlane(dst, plane, value);
        }
    }
}