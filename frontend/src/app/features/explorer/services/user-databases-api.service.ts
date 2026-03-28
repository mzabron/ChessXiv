import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  DraftGamesPageResponse,
  DraftGamesResultSortMode,
  DraftGamesSortBy,
  DraftGamesSortDirection
} from './draft-import-api.service';
import { GameReplayResponse } from './game-replay.models';

export interface UserDatabaseDto {
  id: string;
  name: string;
  isPublic: boolean;
  ownerUserId: string;
  gameCount: number;
  createdAtUtc: string;
}

export interface BookmarkedUserDatabaseDto extends UserDatabaseDto {
  bookmarkedAtUtc: string;
}

export interface CreateUserDatabaseRequest {
  name: string;
  isPublic: boolean;
}

@Injectable({ providedIn: 'root' })
export class UserDatabasesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = this.resolveBaseUrl();

  getMine(): Observable<UserDatabaseDto[]> {
    return this.http.get<UserDatabaseDto[]>(`${this.baseUrl}/user-databases/mine`);
  }

  getBookmarks(): Observable<BookmarkedUserDatabaseDto[]> {
    return this.http.get<BookmarkedUserDatabaseDto[]>(`${this.baseUrl}/user-databases/bookmarks`);
  }

  create(request: CreateUserDatabaseRequest): Observable<UserDatabaseDto> {
    return this.http.post<UserDatabaseDto>(`${this.baseUrl}/user-databases`, request);
  }

  delete(userDatabaseId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/user-databases/${userDatabaseId}`);
  }

  getGames(
    userDatabaseId: string,
    page: number,
    pageSize: number,
    sortBy: DraftGamesSortBy,
    sortDirection: DraftGamesSortDirection,
    resultSortMode: DraftGamesResultSortMode
  ): Observable<DraftGamesPageResponse> {
    return this.http.get<DraftGamesPageResponse>(`${this.baseUrl}/user-databases/${userDatabaseId}/games`, {
      params: {
        page,
        pageSize,
        sortBy,
        sortDirection,
        resultSortMode
      }
    });
  }

  getGameReplay(userDatabaseId: string, gameId: string): Observable<GameReplayResponse> {
    return this.http.get<GameReplayResponse>(`${this.baseUrl}/user-databases/${userDatabaseId}/games/${gameId}`);
  }

  private resolveBaseUrl(): string {
    const host = window.location.hostname;
    const isLocalHost = host === 'localhost' || host === '127.0.0.1' || host === '::1';

    if (isLocalHost) {
      return `http://${host}:5027/api`;
    }

    return '/api';
  }
}
