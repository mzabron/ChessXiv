namespace ChessBase.Domain.Engine.Hashing;

public static class ZobristTables
{
    public static readonly ulong[,] PieceSquare = new ulong[12, 64];
    public static readonly ulong SideToMove;
    public static readonly ulong[] CastlingRights = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];

    static ZobristTables()
    {
        ulong seed = 0x9E3779B97F4A7C15UL;

        for (var piece = 0; piece < 12; piece++)
        {
            for (var square = 0; square < 64; square++)
            {
                PieceSquare[piece, square] = Next(ref seed);
            }
        }

        SideToMove = Next(ref seed);

        for (var i = 0; i < CastlingRights.Length; i++)
        {
            CastlingRights[i] = Next(ref seed);
        }

        for (var i = 0; i < EnPassantFile.Length; i++)
        {
            EnPassantFile[i] = Next(ref seed);
        }
    }

    private static ulong Next(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
