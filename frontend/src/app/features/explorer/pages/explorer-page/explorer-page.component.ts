import { Component, ElementRef, HostListener, ViewChild, Input, effect, inject, signal, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom, forkJoin, Subscription } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ChessboardComponent } from '../../components/chessboard/chessboard.component';
import { GamesListComponent } from '../../components/games-list/games-list.component';
import { GamesTreeComponent } from '../../components/games-tree/games-tree.component';
import { MoveListComponent } from '../../components/move-list/move-list.component';
import { EmptyGamesStateComponent } from '../../components/empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../../components/filters-panel/filters-panel.component';
import { DatabasesPanelComponent } from '../../components/databases-panel/databases-panel.component';
import { GamesTableComponent } from '../../components/games-table/games-table.component';
import { MoveRow } from '../../components/move-list/move-list.component';
import { AuthStateService } from '../../../../core/auth/auth-state.service';
import { UserDatabasesApiService } from '../../services/user-databases-api.service';
import { Database } from '../../components/databases-panel/databases-panel.component';
import {
  DraftGameListItem,
  DraftGamesResultSortMode,
  DraftGamesSortBy,
  DraftGamesSortDirection,
  DraftImportApiService,
  DraftImportResult
} from '../../services/draft-import-api.service';
import { DraftImportProgressService, DraftImportProgressUpdate } from '../../services/draft-import-progress.service';

@Component({
  selector: 'app-explorer-page',
  standalone: true,
  imports: [CommonModule, ChessboardComponent, GamesTreeComponent, GamesListComponent, MoveListComponent, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent],
  templateUrl: './explorer-page.component.html',
  styleUrl: './explorer-page.component.scss'
})
export class ExplorerPageComponent implements OnDestroy {
  private readonly authState = inject(AuthStateService);
  private readonly userDatabasesApi = inject(UserDatabasesApiService);
  private readonly draftImportApi = inject(DraftImportApiService);
  private readonly draftImportProgress = inject(DraftImportProgressService);

  private readonly loadedForCurrentSession = signal(false);
  private progressSubscription: Subscription | null = null;

  @Input() isFocusMode = false;

  @ViewChild('layoutRoot', { static: true })
  private readonly layoutRoot!: ElementRef<HTMLElement>;

  @ViewChild('pgnFileInput')
  private readonly pgnFileInput?: ElementRef<HTMLInputElement>;

  protected gamesLoaded = false;
  protected readonly isImporting = signal(false);
  protected readonly importProgress = signal<DraftImportProgressUpdate | null>(null);
  protected readonly importError = signal<string | null>(null);
  protected readonly importErrorVisible = signal(false);
  protected readonly draftGames = signal<DraftGameListItem[]>([]);
  protected readonly draftGamesTotalCount = signal(0);
  protected readonly draftGamesPage = signal(1);
  protected readonly draftGamesPageSize = signal(18);
  protected readonly draftGamesResultSortMode = signal<DraftGamesResultSortMode>('default');
  protected readonly draftGamesSortBy = signal<DraftGamesSortBy>('createdAt');
  protected readonly draftGamesSortDirection = signal<DraftGamesSortDirection>('desc');
  protected currentDatabaseName = 'Games';
  protected currentGamesSource: 'imported' | 'external' = 'imported';
  protected readonly myDatabases = signal<Array<{ id: string; name: string }>>([]);
  protected readonly panelDatabases = signal<Database[]>([]);
  protected readonly currentUserName = this.authState.userName;
  protected moveRows: MoveRow[] = [];
  protected currentPly = 0;
  protected navigationRequest: { ply: number; version: number } | null = null;
  private navigationVersion = 0;
  private importErrorTimerId: number | null = null;
  private importErrorClearTimerId: number | null = null;
  protected mockGames: any[] = [
    {
      year: 2023,
      white: 'Carlsen, M.',
      whiteElo: 2853,
      result: '1-0',
      black: 'Nakamura, H.',
      blackElo: 2789,
      eco: 'C65',
      event: 'Norway Chess',
      moveCount: 42
    },
    {
      year: 2023,
      white: 'Caruana, F.',
      whiteElo: 2782,
      result: '1/2-1/2',
      black: 'Ding, L.',
      blackElo: 2788,
      eco: 'D37',
      event: 'World Championship',
      moveCount: 68
    },
    {
      year: 2024,
      white: 'Fiorito, F.',
      whiteElo: 2470,
      result: '0-1',
      black: 'Carlsen, M.',
      blackElo: 2832,
      eco: 'D31',
      event: 'World Blitz',
      moveCount: 38
    }
  ];
  protected isResizing = false;
  protected leftPaneWidth = 620;

  protected focusRightTab: 'databases' | 'tree' | 'moves' | 'games' | 'filters' = 'tree';

  private static readonly minBoardWidth = 320;
  private static readonly minMoveListWidth = 145;
  private static readonly boardMoveGap = 12;
  private static readonly rightColumnMinWidth = 390;
  private static readonly handleWidth = 8;

  constructor() {
    effect(() => {
      if (!this.authState.isAuthenticated()) {
        this.loadedForCurrentSession.set(false);
        this.myDatabases.set([]);
        this.panelDatabases.set([]);
        this.detachProgressSubscription();
        void this.draftImportProgress.disconnect();
        return;
      }

      void this.draftImportProgress.connect();

      if (this.loadedForCurrentSession()) {
        return;
      }

      this.loadUserDatabases();
    });
  }

  ngOnDestroy(): void {
    this.clearImportErrorTimers();
    this.detachProgressSubscription();
  }

  protected startResize(event: MouseEvent): void {
    event.preventDefault();
    this.isResizing = true;
  }

  @HostListener('window:mousemove', ['$event'])
  protected onWindowMouseMove(event: MouseEvent): void {
    if (!this.isResizing) {
      return;
    }

    const layoutBounds = this.layoutRoot.nativeElement.getBoundingClientRect();
    const requestedLeftWidth = event.clientX - layoutBounds.left;
    const minLeftWidth =
      ExplorerPageComponent.minBoardWidth +
      ExplorerPageComponent.minMoveListWidth +
      ExplorerPageComponent.boardMoveGap;

    const computedStyles = window.getComputedStyle(this.layoutRoot.nativeElement);
    const gridGap = Number.parseFloat(computedStyles.columnGap) || 0;
    const totalLayoutGaps = gridGap * 2;
    const maxLeftWidth = Math.max(
      minLeftWidth,
      layoutBounds.width -
      ExplorerPageComponent.rightColumnMinWidth -
      ExplorerPageComponent.handleWidth -
      totalLayoutGaps
    );

    this.leftPaneWidth = Math.min(Math.max(requestedLeftWidth, minLeftWidth), maxLeftWidth);
  }

  @HostListener('window:mouseup')
  protected onWindowMouseUp(): void {
    this.isResizing = false;
  }

  protected importDatabase(): void {
    this.clearImportError();
    this.pgnFileInput?.nativeElement.click();
  }

  protected searchCommunityDatabase(): void {
    // Placeholder action for searching a remote community database.
    console.log('Search database (community database)');
    this.gamesLoaded = true;
    this.currentDatabaseName = 'Community Database';
    this.currentGamesSource = 'external';
  }

  protected saveCurrentDatabase(): void {
    // The save modal handles whether this becomes merge or create.
  }

  protected async onSaveDatabaseRequest(payload: {
    mode: 'merge' | 'create';
    targetDatabaseId?: string;
    newDatabaseName?: string;
    visibility: 'private' | 'public';
  }): Promise<void> {
    this.clearImportError();

    try {
      let userDatabaseId = payload.targetDatabaseId;

      if (payload.mode === 'create') {
        const created = await firstValueFrom(
          this.userDatabasesApi.create({
            name: payload.newDatabaseName ?? 'Imported Games',
            isPublic: payload.visibility === 'public'
          })
        );

        userDatabaseId = created.id;
      }

      if (!userDatabaseId) {
        this.showImportError('Choose a target database before saving.');
        return;
      }

      await firstValueFrom(this.draftImportApi.promoteDraft({ userDatabaseId }));
      this.currentDatabaseName = payload.mode === 'create'
        ? (payload.newDatabaseName ?? 'Imported Games')
        : (this.myDatabases().find(db => db.id === userDatabaseId)?.name ?? 'Saved Database');

      await this.loadDraftGamesPage();
      await this.reloadUserDatabases();
    } catch {
      this.showImportError('Saving imported games failed. Please try again.');
    }
  }

  protected addCurrentDatabaseBookmark(): void {
    console.log('Bookmark external user database');
  }

  protected onMoveRowsChanged(moveRows: MoveRow[]): void {
    this.moveRows = moveRows;
  }

  protected onCurrentPlyChanged(ply: number): void {
    this.currentPly = ply;
  }

  protected onPlySelected(ply: number): void {
    this.navigationVersion++;
    this.navigationRequest = { ply, version: this.navigationVersion };
  }

  protected onDraftGamesSortChanged(payload: { sortBy: DraftGamesSortBy; sortDirection: DraftGamesSortDirection }): void {
    this.draftGamesSortBy.set(payload.sortBy);
    this.draftGamesSortDirection.set(payload.sortDirection);

    if (payload.sortBy !== 'result') {
      this.draftGamesResultSortMode.set('default');
    }

    this.draftGamesPage.set(1);
    void this.loadDraftGamesPage();
  }

  protected onDraftGamesResultSortModeChanged(resultSortMode: DraftGamesResultSortMode): void {
    this.draftGamesResultSortMode.set(resultSortMode);
    this.draftGamesPage.set(1);
    void this.loadDraftGamesPage();
  }

  protected onDraftGamesPageSizeChanged(pageSize: number): void {
    this.draftGamesPageSize.set(pageSize);
    this.draftGamesPage.set(1);
    void this.loadDraftGamesPage();
  }

  protected onDraftGamesPageChanged(page: number): void {
    this.draftGamesPage.set(page);
    void this.loadDraftGamesPage();
  }

  protected async onPgnFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file) {
      return;
    }

    const lowerName = file.name.toLowerCase();
    if (!lowerName.endsWith('.pgn')) {
      this.showImportError('Select a valid .pgn file.');
      return;
    }

    const pgn = await file.text();
    await this.runDraftImport(pgn);
  }

  private async runDraftImport(pgn: string): Promise<void> {
    this.isImporting.set(true);
    this.clearImportError();
    this.importProgress.set({
      parsedCount: 0,
      importedCount: 0,
      skippedCount: 0,
      isCompleted: false,
      isFailed: false,
      message: 'Uploading file...'
    });
    this.draftImportProgress.reset();

    let progressConnected = false;

    try {
      try {
        await this.draftImportProgress.connect();
        progressConnected = true;
        this.detachProgressSubscription();
        this.progressSubscription = this.draftImportProgress.updates$.subscribe(update => {
          if (update) {
            this.importProgress.set(update);
          }
        });
      } catch {
        // Import should still work even if live progress transport is unavailable.
        progressConnected = false;
      }

      const result = await firstValueFrom(this.draftImportApi.importDraft({ pgn }));
      this.applyImportedDraftState(result);
    } catch (error) {
      this.showImportError(this.resolveImportErrorMessage(error, progressConnected));
    } finally {
      this.isImporting.set(false);
      this.detachProgressSubscription();
    }
  }

  private applyImportedDraftState(result: DraftImportResult): void {
    this.gamesLoaded = result.importedCount > 0;
    this.currentDatabaseName = 'Imported Draft';
    this.currentGamesSource = 'imported';
    this.draftGamesPage.set(1);
    void this.loadDraftGamesPage();

    if (!result.importedCount && result.skippedCount > 0) {
      this.showImportError('No games were imported. All parsed games were skipped.');
    }
  }

  private detachProgressSubscription(): void {
    this.progressSubscription?.unsubscribe();
    this.progressSubscription = null;
  }

  private async loadDraftGamesPage(): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.draftImportApi.getDraftGames(
          this.draftGamesPage(),
          this.draftGamesPageSize(),
          this.draftGamesSortBy(),
          this.draftGamesSortDirection(),
          this.draftGamesResultSortMode()
        )
      );

      this.draftGames.set(response.items);
      this.draftGamesTotalCount.set(response.totalCount);
      this.gamesLoaded = response.totalCount > 0;
    } catch {
      this.showImportError('Unable to load imported draft games.');
    }
  }

  private resolveImportErrorMessage(error: unknown, progressConnected: boolean): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'Import failed: you are not authenticated. Please sign in again.';
      }

      if (error.status === 0) {
        return 'Import failed: backend is unreachable.';
      }

      if (typeof error.error === 'string' && error.error.trim().length > 0) {
        return `Import failed: ${error.error}`;
      }

      return `Import failed with status ${error.status}.`;
    }

    if (!progressConnected) {
      return 'Import failed. Live progress could not connect, and the import request did not complete.';
    }

    return 'Import failed. Please try again.';
  }

  private loadUserDatabases(): void {
    forkJoin({
      mine: this.userDatabasesApi.getMine(),
      bookmarks: this.userDatabasesApi.getBookmarks()
    }).subscribe({
      next: ({ mine, bookmarks }) => {
        this.loadedForCurrentSession.set(true);
        this.myDatabases.set(mine.map(db => ({ id: db.id, name: db.name })));

        const mineMapped: Database[] = mine.map(db => ({
          id: db.id,
          name: db.name,
          owner: this.currentUserName() ?? db.ownerUserId,
          creationDate: new Date(db.createdAtUtc),
          gamesCount: db.gamesCount
        }));

        const bookmarkMapped: Database[] = bookmarks
          .filter(bookmark => !mine.some(owned => owned.id === bookmark.id))
          .map(bookmark => ({
            id: bookmark.id,
            name: bookmark.name,
            owner: bookmark.ownerUserId,
            creationDate: new Date(bookmark.createdAtUtc),
            gamesCount: bookmark.gamesCount
          }));

        this.panelDatabases.set([...mineMapped, ...bookmarkMapped]);
      },
      error: () => {
        this.loadedForCurrentSession.set(false);
      }
    });
  }

  private async reloadUserDatabases(): Promise<void> {
    try {
      const [mine, bookmarks] = await Promise.all([
        firstValueFrom(this.userDatabasesApi.getMine()),
        firstValueFrom(this.userDatabasesApi.getBookmarks())
      ]);

      this.myDatabases.set(mine.map(db => ({ id: db.id, name: db.name })));

      const mineMapped: Database[] = mine.map(db => ({
        id: db.id,
        name: db.name,
        owner: this.currentUserName() ?? db.ownerUserId,
        creationDate: new Date(db.createdAtUtc),
        gamesCount: db.gamesCount
      }));

      const bookmarkMapped: Database[] = bookmarks
        .filter(bookmark => !mine.some(owned => owned.id === bookmark.id))
        .map(bookmark => ({
          id: bookmark.id,
          name: bookmark.name,
          owner: bookmark.ownerUserId,
          creationDate: new Date(bookmark.createdAtUtc),
          gamesCount: bookmark.gamesCount
        }));

      this.panelDatabases.set([...mineMapped, ...bookmarkMapped]);
    } catch {
      this.showImportError('Unable to refresh user databases after save.');
    }
  }

  private showImportError(message: string): void {
    this.clearImportErrorTimers();
    this.importError.set(message);
    this.importErrorVisible.set(true);

    this.importErrorTimerId = window.setTimeout(() => {
      this.importErrorVisible.set(false);

      this.importErrorClearTimerId = window.setTimeout(() => {
        this.importError.set(null);
      }, 300);
    }, 4200);
  }

  private clearImportError(): void {
    this.clearImportErrorTimers();
    this.importErrorVisible.set(false);
    this.importError.set(null);
  }

  private clearImportErrorTimers(): void {
    if (this.importErrorTimerId !== null) {
      window.clearTimeout(this.importErrorTimerId);
      this.importErrorTimerId = null;
    }

    if (this.importErrorClearTimerId !== null) {
      window.clearTimeout(this.importErrorClearTimerId);
      this.importErrorClearTimerId = null;
    }
  }

}
