namespace ChessXiv.Domain.Engine.Types;

public readonly record struct Bitboard(ulong Value)
{
    public bool IsEmpty => Value == 0UL;

    public bool Contains(Square square) => (Value & (1UL << square.Index)) != 0UL;

    public Bitboard With(Square square) => new(Value | (1UL << square.Index));

    public Bitboard Without(Square square) => new(Value & ~(1UL << square.Index));

    public static Bitboard operator |(Bitboard left, Bitboard right) => new(left.Value | right.Value);

    public static Bitboard operator &(Bitboard left, Bitboard right) => new(left.Value & right.Value);

    public static Bitboard operator ~(Bitboard source) => new(~source.Value);
}
