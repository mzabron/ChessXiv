/**
 * Types for the local (in-browser) Stockfish engine.
 *
 * Analysis runs entirely on the user's machine in a Web Worker: the backend has no engine
 * and could not evaluate positions for every visitor at once.
 */

export type EngineStatus = 'off' | 'loading' | 'ready' | 'error';

export type EngineOptionType = 'check' | 'spin' | 'combo' | 'button' | 'string';

/**
 * A UCI option exactly as the engine declared it in its `uci` response. The panel renders
 * these generically rather than hardcoding a list, so whatever a future Stockfish build
 * exposes shows up in the UI without a code change.
 */
export interface EngineOption {
  name: string;
  type: EngineOptionType;
  defaultValue: string;
  min?: number;
  max?: number;
  choices?: string[];
}

/**
 * One principal variation. Scores are always given from White's point of view - the engine
 * reports them from the side to move's, which flips sign every ply and reads as noise.
 */
export interface EngineLine {
  /** 1-based rank of this line within the current search; line 1 is the engine's best. */
  multipv: number;
  depth: number;
  /** Centipawns from White's perspective, or null when the line ends in mate. */
  cp: number | null;
  /** Signed distance to mate from White's perspective (+3 = White mates in 3), else null. */
  mate: number | null;
  pvUci: string[];
  pvSan: string[];
}
