namespace ChessXiv.Application.Contracts;

public class MoveTreeResponse
{
    public int TotalGamesInPosition { get; set; }
    public IReadOnlyList<MoveTreeMoveDto> Moves { get; set; } = Array.Empty<MoveTreeMoveDto>();
}
