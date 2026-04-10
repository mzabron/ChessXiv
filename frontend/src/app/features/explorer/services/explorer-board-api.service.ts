import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

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
  private readonly http = inject(HttpClient);
  private readonly baseUrl = this.resolveBaseUrl();

  applyMove(request: PositionMoveRequest): Observable<PositionMoveResponse> {
    return this.http.post<PositionMoveResponse>(`${this.baseUrl}/position/move`, request);
  }

  getMoveTree(request: ExplorerMoveTreeRequest): Observable<ExplorerMoveTreeResponse> {
    return this.http.post<ExplorerMoveTreeResponse>(`${this.baseUrl}/move-tree`, request);
  }

  private resolveBaseUrl(): string {
    const host = window.location.hostname;
    const isLocalHost = host === 'localhost' || host === '127.0.0.1' || host === '::1';

    if (isLocalHost) {
      return `http://${host}:5027/api/games/explorer`;
    }

    return '/api/games/explorer';
  }
}
