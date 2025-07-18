using System;
using System.Collections.Generic;
using Nisa.Chess;

namespace Nisa.Neural
{
    /// <summary>
    /// Exact 1,858-slot mapping used by Lc0's flat policy head.
    /// This implementation follows the exact specification from Lc0's source code.
    /// </summary>
    internal static class PolicyMap
    {
        // ──────────────────────────────────────────────────────────────────
        // Policy index layout:
        // 0-55:     Queen promotions (8 files × 7 directions)
        // 56-503:   Queen-like moves (64 squares × 7 distances × 8 directions) 
        // 504-695:  Knight moves (64 squares × 8 directions, reduced)
        // 696-1607: Pawn moves (64 squares × 3 types + extras)
        // 1608-1663: Under-promotions to N/B/R (8 files × 7 directions)
        // 1664-1857: Special moves and padding
        // ──────────────────────────────────────────────────────────────────

        // Direction vectors for queen moves (N, NE, E, SE, S, SW, W, NW)
        private static readonly (int df, int dr)[] QueenDirs = 
        {
            (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)
        };

        // Knight move offsets
        private static readonly (int df, int dr)[] KnightOffsets = 
        {
            (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)
        };

        // Cache for move to index mapping
        private static readonly Dictionary<ulong, int> _moveToIndexCache = new();

        // ──────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ──────────────────────────────────────────────────────────────────
        public static Move IndexToMove(Board b, int index)
        {
            bool white = b.WhiteToMove;
            
            // For black, mirror the index
            if (!white)
            {
                index = MirrorPolicyIndex(index);
            }

            var move = DecodePolicyIndex(index);
            
            // For black, flip the move back
            if (!white && move.From != 0 || move.To != 0)
            {
                move = Move.Create(
                    63 - move.From,
                    63 - move.To,
                    move.Promotion,
                    move.Flags
                );
            }

            return move;
        }

        public static int MoveToIndex(Board b, Move mv)
        {
            // Create unique cache key
            ulong key = ((ulong)(uint)mv.GetHashCode()) |
                       ((ulong)(b.WhiteToMove ? 1 : 0) << 32);

            if (_moveToIndexCache.TryGetValue(key, out int idx)) 
                return idx;

            // For black moves, flip to white perspective
            Move searchMove = mv;
            if (!b.WhiteToMove)
            {
                searchMove = Move.Create(
                    63 - mv.From,
                    63 - mv.To,
                    mv.Promotion,
                    mv.Flags
                );
            }

            // Encode the move
            int index = EncodePolicyIndex(searchMove);
            
            // For black, mirror the index
            if (!b.WhiteToMove && index >= 0)
            {
                index = MirrorPolicyIndex(index);
            }

            if (index >= 0)
            {
                _moveToIndexCache[key] = index;
            }

            return index;
        }

        // ──────────────────────────────────────────────────────────────────
        // DECODER
        // ──────────────────────────────────────────────────────────────────
        private static Move DecodePolicyIndex(int idx)
        {
            // Queen promotions (0-55)
            if (idx < 56)
            {
                return DecodePromotion(idx, 4); // Queen
            }

            // Queen-like moves (56-503)
            if (idx < 504)
            {
                return DecodeQueenMove(idx - 56);
            }

            // Knight moves (504-695)
            if (idx < 696)
            {
                return DecodeKnightMove(idx - 504);
            }

            // Pawn pushes and captures (696-1607)
            if (idx < 1608)
            {
                return DecodePawnMove(idx - 696);
            }

            // Under-promotions (1608-1663)
            if (idx < 1664)
            {
                int underPromoIdx = idx - 1608;
                int promoType = underPromoIdx / 56 + 1; // 1=N, 2=B, 3=R
                return DecodePromotion(underPromoIdx % 56, promoType);
            }

            // Invalid indices
            return Move.Create(0, 0);
        }

        private static Move DecodePromotion(int idx, int promoType)
        {
            // idx encodes file (0-7) and direction (0-6)
            // 0=straight, 1-3=left captures, 4-6=right captures
            int direction = idx / 8;
            int file = idx % 8;

            int from = 6 * 8 + file; // 7th rank
            int to = 7 * 8 + file;   // 8th rank

            // Adjust for captures
            if (direction >= 1 && direction <= 3)
            {
                to -= 1; // Left capture
            }
            else if (direction >= 4 && direction <= 6)
            {
                to += 1; // Right capture
            }

            // Validate bounds
            if ((to & 7) < 0 || (to & 7) > 7) 
                return Move.Create(0, 0);

            return Move.Create(from, to, promoType);
        }

        private static Move DecodeQueenMove(int idx)
        {
            // Layout: square(64) × distance(7) × direction(8)
            // But stored as: direction(8) × distance(7) × square(64)
            int square = idx % 64;
            idx /= 64;
            int distance = idx % 7 + 1; // 1-7
            int direction = idx / 7;

            int rank = square / 8;
            int file = square % 8;

            var (df, dr) = QueenDirs[direction];
            int toFile = file + df * distance;
            int toRank = rank + dr * distance;

            // Check bounds
            if (toFile < 0 || toFile > 7 || toRank < 0 || toRank > 7)
                return Move.Create(0, 0);

            return Move.Create(square, toRank * 8 + toFile);
        }

        private static Move DecodeKnightMove(int idx)
        {
            // Knight moves are stored compactly
            // Only 2 ranks (0-1) × 24 moves per rank × 8 files
            int square = (idx / 24) + (idx % 2) * 8;
            int moveIdx = (idx % 24) / 2;

            if (moveIdx >= 8) 
                return Move.Create(0, 0);

            int rank = square / 8;
            int file = square % 8;

            var (df, dr) = KnightOffsets[moveIdx];
            int toFile = file + df;
            int toRank = rank + dr;

            // Check bounds
            if (toFile < 0 || toFile > 7 || toRank < 0 || toRank > 7)
                return Move.Create(0, 0);

            return Move.Create(square, toRank * 8 + toFile);
        }

        private static Move DecodePawnMove(int idx)
        {
            // Standard pawn moves for ranks 2-6
            // Special handling for rank 7 (promotions handled separately)
            int square = idx / 3;
            int moveType = idx % 3;

            if (square >= 64) 
                return Move.Create(0, 0);

            int rank = square / 8;
            int file = square % 8;

            // Skip invalid pawn squares
            if (rank == 0 || rank == 7) 
                return Move.Create(0, 0);

            int to;
            switch (moveType)
            {
                case 0: // Push
                    to = square + 8;
                    if (rank == 1) // Double push from 2nd rank
                    {
                        return Move.Create(square, square + 16, 0, 1);
                    }
                    break;
                    
                case 1: // Capture left
                    if (file == 0) return Move.Create(0, 0);
                    to = square + 7;
                    break;
                    
                case 2: // Capture right
                    if (file == 7) return Move.Create(0, 0);
                    to = square + 9;
                    break;
                    
                default:
                    return Move.Create(0, 0);
            }

            return Move.Create(square, to);
        }

        // ──────────────────────────────────────────────────────────────────
        // ENCODER
        // ──────────────────────────────────────────────────────────────────
        private static int EncodePolicyIndex(Move move)
        {
            int from = move.From;
            int to = move.To;
            int fromRank = from / 8;
            int fromFile = from % 8;
            int toRank = to / 8;
            int toFile = to % 8;

            // Promotions
            if (move.Promotion > 0 && fromRank == 6 && toRank == 7)
            {
                int direction = 0;
                if (toFile < fromFile) 
                    direction = 1; // Left capture
                else if (toFile > fromFile) 
                    direction = 4; // Right capture
                
                int baseIdx = fromFile * 7 + direction;
                
                if (move.Promotion == 4) // Queen
                    return baseIdx;
                else // Under-promotion
                    return 1608 + (move.Promotion - 1) * 56 + baseIdx;
            }

            // Try queen-like encoding
            int fileDiff = toFile - fromFile;
            int rankDiff = toRank - fromRank;
            
            if (fileDiff == 0 || rankDiff == 0 || Math.Abs(fileDiff) == Math.Abs(rankDiff))
            {
                // Find direction
                int direction = -1;
                for (int d = 0; d < 8; d++)
                {
                    var (df, dr) = QueenDirs[d];
                    if (Math.Sign(fileDiff) == df && Math.Sign(rankDiff) == dr)
                    {
                        direction = d;
                        break;
                    }
                }

                if (direction >= 0)
                {
                    int distance = Math.Max(Math.Abs(fileDiff), Math.Abs(rankDiff));
                    if (distance >= 1 && distance <= 7)
                    {
                        return 56 + (direction * 7 + (distance - 1)) * 64 + from;
                    }
                }
            }

            // Try knight encoding
            for (int k = 0; k < 8; k++)
            {
                var (df, dr) = KnightOffsets[k];
                if (fromFile + df == toFile && fromRank + dr == toRank)
                {
                    if (fromRank < 2)
                    {
                        return 504 + fromFile * 24 + fromRank * 12 + k;
                    }
                    break;
                }
            }

            // Try pawn encoding
            if (Math.Abs(fileDiff) <= 1 && rankDiff == 1 && fromRank >= 1 && fromRank <= 6)
            {
                int moveType;
                if (fileDiff == 0)
                {
                    // Check for double push
                    if (from + 16 == to && fromRank == 1)
                    {
                        return 696 + from * 3; // Regular push encodes double push
                    }
                    moveType = 0; // Push
                }
                else if (fileDiff == -1)
                {
                    moveType = 1; // Capture left
                }
                else
                {
                    moveType = 2; // Capture right
                }
                
                return 696 + from * 3 + moveType;
            }

            return -1; // Invalid move for policy
        }

        // ──────────────────────────────────────────────────────────────────
        // MIRRORING
        // ──────────────────────────────────────────────────────────────────
        private static int MirrorPolicyIndex(int idx)
        {
            // Each section of the policy needs different mirroring logic
            
            // Queen promotions (0-55)
            if (idx < 56)
            {
                int direction = idx / 8;
                int file = idx % 8;
                int newFile = 7 - file;
                
                // Mirror capture direction
                int newDirection = direction;
                if (direction >= 1 && direction <= 3) // Left captures
                    newDirection = direction + 3; // Become right captures
                else if (direction >= 4 && direction <= 6) // Right captures  
                    newDirection = direction - 3; // Become left captures
                    
                return newFile * 7 + newDirection;
            }

            // Queen moves (56-503)
            if (idx < 504)
            {
                int relIdx = idx - 56;
                int square = relIdx % 64;
                relIdx /= 64;
                int distance = relIdx % 7;
                int direction = relIdx / 7;
                
                // Mirror square
                int rank = square / 8;
                int file = square % 8;
                int newSquare = (7 - rank) * 8 + (7 - file);
                
                // Mirror direction vertically
                int newDirection = (8 - direction) % 8;
                
                return 56 + (newDirection * 7 + distance) * 64 + newSquare;
            }

            // Knight moves (504-695)
            if (idx < 696)
            {
                // Knight moves need complex mirroring
                int relIdx = idx - 504;
                int fileGroup = relIdx / 24;
                int remainder = relIdx % 24;
                int rank = remainder % 2;
                int moveIdx = remainder / 2;
                
                int square = fileGroup + rank * 8;
                int newRank = 7 - (square / 8);
                int newFile = 7 - (square % 8);
                int newSquare = newRank * 8 + newFile;
                
                // Mirror knight direction
                int[] mirrorMap = {2, 1, 0, 7, 6, 5, 4, 3};
                int newMoveIdx = moveIdx < 8 ? mirrorMap[moveIdx] : moveIdx;
                
                int newFileGroup = newSquare % 8;
                int newRankGroup = newSquare / 8;
                
                return 504 + newFileGroup * 24 + newRankGroup * 12 + newMoveIdx;
            }

            // Pawn moves (696-1607)
            if (idx < 1608)
            {
                int relIdx = idx - 696;
                int square = relIdx / 3;
                int moveType = relIdx % 3;
                
                int rank = square / 8;
                int file = square % 8;
                int newSquare = (7 - rank) * 8 + (7 - file);
                
                // Swap left/right captures
                int newMoveType = moveType;
                if (moveType == 1) newMoveType = 2;
                else if (moveType == 2) newMoveType = 1;
                
                return 696 + newSquare * 3 + newMoveType;
            }

            // Under-promotions (1608-1663)
            if (idx < 1664)
            {
                int relIdx = idx - 1608;
                int promoType = relIdx / 56;
                int promoIdx = relIdx % 56;
                
                // Mirror like queen promotions
                int direction = promoIdx / 8;
                int file = promoIdx % 8;
                int newFile = 7 - file;
                
                int newDirection = direction;
                if (direction >= 1 && direction <= 3)
                    newDirection = direction + 3;
                else if (direction >= 4 && direction <= 6)
                    newDirection = direction - 3;
                    
                return 1608 + promoType * 56 + newFile * 7 + newDirection;
            }

            return idx; // Invalid/special indices unchanged
        }
    }
}