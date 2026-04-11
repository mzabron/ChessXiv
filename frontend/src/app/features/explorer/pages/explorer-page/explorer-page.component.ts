import { Component, ElementRef, HostListener, ViewChild, Input, effect, inject, signal, OnDestroy, AfterViewInit } from '@angular/core';
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
import { GameReplayResponse } from '../../services/game-replay.models';
import {
  ExplorerGamesFilterState,
  createDefaultExplorerGamesFilterState,
  toExplorerGamesFiltersQuery,
  toExplorerMoveTreeFiltersPayload
} from '../../services/games-filters.models';
import {
  ExplorerBoardApiService,
  ExplorerMoveTreeRequest,
  ExplorerMoveTreeResponse
} from '../../services/explorer-board-api.service';

@Component({
  selector: 'app-explorer-page',
  standalone: true,
  imports: [CommonModule, ChessboardComponent, GamesTreeComponent, GamesListComponent, MoveListComponent, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent],
  templateUrl: './explorer-page.component.html',
  styleUrl: './explorer-page.component.scss'
})
export class ExplorerPageComponent implements OnDestroy, AfterViewInit {
  private static readonly initialBoardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  private readonly authState = inject(AuthStateService);
  private readonly userDatabasesApi = inject(UserDatabasesApiService);
  private readonly draftImportApi = inject(DraftImportApiService);
  private readonly draftImportProgress = inject(DraftImportProgressService);
  private readonly explorerBoardApi = inject(ExplorerBoardApiService);

  private readonly loadedForCurrentSession = signal(false);
  protected readonly activeUserDatabaseId = signal<string | null>(null);
  private static readonly activeDatabaseStorageKey = 'chessxiv.explorer.active-user-database';
  private progressSubscription: Subscription | null = null;

  @Input() isFocusMode = false;

  @ViewChild('layoutRoot', { static: true })
  private readonly layoutRoot!: ElementRef<HTMLElement>;

  @ViewChild('pgnFileInput')
  private readonly pgnFileInput?: ElementRef<HTMLInputElement>;

  @ViewChild('mainChessboard', { read: ElementRef })
  private readonly mainChessboardRef?: ElementRef<HTMLElement>;

  @ViewChild('mainMoveList', { read: ElementRef })
  private readonly mainMoveListRef?: ElementRef<HTMLElement>;

  @ViewChild('boardMovesRow', { read: ElementRef })
  private readonly boardMovesRowRef?: ElementRef<HTMLElement>;

  @ViewChild('focusTabs', { read: ElementRef })
  private readonly focusTabsRef?: ElementRef<HTMLElement>;

  protected gamesLoaded = false;
  protected readonly isImporting = signal(false);
  protected readonly isSavingDraft = signal(false);
  protected readonly saveDraftCompleted = signal(false);
  protected readonly importProgress = signal<DraftImportProgressUpdate | null>(null);
  protected readonly importError = signal<string | null>(null);
  protected readonly importErrorVisible = signal(false);
  protected readonly deleteConfirmationVisible = signal(false);
  protected readonly deleteConfirmationKind = signal<'draft' | 'database' | null>(null);
  protected readonly deleteConfirmationDatabaseName = signal('');
  protected readonly draftGames = signal<DraftGameListItem[]>([]);
  protected readonly draftGamesTotalCount = signal(0);
  protected readonly draftGamesPage = signal(1);
  protected readonly draftGamesPageSize = signal(18);
  protected readonly draftGamesResultSortMode = signal<DraftGamesResultSortMode>('default');
  protected readonly draftGamesSortBy = signal<DraftGamesSortBy>('createdAt');
  protected readonly draftGamesSortDirection = signal<DraftGamesSortDirection>('desc');
  protected readonly gamesFilters = signal<ExplorerGamesFilterState>(createDefaultExplorerGamesFilterState());
  protected readonly boardFen = signal('');
  protected readonly moveTreeData = signal<ExplorerMoveTreeResponse | null>(null);
  protected readonly moveTreeLoading = signal(false);
  protected readonly moveTreeError = signal<string | null>(null);
  protected currentDatabaseName = 'Games';
  protected currentGamesSource: 'imported' | 'external' | 'userDatabase' = 'imported';
  protected readonly selectedGameId = signal<string | null>(null);
  protected readonly selectedGameReplay = signal<GameReplayResponse | null>(null);
  protected readonly boardSanMoveRequest = signal<{ san: string; version: number } | null>(null);
  protected readonly myDatabases = signal<Array<{ id: string; name: string }>>([]);
  protected readonly panelDatabases = signal<Database[]>([]);
  protected readonly currentUserName = this.authState.userName;
  protected moveRows: MoveRow[] = [];
  protected currentPly = 0;
  protected navigationRequest: { ply: number; version: number } | null = null;
  private navigationVersion = 0;
  private moveTreeRequestVersion = 0;
  private sanMoveRequestVersion = 0;
  private importErrorTimerId: number | null = null;
  private importErrorClearTimerId: number | null = null;
  private pendingDeleteDatabase: Database | null = null;
  private boardMovesResizeObserver: ResizeObserver | null = null;
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
        this.activeUserDatabaseId.set(null);
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
    this.boardMovesResizeObserver?.disconnect();
  }

  ngAfterViewInit(): void {
    this.initBoardMovesHeightSync();
  }

  private initBoardMovesHeightSync(): void {
    const boardElement = this.mainChessboardRef?.nativeElement;
    const rowElement = this.boardMovesRowRef?.nativeElement;
    const moveListElement = this.mainMoveListRef?.nativeElement;

    if (!boardElement || !rowElement || !moveListElement) {
      return;
    }

    const syncHeight = (): void => {
      const boardHeight = Math.round(boardElement.getBoundingClientRect().height);
      if (boardHeight <= 0) {
        return;
      }

      rowElement.style.setProperty('--board-moves-height', `${boardHeight}px`);
      moveListElement.style.setProperty('height', `${boardHeight}px`);
    };

    syncHeight();
    this.boardMovesResizeObserver?.disconnect();
    this.boardMovesResizeObserver = new ResizeObserver(() => syncHeight());
    this.boardMovesResizeObserver.observe(boardElement);
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

  protected scrollFocusTabs(direction: 'left' | 'right'): void {
    const tabsElement = this.focusTabsRef?.nativeElement;
    if (!tabsElement) {
      return;
    }

    const amount = Math.max(96, Math.floor(tabsElement.clientWidth * 0.55));
    const delta = direction === 'left' ? -amount : amount;
    tabsElement.scrollBy({ left: delta, behavior: 'smooth' });
  }

  protected importDatabase(): void {
    this.clearImportError();
    this.pgnFileInput?.nativeElement.click();
  }

  protected searchCommunityDatabase(): void {
    // Placeholder action for searching a remote community database.
    console.log('Search database (community database)');
    this.activeUserDatabaseId.set(null);
    this.clearPersistedActiveDatabase();
    this.gamesLoaded = true;
    this.currentDatabaseName = 'Community Database';
    this.currentGamesSource = 'external';
    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.moveRows = [];
    this.currentPly = 0;
    this.clearMoveTree();
  }

  protected onDatabaseSelected(database: Database): void {
    if (this.isFocusMode) {
      this.focusRightTab = 'games';
    }

    void this.openUserDatabase(database);
  }

  protected closeCurrentDatabase(): void {
    if (this.currentGamesSource === 'imported' && this.gamesLoaded) {
      this.openDeleteConfirmation('draft');
      return;
    }

    void this.handleCloseCurrentDatabase();
  }

  protected onDatabaseDeleteRequested(database: Database): void {
    if (database.owner !== (this.currentUserName() ?? '')) {
      return;
    }

    this.pendingDeleteDatabase = database;
    this.deleteConfirmationDatabaseName.set(database.name);
    this.openDeleteConfirmation('database');
  }

  protected cancelDeleteConfirmation(): void {
    this.pendingDeleteDatabase = null;
    this.deleteConfirmationVisible.set(false);
    this.deleteConfirmationKind.set(null);
    this.deleteConfirmationDatabaseName.set('');
  }

  protected confirmDeleteConfirmation(): void {
    const kind = this.deleteConfirmationKind();
    const pendingDatabase = this.pendingDeleteDatabase;
    this.cancelDeleteConfirmation();

    if (kind === 'draft') {
      void this.handleCloseCurrentDatabase();
      return;
    }

    if (kind === 'database' && pendingDatabase) {
      void this.deleteDatabase(pendingDatabase);
    }
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
    if (this.isSavingDraft()) {
      return;
    }

    this.clearImportError();
    this.saveDraftCompleted.set(false);
    this.isSavingDraft.set(true);

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
      await this.reloadUserDatabases();

      const selectedDatabase = this.panelDatabases().find(db => db.id === userDatabaseId)
        ?? {
        id: userDatabaseId,
        name: payload.mode === 'create'
          ? (payload.newDatabaseName ?? 'Imported Games')
          : (this.myDatabases().find(db => db.id === userDatabaseId)?.name ?? 'Saved Database'),
        owner: this.currentUserName() ?? '',
        creationDate: new Date(),
        gamesCount: 0
      };

      await this.openUserDatabase(selectedDatabase);
      this.saveDraftCompleted.set(true);
    } catch (error) {
      if (error instanceof HttpErrorResponse && payload.mode === 'create') {
        const rawMessage = this.extractErrorMessage(error);
        if (error.status === 409 || rawMessage.toLowerCase().includes('already exists')) {
          this.showImportError('You already have a database with this name.');
          return;
        }
      }

      this.showImportError('Saving imported games failed. Please try again.');
    } finally {
      this.isSavingDraft.set(false);
    }
  }

  protected closeSaveDraftModal(): void {
    this.saveDraftCompleted.set(false);
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

  protected onBoardFenChanged(fen: string): void {
    this.boardFen.set(fen ?? '');
    void this.loadMoveTree();
  }

  protected onPlySelected(ply: number): void {
    this.navigationVersion++;
    this.navigationRequest = { ply, version: this.navigationVersion };
  }

  protected onTreeMoveSelected(san: string): void {
    const normalizedSan = san.trim();
    if (!normalizedSan) {
      return;
    }

    this.sanMoveRequestVersion++;
    this.boardSanMoveRequest.set({ san: normalizedSan, version: this.sanMoveRequestVersion });
  }

  protected onDraftGamesSortChanged(payload: { sortBy: DraftGamesSortBy; sortDirection: DraftGamesSortDirection }): void {
    this.draftGamesSortBy.set(payload.sortBy);
    this.draftGamesSortDirection.set(payload.sortDirection);

    if (payload.sortBy !== 'result') {
      this.draftGamesResultSortMode.set('default');
    }

    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    void this.loadCurrentGamesPage();
  }

  protected onDraftGamesResultSortModeChanged(resultSortMode: DraftGamesResultSortMode): void {
    this.draftGamesResultSortMode.set(resultSortMode);
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    void this.loadCurrentGamesPage();
  }

  protected onDraftGamesPageSizeChanged(pageSize: number): void {
    this.draftGamesPageSize.set(pageSize);
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    void this.loadCurrentGamesPage();
  }

  protected onDraftGamesPageChanged(page: number): void {
    this.draftGamesPage.set(page);
    this.syncFilterStateFromListControls();
    void this.loadCurrentGamesPage();
  }

  protected onGamesFiltersChanged(filters: ExplorerGamesFilterState): void {
    this.gamesFilters.set(filters);
  }

  protected onGamesFiltersApplied(filters: ExplorerGamesFilterState): void {
    this.gamesFilters.set(filters);
    this.draftGamesSortBy.set(filters.sortBy);
    this.draftGamesSortDirection.set(filters.sortDirection);

    if (filters.sortBy !== 'result') {
      this.draftGamesResultSortMode.set('default');
    }

    this.draftGamesPageSize.set(filters.pageSize);
    this.draftGamesPage.set(filters.page);
    this.syncFilterStateFromListControls();

    if (this.isFocusMode) {
      this.focusRightTab = 'games';
    }

    void this.reloadGamesAndMoveTree();
  }

  protected onGamesFiltersReset(): void {
    const cleared = createDefaultExplorerGamesFilterState({
      sortBy: this.draftGamesSortBy(),
      sortDirection: this.draftGamesSortDirection(),
      page: 1,
      pageSize: this.draftGamesPageSize()
    });

    this.gamesFilters.set(cleared);
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    void this.reloadGamesAndMoveTree();
  }

  protected onGameSelected(game: DraftGameListItem): void {
    void this.loadSelectedGameReplay(game);
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
        await Promise.race([
          this.draftImportProgress.connect(),
          new Promise((_, reject) => setTimeout(() => reject(new Error('SignalR timeout')), 5000))
        ]);
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
    void this.reloadGamesAndMoveTree();

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
      const hadLoadedImportedSource = this.currentGamesSource === 'imported' && this.gamesLoaded;
      const filters = toExplorerGamesFiltersQuery(this.gamesFilters());
      const response = await firstValueFrom(
        this.draftImportApi.getDraftGames(
          this.draftGamesPage(),
          this.draftGamesPageSize(),
          this.draftGamesSortBy(),
          this.draftGamesSortDirection(),
          this.draftGamesResultSortMode(),
          filters
        )
      );

      this.draftGames.set(response.items);
      this.draftGamesTotalCount.set(response.totalCount);
      this.gamesLoaded = response.totalCount > 0 || hadLoadedImportedSource;
      this.clearSelectedGameIfMissing(response.items);
    } catch {
      this.showImportError('Unable to load imported draft games.');
    }
  }

  private async loadUserDatabaseGamesPage(databaseId: string): Promise<void> {
    try {
      const filters = toExplorerGamesFiltersQuery(this.gamesFilters());
      const response = await firstValueFrom(
        this.userDatabasesApi.getGames(
          databaseId,
          this.draftGamesPage(),
          this.draftGamesPageSize(),
          this.draftGamesSortBy(),
          this.draftGamesSortDirection(),
          this.draftGamesResultSortMode(),
          filters
        )
      );

      this.draftGames.set(response.items);
      this.draftGamesTotalCount.set(response.totalCount);
      this.gamesLoaded = true;
      this.clearSelectedGameIfMissing(response.items);
    } catch {
      this.showImportError('Unable to load games from selected database.');
    }
  }

  private async loadCurrentGamesPage(): Promise<void> {
    if (this.currentGamesSource === 'userDatabase') {
      const userDatabaseId = this.activeUserDatabaseId();
      if (!userDatabaseId) {
        this.gamesLoaded = false;
        this.draftGames.set([]);
        this.draftGamesTotalCount.set(0);
        return;
      }

      await this.loadUserDatabaseGamesPage(userDatabaseId);
      return;
    }

    if (this.currentGamesSource === 'imported') {
      await this.loadDraftGamesPage();
    }
  }

  private async reloadGamesAndMoveTree(): Promise<void> {
    await this.loadCurrentGamesPage();
    await this.loadMoveTree();
  }

  private async loadSelectedGameReplay(game: DraftGameListItem): Promise<void> {
    try {
      let replay: GameReplayResponse | null = null;

      if (this.currentGamesSource === 'userDatabase') {
        const userDatabaseId = this.activeUserDatabaseId();
        if (!userDatabaseId) {
          return;
        }

        replay = await firstValueFrom(this.userDatabasesApi.getGameReplay(userDatabaseId, game.id));
      } else if (this.currentGamesSource === 'imported') {
        replay = await firstValueFrom(this.draftImportApi.getDraftGameReplay(game.id));
      }

      if (!replay) {
        return;
      }

      this.selectedGameId.set(game.id);
      this.selectedGameReplay.set(replay);
      this.navigationVersion++;
      this.navigationRequest = { ply: 0, version: this.navigationVersion };
    } catch {
      this.showImportError('Unable to load selected game replay.');
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

  private extractErrorMessage(error: HttpErrorResponse): string {
    if (typeof error.error === 'string') {
      return error.error;
    }

    if (error.error && typeof error.error === 'object' && 'message' in error.error) {
      const message = (error.error as { message?: unknown }).message;
      if (typeof message === 'string') {
        return message;
      }
    }

    return '';
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
          gamesCount: db.gameCount
        }));

        const bookmarkMapped: Database[] = bookmarks
          .filter(bookmark => !mine.some(owned => owned.id === bookmark.id))
          .map(bookmark => ({
            id: bookmark.id,
            name: bookmark.name,
            owner: bookmark.ownerUserId,
            creationDate: new Date(bookmark.createdAtUtc),
            gamesCount: bookmark.gameCount
          }));

        const allDatabases = [...mineMapped, ...bookmarkMapped];
        this.panelDatabases.set(allDatabases);
        void this.initializeGamesSourceForSession(allDatabases);
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
        gamesCount: db.gameCount
      }));

      const bookmarkMapped: Database[] = bookmarks
        .filter(bookmark => !mine.some(owned => owned.id === bookmark.id))
        .map(bookmark => ({
          id: bookmark.id,
          name: bookmark.name,
          owner: bookmark.ownerUserId,
          creationDate: new Date(bookmark.createdAtUtc),
          gamesCount: bookmark.gameCount
        }));

      this.panelDatabases.set([...mineMapped, ...bookmarkMapped]);
    } catch {
      this.showImportError('Unable to refresh user databases after save.');
    }
  }

  private async openUserDatabase(database: Database): Promise<void> {
    this.clearImportError();
    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.moveRows = [];
    this.currentPly = 0;
    this.activeUserDatabaseId.set(database.id);
    this.currentDatabaseName = database.name;
    this.currentGamesSource = 'userDatabase';
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    this.persistActiveDatabase(database);
    await this.reloadGamesAndMoveTree();
  }

  private async initializeGamesSourceForSession(availableDatabases: Database[]): Promise<void> {
    const restored = await this.tryRestorePersistedActiveDatabase(availableDatabases);
    if (restored) {
      return;
    }

    await this.restoreImportedDraftIfAny();
  }

  private async tryRestorePersistedActiveDatabase(availableDatabases: Database[]): Promise<boolean> {
    const persisted = this.readPersistedActiveDatabase();
    if (!persisted) {
      return false;
    }

    const currentUserId = this.authState.currentUser()?.userId;
    if (!currentUserId || persisted.userId !== currentUserId) {
      return false;
    }

    const matched = availableDatabases.find(db => db.id === persisted.databaseId);
    if (!matched) {
      this.clearPersistedActiveDatabase();
      return false;
    }

    await this.openUserDatabase(matched);
    return true;
  }

  private async restoreImportedDraftIfAny(): Promise<void> {
    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.moveRows = [];
    this.currentPly = 0;
    this.currentGamesSource = 'imported';
    this.currentDatabaseName = 'Imported Draft';
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    await this.reloadGamesAndMoveTree();

    if (!this.gamesLoaded) {
      this.currentDatabaseName = 'Games';
    }
  }

  private async handleCloseCurrentDatabase(): Promise<void> {
    const clearImportedDraft = this.currentGamesSource === 'imported';
    this.resetCurrentGamesView();

    if (clearImportedDraft) {
      try {
        await firstValueFrom(this.draftImportApi.clearDraftGames());
      } catch {
        this.showImportError('Unable to clear imported draft games.');
      }
    }
  }

  private resetCurrentGamesView(): void {
    this.activeUserDatabaseId.set(null);
    this.clearPersistedActiveDatabase();
    this.currentDatabaseName = 'Games';
    this.currentGamesSource = 'imported';
    this.draftGames.set([]);
    this.draftGamesTotalCount.set(0);
    this.draftGamesPage.set(1);
    this.syncFilterStateFromListControls();
    this.gamesLoaded = false;
    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.moveRows = [];
    this.currentPly = 0;
    this.clearMoveTree();
  }

  private async loadMoveTree(): Promise<void> {
    if (!this.authState.isAuthenticated()) {
      this.clearMoveTree();
      return;
    }

    if (this.currentGamesSource === 'external') {
      this.clearMoveTree();
      return;
    }

    const boardFen = this.boardFen().trim();
    const fen = boardFen.length > 0 ? boardFen : ExplorerPageComponent.initialBoardFen;

    if (this.currentGamesSource === 'userDatabase') {
      const userDatabaseId = this.activeUserDatabaseId();
      if (!userDatabaseId) {
        this.clearMoveTree();
        return;
      }
    }

    const requestVersion = ++this.moveTreeRequestVersion;
    this.moveTreeLoading.set(true);
    this.moveTreeError.set(null);

    const filterPayload = toExplorerMoveTreeFiltersPayload(this.gamesFilters());
    const request: ExplorerMoveTreeRequest = {
      fen,
      source: this.currentGamesSource === 'userDatabase' ? 0 : 1,
      userDatabaseId: this.currentGamesSource === 'userDatabase' ? this.activeUserDatabaseId() ?? undefined : undefined,
      maxMoves: 40,
      ...filterPayload
    };

    try {
      const response = await firstValueFrom(this.explorerBoardApi.getMoveTree(request));
      if (requestVersion !== this.moveTreeRequestVersion) {
        return;
      }

      this.moveTreeData.set(response);
    } catch {
      if (requestVersion !== this.moveTreeRequestVersion) {
        return;
      }

      this.moveTreeData.set(null);
      this.moveTreeError.set('Unable to load move tree for current filtered games.');
    } finally {
      if (requestVersion === this.moveTreeRequestVersion) {
        this.moveTreeLoading.set(false);
      }
    }
  }

  private clearMoveTree(): void {
    this.moveTreeRequestVersion++;
    this.moveTreeLoading.set(false);
    this.moveTreeError.set(null);
    this.moveTreeData.set(null);
  }

  private async deleteDatabase(database: Database): Promise<void> {
    const previousPanelDatabases = this.panelDatabases();
    const previousMyDatabases = this.myDatabases();
    const deletingActive = this.activeUserDatabaseId() === database.id;

    this.panelDatabases.set(previousPanelDatabases.filter(db => db.id !== database.id));
    this.myDatabases.set(previousMyDatabases.filter(db => db.id !== database.id));

    if (deletingActive) {
      this.resetCurrentGamesView();
    }

    try {
      await firstValueFrom(this.userDatabasesApi.delete(database.id));
      await this.reloadUserDatabases();
    } catch {
      this.panelDatabases.set(previousPanelDatabases);
      this.myDatabases.set(previousMyDatabases);

      if (deletingActive) {
        void this.openUserDatabase(database);
      }

      this.showImportError('Unable to delete database. Please try again.');
    }
  }

  private openDeleteConfirmation(kind: 'draft' | 'database'): void {
    this.deleteConfirmationKind.set(kind);
    this.deleteConfirmationVisible.set(true);
  }

  private persistActiveDatabase(database: Database): void {
    const currentUserId = this.authState.currentUser()?.userId;
    if (!currentUserId) {
      return;
    }

    const payload = {
      userId: currentUserId,
      databaseId: database.id
    };

    localStorage.setItem(ExplorerPageComponent.activeDatabaseStorageKey, JSON.stringify(payload));
  }

  private readPersistedActiveDatabase(): { userId: string; databaseId: string } | null {
    const raw = localStorage.getItem(ExplorerPageComponent.activeDatabaseStorageKey);
    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as { userId?: unknown; databaseId?: unknown };
      if (typeof parsed.userId !== 'string' || typeof parsed.databaseId !== 'string') {
        return null;
      }

      return {
        userId: parsed.userId,
        databaseId: parsed.databaseId
      };
    } catch {
      return null;
    }
  }

  private clearPersistedActiveDatabase(): void {
    localStorage.removeItem(ExplorerPageComponent.activeDatabaseStorageKey);
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

  private syncFilterStateFromListControls(): void {
    this.gamesFilters.update(current => ({
      ...current,
      sortBy: this.draftGamesSortBy(),
      sortDirection: this.draftGamesSortDirection(),
      page: this.draftGamesPage(),
      pageSize: this.draftGamesPageSize()
    }));
  }

  private clearSelectedGameIfMissing(games: DraftGameListItem[]): void {
    const selectedGameId = this.selectedGameId();
    if (!selectedGameId) {
      return;
    }

    if (games.some(game => game.id === selectedGameId)) {
      return;
    }

    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.moveRows = [];
    this.currentPly = 0;
  }

}
