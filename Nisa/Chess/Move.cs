using System;

namespace Nisa.Chess;

/// <summary>
/// 16-bit packed move:
/// bits 0-5   = from-square (0-63)  
/// bits 6-11 = to-square   (0-63)  
/// bits 12-14 = promotion piece (0 = none, 1 = n, 2 = b, 3 = r, 4 = q)  
/// bit 15     = extra flags (castle / ep etc. — user-defined)
/// </summary>
public readonly record struct Move(ushort Packed)
{
    public byte From       => (byte)(Packed & 0x3F);
    public byte To         => (byte)((Packed >> 6) & 0x3F);
    public byte Promotion  => (byte)((Packed >> 12) & 0x7);
    public ushort Flags    => (ushort)(Packed >> 15);

    public static Move Create(int from, int to, int promotion = 0, ushort flags = 0) =>
        new((ushort)(from | (to << 6) | (promotion << 12) | (flags << 15)));

    public string ToUci()
    {
        Span<char> s = stackalloc char[5];
        s[0] = (char)('a' + (From & 7));
        s[1] = (char)('1' + (From >> 3));
        s[2] = (char)('a' + (To   & 7));
        s[3] = (char)('1' + (To   >> 3));
        if (Promotion is > 0 and < 5)
        {
            s[4] = " nbrq"[Promotion];   // n,b,r,q
            return new string(s[..5]);
        }
        return new string(s[..4]);
    }
}
