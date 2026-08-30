namespace ChessXiv.Domain.Engine.Hashing;

public static class ZobristTables
{
    public static readonly ulong[,] PieceSquare = new ulong[12, 64];
    public static readonly ulong SideToMove;
    public static readonly ulong[] CastlingRights = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];

    /// <summary>
    /// A second, independently seeded table set. Stored positions are identified by the
    /// two halves together, giving a 128-bit key: at chess-database scale that makes a
    /// collision impossible in practice, which is what lets the full FEN string be dropped
    /// from storage without weakening exact position search.
    /// </summary>
    public static readonly ulong[,] PieceSquareHigh = new ulong[12, 64];
    public static readonly ulong SideToMoveHigh;
    public static readonly ulong[] CastlingRightsHigh = new ulong[16];
    public static readonly ulong[] EnPassantFileHigh = new ulong[8];

    static ZobristTables()
    {
        ulong seed = 0x9E3779B97F4A7C15UL;
        Fill(ref seed, PieceSquare, out SideToMove, CastlingRights, EnPassantFile);

        ulong highSeed = 0xD1B54A32D192ED03UL;
        Fill(ref highSeed, PieceSquareHigh, out SideToMoveHigh, CastlingRightsHigh, EnPassantFileHigh);
    }

    private static void Fill(
        ref ulong seed,
        ulong[,] pieceSquare,
        out ulong sideToMove,
        ulong[] castlingRights,
        ulong[] enPassantFile)
    {
        for (var piece = 0; piece < 12; piece++)
        {
            for (var square = 0; square < 64; square++)
            {
                pieceSquare[piece, square] = Next(ref seed);
            }
        }

        sideToMove = Next(ref seed);

        for (var i = 0; i < castlingRights.Length; i++)
        {
            castlingRights[i] = Next(ref seed);
        }

        for (var i = 0; i < enPassantFile.Length; i++)
        {
            enPassantFile[i] = Next(ref seed);
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
