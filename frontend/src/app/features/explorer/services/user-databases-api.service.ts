import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  DraftGamesPageResponse,
  DraftGamesResultSortMode,
  DraftGamesSortBy,
  DraftGamesSortDirection
} from './draft-import-api.service';
import { ExplorerGamesFiltersQuery } from './games-filters.models';
import { GameReplayResponse } from './game-replay.models';

export interface UserDatabaseDto {
  id: string;
  name: string;
  isPublic: boolean;
  ownerUserId: string;
  ownerUserName: string;
  gameCount: number;
  createdAtUtc: string;
}

/**
 * One row of the databases panel. The backend returns the same public set to signed-out
 * and signed-in callers, so signing in only ever adds the caller's own private databases.
 */
export interface UserDatabaseListItemDto extends UserDatabaseDto {
  /** When the database's games last changed - not renames or visibility edits. */
  contentUpdatedAtUtc: string;
  isOwner: boolean;
  isBookmarked: boolean;
}

export interface AddGamesFromSelectionRequest {
  /** Omit to take games from the caller's draft. */
  sourceUserDatabaseId?: string;
  /** An explicit tick-box selection; when omitted the whole filtered set is added. */
  gameIds?: string[];
  filters: ExplorerGamesFiltersQuery;
}

export interface AddGamesFromSelectionResponse {
  addedCount: number;
  skippedCount: number;
  totalMatched: number;
  savedGamesUsed: number;
  savedGamesLimit: number;
}

export interface CreateUserDatabaseRequest {
  name: string;
  isPublic: boolean;
}

export interface UpdateUserDatabaseRequest {
  name: string;
  isPublic: boolean;
}

@Injectable({ providedIn: 'root' })
export class UserDatabasesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getVisible(): Observable<UserDatabaseListItemDto[]> {
    return this.http.get<UserDatabaseListItemDto[]>(`${this.baseUrl}/user-databases`);
  }

  addBookmark(userDatabaseId: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/user-databases/${userDatabaseId}/bookmark`, {});
  }

  removeBookmark(userDatabaseId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/user-databases/${userDatabaseId}/bookmark`);
  }

  addGamesFromSelection(
    userDatabaseId: string,
    request: AddGamesFromSelectionRequest
  ): Observable<AddGamesFromSelectionResponse> {
    return this.http.post<AddGamesFromSelectionResponse>(
      `${this.baseUrl}/user-databases/${userDatabaseId}/games/from-selection`,
      request
    );
  }

  removeGames(userDatabaseId: string, gameIds: string[]): Observable<{ removedCount: number; deletedOrphanCount: number }> {
    return this.http.post<{ removedCount: number; deletedOrphanCount: number }>(
      `${this.baseUrl}/user-databases/${userDatabaseId}/games/remove`,
      { gameIds }
    );
  }

  create(request: CreateUserDatabaseRequest): Observable<UserDatabaseDto> {
    return this.http.post<UserDatabaseDto>(`${this.baseUrl}/user-databases`, request);
  }

  delete(userDatabaseId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/user-databases/${userDatabaseId}`);
  }

  update(userDatabaseId: string, request: UpdateUserDatabaseRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/user-databases/${userDatabaseId}`, request);
  }

  getGames(
    userDatabaseId: string,
    page: number,
    pageSize: number,
    sortBy: DraftGamesSortBy,
    sortDirection: DraftGamesSortDirection,
    resultSortMode: DraftGamesResultSortMode,
    filters?: ExplorerGamesFiltersQuery
  ): Observable<DraftGamesPageResponse> {
    return this.http.get<DraftGamesPageResponse>(`${this.baseUrl}/user-databases/${userDatabaseId}/games`, {
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

  getGameReplay(userDatabaseId: string, gameId: string): Observable<GameReplayResponse> {
    return this.http.get<GameReplayResponse>(`${this.baseUrl}/user-databases/${userDatabaseId}/games/${gameId}`);
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
