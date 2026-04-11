import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GameReplayResponse } from './game-replay.models';
import { ExplorerGamesFiltersQuery } from './games-filters.models';

export interface DraftPromotionRequest {
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
  private readonly baseUrl = '/api/pgn';

  importDraft(file: File): Observable<void> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<void>(`${this.baseUrl}/drafts/import-file`, formData);
  }

  promoteDraft(request: DraftPromotionRequest): Observable<DraftPromotionResult> {
    return this.http.post<DraftPromotionResult>(`${this.baseUrl}/drafts/promote`, request);
  }

  importToDatabase(file: File, userDatabaseId: string): Observable<void> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('userDatabaseId', userDatabaseId);
    return this.http.post<void>(`${this.baseUrl}/import-to-database-file`, formData);
  }

  getDraftImportProgress(): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/drafts/import-progress`);
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