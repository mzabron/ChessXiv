import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, tap } from 'rxjs';

export interface PositionMoveRequest {
  fen: string;
  from?: string;
  to?: string;
  san?: string;
  promotion?: string | null;
}

export interface PositionMoveResponse {
  isValid: boolean;
  fen?: string | null;
  san?: string | null;
  error?: string | null;
}

export interface ExplorerMoveTreeRequest {
  fen: string;
  source: number;
  userDatabaseId?: string;
  maxMoves?: number;
  whiteFirstName?: string;
  whiteLastName?: string;
  blackFirstName?: string;
  blackLastName?: string;
  ignoreColors?: boolean;
  eloEnabled?: boolean;
  eloFrom?: number;
  eloTo?: number;
  eloMode?: number;
  yearEnabled?: boolean;
  yearFrom?: number;
  yearTo?: number;
  ecoCode?: string;
  result?: string;
  moveCountFrom?: number;
  moveCountTo?: number;
  searchByPosition?: boolean;
  filterFen?: string;
  positionMode?: number;
}

export interface ExplorerMoveTreeMoveDto {
  moveSan: string;
  games: number;
  whiteWins: number;
  draws: number;
  blackWins: number;
  whiteWinPct: number;
  drawPct: number;
  blackWinPct: number;
}

export interface ExplorerMoveTreeResponse {
  totalGamesInPosition: number;
  moves: ExplorerMoveTreeMoveDto[];
}

@Injectable({ providedIn: 'root' })
export class ExplorerBoardApiService {
  /**
   * Move-tree responses are a pure function of (source, database, filters, position), so
   * they can be memoised for as long as the underlying pool of games is unchanged. This
   * is what stops the expensive start-position tree from being recomputed every time the
   * user opens a game, which resets the board to ply 0.
   */
  private static readonly moveTreeCacheLimit = 240;

  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/games/explorer';
  private readonly moveTreeCache = new Map<string, ExplorerMoveTreeResponse>();

  applyMove(request: PositionMoveRequest): Observable<PositionMoveResponse> {
    return this.http.post<PositionMoveResponse>(`${this.baseUrl}/position/move`, request);
  }

  getMoveTree(request: ExplorerMoveTreeRequest): Observable<ExplorerMoveTreeResponse> {
    const cacheKey = ExplorerBoardApiService.buildMoveTreeCacheKey(request);
    const cached = this.moveTreeCache.get(cacheKey);

    if (cached) {
      // Refresh recency so the entries worth keeping survive eviction.
      this.moveTreeCache.delete(cacheKey);
      this.moveTreeCache.set(cacheKey, cached);
      return of(cached);
    }

    return this.http
      .post<ExplorerMoveTreeResponse>(`${this.baseUrl}/move-tree`, request)
      .pipe(tap(response => this.storeMoveTree(cacheKey, response)));
  }

  /**
   * Drops memoised trees. Must be called whenever the pool of games behind them can have
   * changed: a new import, a save, a deletion, or a change of session.
   */
  invalidateMoveTreeCache(): void {
    this.moveTreeCache.clear();
  }

  private storeMoveTree(cacheKey: string, response: ExplorerMoveTreeResponse): void {
    if (this.moveTreeCache.size >= ExplorerBoardApiService.moveTreeCacheLimit) {
      const oldestKey = this.moveTreeCache.keys().next().value;
      if (oldestKey !== undefined) {
        this.moveTreeCache.delete(oldestKey);
      }
    }

    this.moveTreeCache.set(cacheKey, response);
  }

  private static buildMoveTreeCacheKey(request: ExplorerMoveTreeRequest): string {
    // Key on sorted entries so property order in the request cannot produce a miss for a
    // request that is semantically identical.
    return JSON.stringify(
      Object.entries(request)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .sort(([left], [right]) => left.localeCompare(right))
    );
  }
}
