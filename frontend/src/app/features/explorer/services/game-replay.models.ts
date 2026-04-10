export interface GameReplayMoveDto {
  moveNumber: number;
  whiteMove: string;
  blackMove: string | null;
  whiteClk: string | null;
  blackClk: string | null;
}

export interface GameReplayResponse {
  gameId: string;
  white: string;
  whiteElo: number | null;
  black: string;
  blackElo: number | null;
  result: string;
  event: string | null;
  year: number;
  fenHistory: string[];
  moves: GameReplayMoveDto[];
}
