namespace ChessXiv.Domain.Engine.Types;

public enum MoveType : byte
{
    Quiet = 0,
    Capture = 1,
    DoublePawnPush = 2,
    KingSideCastle = 3,
    QueenSideCastle = 4,
    EnPassant = 5,
    Promotion = 6,
    PromotionCapture = 7
}
