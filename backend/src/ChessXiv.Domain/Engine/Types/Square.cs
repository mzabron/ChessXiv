using System.Diagnostics;

namespace ChessXiv.Domain.Engine.Types;
public readonly record struct Square(byte Index)
{
    public int File => Index & 7;
    public int Rank => Index >> 3;

    public static Square From(int file, int rank)
    {
        if (file is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(file), "File must be between 0 and 7.");
        }

        if (rank is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be between 0 and 7.");
        }

        return new Square((byte)((rank << 3) + file));
    }
}
