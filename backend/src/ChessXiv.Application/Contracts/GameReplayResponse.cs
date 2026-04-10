namespace ChessXiv.Application.Contracts;

public sealed record GameReplayMoveDto(
    int MoveNumber,
    string WhiteMove,
    string? BlackMove,
    string? WhiteClk,
    string? BlackClk);

public sealed record GameReplayResponse(
    Guid GameId,
    string White,
    int? WhiteElo,
    string Black,
    int? BlackElo,
    string Result,
    string? Event,
    int Year,
    IReadOnlyList<string> FenHistory,
    IReadOnlyList<GameReplayMoveDto> Moves);