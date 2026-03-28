import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GameReplayResponse } from './game-replay.models';
import { ExplorerGamesFiltersQuery } from './games-filters.models';

export interface DraftImportRequest {
  pgn: string;
}

export interface DraftImportResult {
  parsedCount: number;
  importedCount: number;
  skippedCount: number;
}

export interface DraftPromotionRequest {
  userDatabaseId: string;
}

export interface DirectImportToDatabaseRequest {
  pgn: string;
  userDatabaseId: string;
}

export interface DraftPromotionResult {
  promotedCount: number;
  skippedCount: number;
}

export interface DraftGameListItem {
  id: string;
  year: number;
  white: string;
  whiteElo: number | null;
  result: string;
  black: string;
  blackElo: number | null;
  eco: string | null;
  event: string | null;
  moveCount: number;
  createdAtUtc: string;
}

export interface DraftGamesPageResponse {
  page: number;
  pageSize: number;
  totalCount: number;
  items: DraftGameListItem[];
}

export type DraftGamesSortBy = 'createdAt' | 'year' | 'white' | 'black' | 'whiteElo' | 'blackElo' | 'result' | 'eco' | 'event' | 'moves';
export type DraftGamesSortDirection = 'asc' | 'desc';
export type DraftGamesResultSortMode = 'default' | 'whiteFirst' | 'blackFirst' | 'drawFirst';

@Injectable({ providedIn: 'root' })
export class DraftImportApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = this.resolveBaseUrl();

  importDraft(request: DraftImportRequest): Observable<DraftImportResult> {
    return this.http.post<DraftImportResult>(`${this.baseUrl}/drafts/import`, request);
  }

  promoteDraft(request: DraftPromotionRequest): Observable<DraftPromotionResult> {
    return this.http.post<DraftPromotionResult>(`${this.baseUrl}/drafts/promote`, request);
  }

  importToDatabase(request: DirectImportToDatabaseRequest): Observable<DraftImportResult> {
    return this.http.post<DraftImportResult>(`${this.baseUrl}/import-to-database`, request);
  }

  getDraftGames(
    page: number,
    pageSize: number,
    sortBy: DraftGamesSortBy,
    sortDirection: DraftGamesSortDirection,
    resultSortMode: DraftGamesResultSortMode,
    filters?: ExplorerGamesFiltersQuery
  ): Observable<DraftGamesPageResponse> {
    return this.http.get<DraftGamesPageResponse>(`${this.baseUrl}/drafts/games`, {
      params: {
        page,
        pageSize,
        sortBy,
        sortDirection,
        resultSortMode,
        ...this.buildFilterParams(filters)
      }
    });
  }

  clearDraftGames(): Observable<{ deletedCount: number }> {
    return this.http.delete<{ deletedCount: number }>(`${this.baseUrl}/drafts`);
  }

  getDraftGameReplay(gameId: string): Observable<GameReplayResponse> {
    return this.http.get<GameReplayResponse>(`${this.baseUrl}/drafts/games/${gameId}`);
  }

  private resolveBaseUrl(): string {
    const host = window.location.hostname;
    const isLocalHost = host === 'localhost' || host === '127.0.0.1' || host === '::1';

    if (isLocalHost) {
      return `http://${host}:5027/api/pgn`;
    }

    return '/api/pgn';
  }

  private buildFilterParams(filters?: ExplorerGamesFiltersQuery): Record<string, string | number | boolean> {
    if (!filters) {
      return {};
    }

    const params: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(filters)) {
      if (value === undefined || value === null || value === '') {
        continue;
      }

      params[key] = value;
    }

    return params;
  }
}