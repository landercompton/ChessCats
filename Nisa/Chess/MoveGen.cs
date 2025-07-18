using System;
using System.Collections.Generic;

namespace Nisa.Chess
{
    /// <summary>
    /// Improved pseudo-legal move generator with efficient attack detection.
    /// </summary>
    public static class MoveGen
    {
        // ────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Generates pseudo-legal moves for the side to move.
        /// </summary>
        public static List<Move> Generate(Board b)
        {
            var moves = new List<Move>(128);
            bool white = b.WhiteToMove;
            
            GeneratePawnMoves(b, moves, white);
            GenerateKnightMoves(b, moves, white);
            GenerateBishopMoves(b, moves, white);
            GenerateRookMoves(b, moves, white);
            GenerateQueenMoves(b, moves, white);
            GenerateKingMoves(b, moves, white);
            GenerateCastlingMoves(b, moves, white);
            
            return moves;
        }
        
        /// <summary>
        /// Generates only capture moves (for quiescence search).
        /// </summary>
        public static List<Move> GenerateCaptures(Board b)
        {
            var moves = new List<Move>(32);
            bool white = b.WhiteToMove;
            ulong enemy = white ? b.OccupancyBlack : b.OccupancyWhite;
            
            GeneratePawnCaptures(b, moves, white, enemy);
            GeneratePieceCaptures(b, moves, white, enemy);
            
            return moves;
        }
        
        // ────────────────────────────────────────────────────────────────
        // Pawn move generation
        // ────────────────────────────────────────────────────────────────
        
        private static void GeneratePawnMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WP : Board.BP;
            ulong pawns = b[piece];
            ulong empty = ~b.OccupancyAll;
            ulong enemy = white ? b.OccupancyBlack : b.OccupancyWhite;
            
            int forward = white ? 8 : -8;
            int startRank = white ? 1 : 6;
            int promoRank = white ? 6 : 1;
            
            while (pawns != 0)
            {
                int from = BitOps.PopLsb(ref pawns);
                int rank = from / 8;
                int file = from % 8;
                
                // Single push
                int to = from + forward;
                if (to >= 0 && to < 64 && ((empty >> to) & 1) != 0)
                {
                    if (rank == promoRank)
                    {
                        AddPromotions(moves, from, to);
                    }
                    else
                    {
                        moves.Add(Move.Create(from, to));
                        
                        // Double push
                        if (rank == startRank)
                        {
                            int to2 = to + forward;
                            if (((empty >> to2) & 1) != 0)
                            {
                                moves.Add(Move.Create(from, to2, 0, 1)); // flag 1 = double push
                            }
                        }
                    }
                }
                
                // Captures
                foreach (int df in new[] { -1, 1 })
                {
                    if (file + df < 0 || file + df > 7) continue;
                    
                    int captureTo = from + forward + df;
                    if (captureTo < 0 || captureTo >= 64) continue;
                    
                    // Regular capture
                    if (((enemy >> captureTo) & 1) != 0)
                    {
                        if (rank == promoRank)
                            AddPromotions(moves, from, captureTo);
                        else
                            moves.Add(Move.Create(from, captureTo));
                    }
                    // En passant
                    else if (captureTo == b.EpSq)
                    {
                        moves.Add(Move.Create(from, captureTo, 0, 2)); // flag 2 = ep
                    }
                }
            }
        }
        
        private static void GeneratePawnCaptures(Board b, List<Move> moves, bool white, ulong enemy)
        {
            int piece = white ? Board.WP : Board.BP;
            ulong pawns = b[piece];
            int forward = white ? 8 : -8;
            int promoRank = white ? 6 : 1;
            
            while (pawns != 0)
            {
                int from = BitOps.PopLsb(ref pawns);
                int rank = from / 8;
                int file = from % 8;
                
                // Captures only
                foreach (int df in new[] { -1, 1 })
                {
                    if (file + df < 0 || file + df > 7) continue;
                    
                    int to = from + forward + df;
                    if (to < 0 || to >= 64) continue;
                    
                    if (((enemy >> to) & 1) != 0 || to == b.EpSq)
                    {
                        if (rank == promoRank)
                            AddPromotions(moves, from, to);
                        else if (to == b.EpSq)
                            moves.Add(Move.Create(from, to, 0, 2));
                        else
                            moves.Add(Move.Create(from, to));
                    }
                }
            }
        }
        
        private static void AddPromotions(List<Move> moves, int from, int to)
        {
            for (int promo = 4; promo >= 1; promo--) // Q, R, B, N order
            {
                moves.Add(Move.Create(from, to, promo));
            }
        }
        
        // ────────────────────────────────────────────────────────────────
        // Piece move generation
        // ────────────────────────────────────────────────────────────────
        
        private static void GenerateKnightMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WN : Board.BN;
            ulong knights = b[piece];
            ulong notOwn = ~(white ? b.OccupancyWhite : b.OccupancyBlack);
            
            while (knights != 0)
            {
                int from = BitOps.PopLsb(ref knights);
                ulong attacks = Attacks.GetAttacks(piece, from, 0) & notOwn;
                
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
        }
        
        private static void GenerateBishopMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WB : Board.BB;
            ulong bishops = b[piece];
            ulong notOwn = ~(white ? b.OccupancyWhite : b.OccupancyBlack);
            
            while (bishops != 0)
            {
                int from = BitOps.PopLsb(ref bishops);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & notOwn;
                
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
        }
        
        private static void GenerateRookMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WR : Board.BR;
            ulong rooks = b[piece];
            ulong notOwn = ~(white ? b.OccupancyWhite : b.OccupancyBlack);
            
            while (rooks != 0)
            {
                int from = BitOps.PopLsb(ref rooks);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & notOwn;
                
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
        }
        
        private static void GenerateQueenMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WQ : Board.BQ;
            ulong queens = b[piece];
            ulong notOwn = ~(white ? b.OccupancyWhite : b.OccupancyBlack);
            
            while (queens != 0)
            {
                int from = BitOps.PopLsb(ref queens);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & notOwn;
                
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
        }
        
        private static void GenerateKingMoves(Board b, List<Move> moves, bool white)
        {
            int piece = white ? Board.WK : Board.BK;
            int from = b.KingSquare(white);
            ulong notOwn = ~(white ? b.OccupancyWhite : b.OccupancyBlack);
            ulong attacks = Attacks.GetAttacks(piece, from, 0) & notOwn;
            
            while (attacks != 0)
            {
                int to = BitOps.PopLsb(ref attacks);
                moves.Add(Move.Create(from, to));
            }
        }
        
        private static void GeneratePieceCaptures(Board b, List<Move> moves, bool white, ulong enemy)
        {
            // Knights
            int piece = white ? Board.WN : Board.BN;
            ulong pieces = b[piece];
            while (pieces != 0)
            {
                int from = BitOps.PopLsb(ref pieces);
                ulong attacks = Attacks.GetAttacks(piece, from, 0) & enemy;
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
            
            // Bishops
            piece = white ? Board.WB : Board.BB;
            pieces = b[piece];
            while (pieces != 0)
            {
                int from = BitOps.PopLsb(ref pieces);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & enemy;
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
            
            // Rooks
            piece = white ? Board.WR : Board.BR;
            pieces = b[piece];
            while (pieces != 0)
            {
                int from = BitOps.PopLsb(ref pieces);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & enemy;
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
            
            // Queens
            piece = white ? Board.WQ : Board.BQ;
            pieces = b[piece];
            while (pieces != 0)
            {
                int from = BitOps.PopLsb(ref pieces);
                ulong attacks = Attacks.GetAttacks(piece, from, b.OccupancyAll) & enemy;
                while (attacks != 0)
                {
                    int to = BitOps.PopLsb(ref attacks);
                    moves.Add(Move.Create(from, to));
                }
            }
            
            // King
            piece = white ? Board.WK : Board.BK;
            int kingFrom = b.KingSquare(white);
            ulong kingAttacks = Attacks.GetAttacks(piece, kingFrom, 0) & enemy;
            while (kingAttacks != 0)
            {
                int to = BitOps.PopLsb(ref kingAttacks);
                moves.Add(Move.Create(kingFrom, to));
            }
        }
        
        // ────────────────────────────────────────────────────────────────
        // Castling move generation with proper checks
        // ────────────────────────────────────────────────────────────────
        
        private static void GenerateCastlingMoves(Board b, List<Move> moves, bool white)
        {
            if (Attacks.InCheck(b)) return; // Can't castle out of check
            
            if (white)
            {
                // White kingside
                if (b.WCK && (b.OccupancyAll & 0x60UL) == 0) // f1, g1 empty
                {
                    if (!Attacks.IsSquareAttacked(b, 5, false) && // f1 not attacked
                        !Attacks.IsSquareAttacked(b, 6, false))   // g1 not attacked
                    {
                        moves.Add(Move.Create(4, 6, 0, 4)); // flag 4 = castle
                    }
                }
                
                // White queenside
                if (b.WCQ && (b.OccupancyAll & 0x0EUL) == 0) // b1, c1, d1 empty
                {
                    if (!Attacks.IsSquareAttacked(b, 3, false) && // d1 not attacked
                        !Attacks.IsSquareAttacked(b, 2, false))   // c1 not attacked
                    {
                        moves.Add(Move.Create(4, 2, 0, 4));
                    }
                }
            }
            else
            {
                // Black kingside
                if (b.BCK && (b.OccupancyAll & 0x6000000000000000UL) == 0) // f8, g8 empty
                {
                    if (!Attacks.IsSquareAttacked(b, 61, true) && // f8 not attacked
                        !Attacks.IsSquareAttacked(b, 62, true))   // g8 not attacked
                    {
                        moves.Add(Move.Create(60, 62, 0, 4));
                    }
                }
                
                // Black queenside
                if (b.BCQ && (b.OccupancyAll & 0x0E00000000000000UL) == 0) // b8, c8, d8 empty
                {
                    if (!Attacks.IsSquareAttacked(b, 59, true) && // d8 not attacked
                        !Attacks.IsSquareAttacked(b, 58, true))   // c8 not attacked
                    {
                        moves.Add(Move.Create(60, 58, 0, 4));
                    }
                }
            }
        }
    }
}