using System;
using System.Collections.Generic;

namespace Nisa.Chess;

/// <summary>
///  FEN parser / writer for the Nisa bit-board engine.
/// </summary>
public static class Fen
{
    /// <summary>The standard start-position FEN.</summary>
    public const string StartPos =
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Piece-letter → bitboard index.</summary>
    private static readonly Dictionary<char, int> PieceMap = new()
    {
        ['P'] = Board.WP, ['N'] = Board.WN, ['B'] = Board.WB,
        ['R'] = Board.WR, ['Q'] = Board.WQ, ['K'] = Board.WK,
        ['p'] = Board.BP, ['n'] = Board.BN, ['b'] = Board.BB,
        ['r'] = Board.BR, ['q'] = Board.BQ, ['k'] = Board.BK
    };

    /// <summary>
    ///  Parses <paramref name="fen"/> into <paramref name="b"/>.
    ///  Existing board state is cleared first.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown on malformed FEN.</exception>
    public static void ParseInto(string fen, Board b)
    {
        b.Clear();

        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new ArgumentException("FEN string has fewer than 4 fields.", nameof(fen));

        // 1) Piece placement
        int sq = 56;                               // A8
        foreach (char ch in parts[0])
        {
            if (ch == '/')
            {
                sq -= 16;                          // next rank
                continue;
            }

            if (char.IsDigit(ch))
            {
                sq += ch - '0';                    // skip empty squares
                continue;
            }

            if (!PieceMap.TryGetValue(ch, out int piece))
                throw new ArgumentException($"Invalid piece char '{ch}' in FEN.", nameof(fen));

            b[piece] |= 1UL << sq;
            sq++;
        }

        // 2) Active color
        b.WhiteToMove = parts[1] switch
        {
            "w" => true,
            "b" => false,
            _   => throw new ArgumentException("Active-color field must be 'w' or 'b'.", nameof(fen))
        };

        // 3) Castling rights
        string cr = parts[2];
        b.WCK = cr.Contains('K');
        b.WCQ = cr.Contains('Q');
        b.BCK = cr.Contains('k');
        b.BCQ = cr.Contains('q');

        // 4) En-passant square
        b.EpSq = parts[3] == "-"
            ? -1
            : (parts[3][0] - 'a') + 8 * (parts[3][1] - '1');

        // 5) Half-move clock (optional)
        if (parts.Length > 4)
            b.HalfmoveClock = int.Parse(parts[4]);

        // 6) Full-move number (optional)
        if (parts.Length > 5)
            b.Fullmove = int.Parse(parts[5]);
    }

    /// <summary>
    ///  Serializes <paramref name="b"/> back to a FEN string.
    /// </summary>
    public static string From(Board b)
    {
        Span<char> buf = stackalloc char[128];   // plenty for FEN
        int pos = 0;

        // 1) Piece placement
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;

            for (int file = 0; file < 8; file++)
            {
                int sq = rank * 8 + file;
                char pieceChar = '.';

                for (int p = 0; p < 12; p++)
                {
                    if (((b[p] >> sq) & 1) != 0)
                    {
                        pieceChar = ".PNBRQKpnbrqk"[p + 1];
                        break;
                    }
                }

                if (pieceChar == '.')
                {
                    empty++;
                }
                else
                {
                    if (empty != 0) buf[pos++] = (char)('0' + empty);
                    buf[pos++] = pieceChar;
                    empty = 0;
                }
            }

            if (empty != 0) buf[pos++] = (char)('0' + empty);
            if (rank != 0)  buf[pos++] = '/';
        }

        // 2) Active color
        buf[pos++] = ' ';
        buf[pos++] = b.WhiteToMove ? 'w' : 'b';

        // 3) Castling rights
        buf[pos++] = ' ';
        string cast = string.Concat(
            b.WCK ? "K" : "",
            b.WCQ ? "Q" : "",
            b.BCK ? "k" : "",
            b.BCQ ? "q" : "");
        if (cast == "") cast = "-";
        foreach (char c in cast) buf[pos++] = c;

        // 4) En-passant square
        buf[pos++] = ' ';
        if (b.EpSq == -1)
        {
            buf[pos++] = '-';
        }
        else
        {
            buf[pos++] = (char)('a' + (b.EpSq & 7));
            buf[pos++] = (char)('1' + (b.EpSq >> 3));
        }

        // 5) Half-move clock
        buf[pos++] = ' ';
        string hm = b.HalfmoveClock.ToString();
        hm.AsSpan().CopyTo(buf[pos..]);
        pos += hm.Length;

        // 6) Full-move number
        buf[pos++] = ' ';
        string fm = b.Fullmove.ToString();
        fm.AsSpan().CopyTo(buf[pos..]);
        pos += fm.Length;

        return new string(buf[..pos]);
    }
}
