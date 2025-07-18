using System;

namespace Nisa.Chess
{
    /// <summary>
    /// Efficient bitboard-based attack generation for all piece types.
    /// Pre-computed tables for non-sliders, magic-free ray attacks for sliders.
    /// </summary>
    internal static class Attacks
    {
        // Pre-computed attack tables
        private static readonly ulong[] KnightAttacks = new ulong[64];
        private static readonly ulong[] KingAttacks = new ulong[64];
        private static readonly ulong[] PawnAttacksWhite = new ulong[64];
        private static readonly ulong[] PawnAttacksBlack = new ulong[64];
        
        // Ray attacks for sliders (8 directions × 64 squares)
        private static readonly ulong[,] RayAttacks = new ulong[8, 64];
        
        // Direction indices
        private const int North = 0, NorthEast = 1, East = 2, SouthEast = 3;
        private const int South = 4, SouthWest = 5, West = 6, NorthWest = 7;
        
        // Useful constants
        private const ulong FileA = 0x0101010101010101UL;
        private const ulong FileH = 0x8080808080808080UL;
        private const ulong Rank1 = 0x00000000000000FFUL;
        private const ulong Rank8 = 0xFF00000000000000UL;
        
        static Attacks()
        {
            InitializeKnightAttacks();
            InitializeKingAttacks();
            InitializePawnAttacks();
            InitializeRayAttacks();
        }
        
        // ─────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Get attack bitboard for a piece at given square.
        /// </summary>
        public static ulong GetAttacks(int piece, int square, ulong occupied)
        {
            return piece switch
            {
                Board.WP => PawnAttacksWhite[square],
                Board.BP => PawnAttacksBlack[square],
                Board.WN or Board.BN => KnightAttacks[square],
                Board.WB or Board.BB => GetBishopAttacks(square, occupied),
                Board.WR or Board.BR => GetRookAttacks(square, occupied),
                Board.WQ or Board.BQ => GetQueenAttacks(square, occupied),
                Board.WK or Board.BK => KingAttacks[square],
                _ => 0UL
            };
        }
        
        /// <summary>
        /// Check if a square is attacked by the given side.
        /// </summary>
        public static bool IsSquareAttacked(Board board, int square, bool byWhite)
        {
            ulong occupied = board.OccupancyAll;
            
            // Check pawn attacks (reversed - look where enemy pawns could be)
            ulong pawnAttacks = byWhite ? PawnAttacksBlack[square] : PawnAttacksWhite[square];
            if ((pawnAttacks & board[byWhite ? Board.WP : Board.BP]) != 0)
                return true;
            
            // Check knight attacks
            if ((KnightAttacks[square] & board[byWhite ? Board.WN : Board.BN]) != 0)
                return true;
            
            // Check king attacks
            if ((KingAttacks[square] & board[byWhite ? Board.WK : Board.BK]) != 0)
                return true;
            
            // Check bishop/queen attacks on diagonals
            ulong bishopAttacks = GetBishopAttacks(square, occupied);
            if ((bishopAttacks & (board[byWhite ? Board.WB : Board.BB] | 
                                  board[byWhite ? Board.WQ : Board.BQ])) != 0)
                return true;
            
            // Check rook/queen attacks on ranks/files
            ulong rookAttacks = GetRookAttacks(square, occupied);
            if ((rookAttacks & (board[byWhite ? Board.WR : Board.BR] | 
                                board[byWhite ? Board.WQ : Board.BQ])) != 0)
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Get all squares attacked by a side.
        /// </summary>
        public static ulong GetAllAttacks(Board board, bool byWhite)
        {
            ulong attacks = 0UL;
            ulong occupied = board.OccupancyAll;
            
            // Pawn attacks
            ulong pawns = board[byWhite ? Board.WP : Board.BP];
            while (pawns != 0)
            {
                int sq = BitOps.PopLsb(ref pawns);
                attacks |= byWhite ? PawnAttacksWhite[sq] : PawnAttacksBlack[sq];
            }
            
            // Knight attacks
            ulong knights = board[byWhite ? Board.WN : Board.BN];
            while (knights != 0)
            {
                int sq = BitOps.PopLsb(ref knights);
                attacks |= KnightAttacks[sq];
            }
            
            // Bishop attacks
            ulong bishops = board[byWhite ? Board.WB : Board.BB];
            while (bishops != 0)
            {
                int sq = BitOps.PopLsb(ref bishops);
                attacks |= GetBishopAttacks(sq, occupied);
            }
            
            // Rook attacks
            ulong rooks = board[byWhite ? Board.WR : Board.BR];
            while (rooks != 0)
            {
                int sq = BitOps.PopLsb(ref rooks);
                attacks |= GetRookAttacks(sq, occupied);
            }
            
            // Queen attacks
            ulong queens = board[byWhite ? Board.WQ : Board.BQ];
            while (queens != 0)
            {
                int sq = BitOps.PopLsb(ref queens);
                attacks |= GetQueenAttacks(sq, occupied);
            }
            
            // King attacks
            int kingSquare = board.KingSquare(byWhite);
            attacks |= KingAttacks[kingSquare];
            
            return attacks;
        }
        
        /// <summary>
        /// Check if the current side to move is in check.
        /// </summary>
        public static bool InCheck(Board board)
        {
            int kingSquare = board.KingSquare(board.WhiteToMove);
            return IsSquareAttacked(board, kingSquare, !board.WhiteToMove);
        }
        
        // ─────────────────────────────────────────────────────────────────
        // Slider attacks using classical ray scanning
        // ─────────────────────────────────────────────────────────────────
        
        private static ulong GetBishopAttacks(int square, ulong occupied)
        {
            return GetRayAttack(square, occupied, NorthEast) |
                   GetRayAttack(square, occupied, SouthEast) |
                   GetRayAttack(square, occupied, SouthWest) |
                   GetRayAttack(square, occupied, NorthWest);
        }
        
        private static ulong GetRookAttacks(int square, ulong occupied)
        {
            return GetRayAttack(square, occupied, North) |
                   GetRayAttack(square, occupied, East) |
                   GetRayAttack(square, occupied, South) |
                   GetRayAttack(square, occupied, West);
        }
        
        private static ulong GetQueenAttacks(int square, ulong occupied)
        {
            return GetBishopAttacks(square, occupied) | GetRookAttacks(square, occupied);
        }
        
        private static ulong GetRayAttack(int square, ulong occupied, int direction)
        {
            ulong attacks = RayAttacks[direction, square];
            ulong blockers = attacks & occupied;
            
            if (blockers != 0)
            {
                int blocker = direction < 4 
                    ? BitOps.Lsb(blockers)      // Positive directions: first blocker
                    : 63 - BitOps.Lsb(Reverse(blockers)); // Negative directions: last blocker
                    
                attacks ^= RayAttacks[direction, blocker];
            }
            
            return attacks;
        }
        
        // Reverse bits in a ulong (for negative ray directions)
        private static ulong Reverse(ulong b)
        {
            b = ((b & 0x5555555555555555UL) << 1) | ((b & 0xAAAAAAAAAAAAAAAAUL) >> 1);
            b = ((b & 0x3333333333333333UL) << 2) | ((b & 0xCCCCCCCCCCCCCCCCUL) >> 2);
            b = ((b & 0x0F0F0F0F0F0F0F0FUL) << 4) | ((b & 0xF0F0F0F0F0F0F0F0UL) >> 4);
            b = ((b & 0x00FF00FF00FF00FFUL) << 8) | ((b & 0xFF00FF00FF00FF00UL) >> 8);
            b = ((b & 0x0000FFFF0000FFFFUL) << 16) | ((b & 0xFFFF0000FFFF0000UL) >> 16);
            b = ((b & 0x00000000FFFFFFFFUL) << 32) | ((b & 0xFFFFFFFF00000000UL) >> 32);
            return b;
        }
        
        // ─────────────────────────────────────────────────────────────────
        // Initialization
        // ─────────────────────────────────────────────────────────────────
        
        private static void InitializeKnightAttacks()
        {
            int[] df = { 1, 2, 2, 1, -1, -2, -2, -1 };
            int[] dr = { 2, 1, -1, -2, -2, -1, 1, 2 };
            
            for (int sq = 0; sq < 64; sq++)
            {
                int rank = sq / 8;
                int file = sq % 8;
                ulong attacks = 0UL;
                
                for (int i = 0; i < 8; i++)
                {
                    int newFile = file + df[i];
                    int newRank = rank + dr[i];
                    
                    if (newFile >= 0 && newFile < 8 && newRank >= 0 && newRank < 8)
                    {
                        attacks |= 1UL << (newRank * 8 + newFile);
                    }
                }
                
                KnightAttacks[sq] = attacks;
            }
        }
        
        private static void InitializeKingAttacks()
        {
            int[] df = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dr = { -1, -1, -1, 0, 0, 1, 1, 1 };
            
            for (int sq = 0; sq < 64; sq++)
            {
                int rank = sq / 8;
                int file = sq % 8;
                ulong attacks = 0UL;
                
                for (int i = 0; i < 8; i++)
                {
                    int newFile = file + df[i];
                    int newRank = rank + dr[i];
                    
                    if (newFile >= 0 && newFile < 8 && newRank >= 0 && newRank < 8)
                    {
                        attacks |= 1UL << (newRank * 8 + newFile);
                    }
                }
                
                KingAttacks[sq] = attacks;
            }
        }
        
        private static void InitializePawnAttacks()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                int rank = sq / 8;
                int file = sq % 8;
                
                // White pawn attacks (upward)
                ulong wAttacks = 0UL;
                if (rank < 7)
                {
                    if (file > 0) wAttacks |= 1UL << (sq + 7);  // Left capture
                    if (file < 7) wAttacks |= 1UL << (sq + 9);  // Right capture
                }
                PawnAttacksWhite[sq] = wAttacks;
                
                // Black pawn attacks (downward)
                ulong bAttacks = 0UL;
                if (rank > 0)
                {
                    if (file > 0) bAttacks |= 1UL << (sq - 9);  // Left capture
                    if (file < 7) bAttacks |= 1UL << (sq - 7);  // Right capture
                }
                PawnAttacksBlack[sq] = bAttacks;
            }
        }
        
        private static void InitializeRayAttacks()
        {
            int[] df = { 0, 1, 1, 1, 0, -1, -1, -1 };
            int[] dr = { 1, 1, 0, -1, -1, -1, 0, 1 };
            
            for (int dir = 0; dir < 8; dir++)
            {
                for (int sq = 0; sq < 64; sq++)
                {
                    int rank = sq / 8;
                    int file = sq % 8;
                    ulong attacks = 0UL;
                    
                    int f = file + df[dir];
                    int r = rank + dr[dir];
                    
                    while (f >= 0 && f < 8 && r >= 0 && r < 8)
                    {
                        attacks |= 1UL << (r * 8 + f);
                        f += df[dir];
                        r += dr[dir];
                    }
                    
                    RayAttacks[dir, sq] = attacks;
                }
            }
        }
    }
}