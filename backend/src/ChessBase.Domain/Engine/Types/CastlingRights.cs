namespace ChessBase.Domain.Engine.Types;

public readonly record struct CastlingRights(byte Value)
{
    public const byte WhiteKingSide = 1 << 0;
    public const byte WhiteQueenSide = 1 << 1;
    public const byte BlackKingSide = 1 << 2;
    public const byte BlackQueenSide = 1 << 3;

    public static CastlingRights None => new(0);
    public static CastlingRights All => new((byte)(WhiteKingSide | WhiteQueenSide | BlackKingSide | BlackQueenSide));

    public bool Has(byte right) => (Value & right) != 0;

    public CastlingRights Add(byte right) => new((byte)(Value | right));

    public CastlingRights Remove(byte right) => new((byte)(Value & ~right));
}
