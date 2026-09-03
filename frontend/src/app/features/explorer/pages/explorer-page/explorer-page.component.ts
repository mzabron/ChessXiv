import { Component, ElementRef, HostListener, ViewChild, Input, Output, EventEmitter, computed, effect, inject, signal, untracked, OnDestroy, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom, Subject, Subscription, takeUntil } from 'rxjs';
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
import { AccountApiService } from '../../../../core/auth/account-api.service';
import { UserDatabaseListItemDto, UserDatabasesApiService } from '../../services/user-databases-api.service';
import { Database } from '../../components/databases-panel/databases-panel.component';

/** Tabs in the focus-mode side panel. */
type FocusPanelTab = 'tree' | 'moves' | 'games' | 'filters' | 'databases';
import {
  DraftGameListItem,
  DraftGamesResultSortMode,
  DraftGamesSortBy,
  DraftGamesSortDirection,
  DraftImportApiService
} from '../../services/draft-import-api.service';
import { DraftImportProgressService, DraftImportProgressUpdate } from '../../services/draft-import-progress.service';
import { GameReplayResponse } from '../../services/game-replay.models';
import {
  ExplorerGamesFilterState,
  createDefaultExplorerGamesFilterState,
  toExplorerGamesFiltersQuery,
  toExplorerMoveTreeFiltersPayload
} from '../../services/games-filters.models';
import { ReorderableTabsDirective } from '../../../../shared/directives/reorderable-tabs.directive';
import {
  ExplorerBoardApiService,
  ExplorerMoveTreeRequest,
  ExplorerMoveTreeResponse
} from '../../services/explorer-board-api.service';

@Component({
  selector: 'app-explorer-page',
  standalone: true,
  imports: [CommonModule, ChessboardComponent, GamesTreeComponent, GamesListComponent, MoveListComponent, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent, ReorderableTabsDirective],
  templateUrl: './explorer-page.component.html',
  styleUrl: './explorer-page.component.scss'
})
export class ExplorerPageComponent implements OnDestroy, AfterViewInit {
  private static readonly initialBoardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  /**
   * Matches the server limit. Checked client-side too, because Cloudflare rejects a larger
   * body before it reaches the origin, which would surface as an opaque network failure.
   */
  private static readonly maxUploadBytes = 100 * 1024 * 1024;
  private readonly authState = inject(AuthStateService);
  private readonly userDatabasesApi = inject(UserDatabasesApiService);
  private readonly draftImportApi = inject(DraftImportApiService);
  private readonly draftImportProgress = inject(DraftImportProgressService);
  private readonly explorerBoardApi = inject(ExplorerBoardApiService);
  private readonly accountApi = inject(AccountApiService);

  private readonly loadedForCurrentSession = signal(false);
  protected readonly activeUserDatabaseId = signal<string | null>(null);
  private static readonly activeDatabaseStorageKey = 'chessxiv.explorer.active-user-database';
  private progressSubscription: Subscription | null = null;

  @Input() isFocusMode = false;

  /** Raised when a guest tries to do something that needs an account. */
  @Output() readonly signInRequested = new EventEmitter<void>();

  protected readonly isRegisteredUser = computed(() => this.authState.isAuthenticated());

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
  protected readonly deleteConfirmationKind = signal<'draft' | 'database' | 'games' | null>(null);
  protected readonly deleteConfirmationDatabaseName = signal('');
  protected readonly draftGames = signal<DraftGameListItem[]>([]);
  protected readonly draftGamesTotalCount = signal(0);
  protected readonly draftGamesPage = signal(1);
  protected readonly draftGamesPageSize = signal(18);
  protected readonly draftGamesResultSortMode = signal<DraftGamesResultSortMode>('default');
  protected readonly draftGamesSortBy = signal<DraftGamesSortBy>('createdAt');
  protected readonly draftGamesSortDirection = signal<DraftGamesSortDirection>('desc');
  protected readonly gamesFilters = signal<ExplorerGamesFilterState>(createDefaultExplorerGamesFilterState());
  /**
   * The filters the currently displayed list was loaded with. Kept apart from the live form
   * state so the "filters active" marker reflects the list, not what is being typed.
   */
  protected readonly appliedGamesFilters = signal<ExplorerGamesFilterState>(createDefaultExplorerGamesFilterState());
  protected readonly boardFen = signal('');
  protected readonly moveTreeData = signal<ExplorerMoveTreeResponse | null>(null);
  protected readonly moveTreeLoading = signal(false);
  protected readonly moveTreeError = signal<string | null>(null);
  protected currentDatabaseName = 'Games';
  protected currentGamesSource: 'imported' | 'userDatabase' = 'imported';
  protected readonly selectedGameId = signal<string | null>(null);
  protected readonly selectedGameReplay = signal<GameReplayResponse | null>(null);
  /**
   * Cleared once the user plays a move of their own: the loaded game's result no longer
   * describes the position on the board, so continuing to show it would be a lie.
   */
  protected readonly hasAbandonedRecordedGame = signal(false);
  protected readonly activeGameResult = computed(() =>
    this.hasAbandonedRecordedGame() ? null : this.selectedGameReplay()?.result ?? null
  );
  protected readonly boardSanMoveRequest = signal<{ san: string; version: number } | null>(null);
  protected readonly myDatabases = signal<Array<{ id: string; name: string }>>([]);
  protected readonly panelDatabases = signal<Database[]>([]);
  protected readonly savedGamesUsed = signal(0);
  protected readonly savedGamesLimit = signal(0);
  protected readonly selectedGameIds = signal<string[]>([]);
  protected readonly isLoadingGames = signal(false);

  /**
   * Cancels whatever game-list request is in flight. Clicking through sort columns fires a
   * request per click, and on a large database those take seconds and finish out of order -
   * so the list flickered through stale results and settled on whichever response happened
   * to arrive last rather than the sort actually asked for. Emitting here unsubscribes the
   * outstanding HttpClient call, which aborts the request instead of just ignoring it.
   */
  private readonly cancelGamesRequests = new Subject<void>();

  /** Guards the loading flag against being cleared by a request that has been superseded. */
  private gamesRequestVersion = 0;
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
  protected isResizing = false;
  protected leftPaneWidth = 620;

  protected focusRightTab: FocusPanelTab = 'tree';

  /** Order is owned by ReorderableTabsDirective, which also persists it. */
  protected readonly focusTabOrderStorageKey = 'chessxiv.explorer.focus-tab-order';
  protected readonly defaultFocusTabOrder: FocusPanelTab[] = [
    'tree', 'moves', 'games', 'filters', 'databases'
  ];

  protected readonly focusTabLabels: Record<FocusPanelTab, string> = {
    tree: 'Tree',
    moves: 'Moves',
    games: 'Games',
    filters: 'Filters',
    databases: 'Databases'
  };

  /**
   * Mirrors the games panel's marker so the focus-mode strip flags applied filters too.
   * Reads the applied state, not the live form - typing is not yet filtering.
   */
  protected readonly hasActiveAppliedFilters = computed(() => {
    const f = this.appliedGamesFilters();

    return Boolean(
      f.whiteFirstName.trim() ||
      f.whiteLastName.trim() ||
      f.blackFirstName.trim() ||
      f.blackLastName.trim() ||
      f.ecoCode.trim() ||
      f.result.trim() ||
      f.eloEnabled ||
      f.yearEnabled ||
      f.moveEnabled ||
      f.searchByPosition
    );
  });

  private static readonly minBoardWidth = 320;
  private static readonly minMoveListWidth = 145;
  private static readonly boardMoveGap = 12;
  private static readonly rightColumnMinWidth = 390;
  private static readonly handleWidth = 8;
  constructor() {
    effect(() => {
      const isAuthenticated = this.authState.isAuthenticated();

      // Signing in or out changes which games the caller may see, so every piece of
      // loaded state has to go - otherwise the previous session's database header, game
      // list and move tree stay on screen while the data behind them is no longer there.
      //
      // untracked, and not optional: onSessionChanged runs synchronously up to its first
      // await, and on that path syncFilterStateFromListControls reads the sort, page and
      // page-size signals. Without this they become dependencies of the effect, so merely
      // sorting a column re-ran the whole sign-in/sign-out teardown - which clears the
      // persisted active database and drops the view back to an empty "Imported Draft".
      // Only a change of session should get in here.
      untracked(() => void this.onSessionChanged(isAuthenticated));
    });
  }

  private async onSessionChanged(isAuthenticated: boolean): Promise<void> {
    this.explorerBoardApi.invalidateMoveTreeCache();
    this.detachProgressSubscription();
    this.resetCurrentGamesView();
    this.myDatabases.set([]);
    this.panelDatabases.set([]);
    this.loadedForCurrentSession.set(false);

    if (!isAuthenticated) {
      await this.draftImportProgress.disconnect();
      // Guests get an anonymous token so they can upload and explore without an account.
      await this.authState.ensureGuestSession();
    }

    await this.draftImportProgress.connect();
    this.attachGlobalProgressSubscription();
    this.checkAndHydrateGhostImport();

    await this.loadDatabases();
    await this.refreshAccountUsage();
  }

  private attachGlobalProgressSubscription(): void {
    if (this.progressSubscription) {
      return;
    }

    this.progressSubscription = this.draftImportProgress.updates$.subscribe(update => {
      if (!update) return;

      this.importProgress.set(update);

      if (!update.isCompleted && !update.isFailed) {
        this.isImporting.set(true);
      } else if (this.isImporting()) {
        if (update.isCompleted) {
          this.applyImportedDraftState(update);
        } else if (update.isFailed) {
          this.showImportError(update.message || 'Import failed.');
        }

        this.isImporting.set(false);
      }
    });
  }

  private checkAndHydrateGhostImport(): void {
    this.draftImportApi.getDraftImportProgress().subscribe({
      next: (progress) => {
        if (progress && !progress.isCompleted && !progress.isFailed) {
          this.isImporting.set(true);
          this.importProgress.set(progress);
        }
      },
      error: () => { } // Ghost import fetch failed or no content, ignore
    });
  }

  ngOnDestroy(): void {
    this.clearImportErrorTimers();
    this.detachProgressSubscription();
    this.boardMovesResizeObserver?.disconnect();
    // Aborts any game-list request still running, then releases the subject itself.
    this.cancelGamesRequests.next();
    this.cancelGamesRequests.complete();
  }

  ngAfterViewInit(): void {
    this.initBoardMovesHeightSync();
  }

  private initBoardMovesHeightSync(): void {
    const boardElement = this.mainChessboardRef?.nativeElement;
    const rowElement = this.boardMovesRowRef?.nativeElement;
    const observedElement = this.getBoardHeightMeasureElement();

    if (!boardElement || !rowElement || !observedElement) {
      return;
    }

    this.syncBoardMovesHeight();
    this.boardMovesResizeObserver?.disconnect();
    this.boardMovesResizeObserver = new ResizeObserver(() => this.syncBoardMovesHeight());
    this.boardMovesResizeObserver.observe(observedElement);
    this.boardMovesResizeObserver.observe(rowElement);
  }

  private getBoardHeightMeasureElement(): HTMLElement | null {
    const boardHost = this.mainChessboardRef?.nativeElement;
    if (!boardHost) {
      return null;
    }

    return boardHost.querySelector('.board-panel') ?? boardHost;
  }

  private syncBoardMovesHeight(): void {
    const rowElement = this.boardMovesRowRef?.nativeElement;
    const moveListElement = this.mainMoveListRef?.nativeElement;
    const boardMeasureElement = this.getBoardHeightMeasureElement();

    if (!rowElement || !moveListElement || !boardMeasureElement) {
      return;
    }

    const boardHeight = Math.round(boardMeasureElement.getBoundingClientRect().height);
    if (boardHeight > 0) {
      rowElement.style.setProperty('--board-moves-height', `${boardHeight}px`);
      moveListElement.style.setProperty('height', `${boardHeight}px`);
      moveListElement.style.setProperty('max-height', `${boardHeight}px`);
    }
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
    requestAnimationFrame(() => this.syncBoardMovesHeight());
  }

  @HostListener('window:mouseup')
  protected onWindowMouseUp(): void {
    this.isResizing = false;
    this.syncBoardMovesHeight();
  }

  @HostListener('window:resize')
  protected onWindowResize(): void {
    this.syncBoardMovesHeight();
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
    if (!database.isOwner) {
      return;
    }

    this.pendingDeleteDatabase = database;
    this.deleteConfirmationDatabaseName.set(database.name);
    this.openDeleteConfirmation('database');
  }

  protected onDatabasesRefreshRequested(): void {
    void this.reloadDatabases();
  }

  protected async onCreateDatabaseRequested(payload: { name: string; isPublic: boolean }): Promise<void> {
    try {
      await firstValueFrom(this.userDatabasesApi.create(payload));
      await this.reloadDatabases();
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 409) {
          this.showImportError('You already have a database with this name.');
          return;
        }

        if (error.status === 401 || error.status === 403) {
          this.showImportError('Sign in to create a database.');
          return;
        }
      }

      this.showImportError('Unable to create database. Please try again.');
    }
  }

  protected async onDatabaseUpdateRequested(payload: { database: Database; name: string; isPublic: boolean }): Promise<void> {
    if (!payload.database.isOwner) {
      return;
    }

    const updated = {
      ...payload.database,
      name: payload.name.trim() || payload.database.name,
      isPublic: payload.isPublic
    };
    const previous = this.panelDatabases();
    const next = previous.map(db => (db.id === payload.database.id ? updated : db));
    this.panelDatabases.set(next);

    if (this.activeUserDatabaseId() === updated.id) {
      this.currentDatabaseName = updated.name;
    }

    try {
      await firstValueFrom(this.userDatabasesApi.update(updated.id, {
        name: updated.name,
        isPublic: updated.isPublic
      }));
    } catch {
      this.panelDatabases.set(previous);
      this.showImportError('Unable to update database settings. Please try again.');
    }
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
      return;
    }

    if (kind === 'games') {
      void this.removeSelectedGames();
    }
  }


  protected async onSaveDatabaseRequest(payload: {
    intent: 'saveDraft' | 'addSelection';
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

    let createdDatabaseId: string | null = null;

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
        createdDatabaseId = created.id;
      }

      if (!userDatabaseId) {
        this.showImportError('Choose a target database before saving.');
        return;
      }

      try {
        // Both intents go through the same selection-aware save: "Save to database" for the
        // imported draft used to always promote the ENTIRE draft via promoteDraft(), ignoring
        // both the checkboxes and the active filters. That meant the modal's own summary text
        // ("N games will be saved") was routinely a lie, and a large, unfiltered draft could
        // trip the saved-games limit even when the visible/filtered/selected count was small.
        // addGamesFromSelection honors filters and an explicit selection for both the draft
        // and a user database, so "what you see is what gets saved" now actually holds.
        await this.addCurrentSelectionToDatabase(userDatabaseId);
      } catch (addError) {
        if (createdDatabaseId) {
          // A "New database" save that fails to add anything must not leave an empty,
          // pointless database behind - the user asked to save games, not to create an
          // empty container. Roll back what create() just did.
          await this.rollBackCreatedDatabase(createdDatabaseId);
        }

        throw addError;
      }

      this.explorerBoardApi.invalidateMoveTreeCache();
      await this.reloadDatabases();
      await this.refreshAccountUsage();

      const selectedDatabase: Database = this.panelDatabases().find(db => db.id === userDatabaseId)
        ?? {
        id: userDatabaseId,
        name: payload.mode === 'create'
          ? (payload.newDatabaseName ?? 'Imported Games')
          : (this.myDatabases().find(db => db.id === userDatabaseId)?.name ?? 'Saved Database'),
        isPublic: payload.visibility === 'public',
        owner: this.currentUserName() ?? '',
        creationDate: new Date(),
        contentUpdatedDate: new Date(),
        gamesCount: 0,
        isOwner: true,
        isBookmarked: false
      };

      await this.openUserDatabase(selectedDatabase);
      this.saveDraftCompleted.set(true);
    } catch (error) {
      this.showImportError(this.resolveSaveErrorMessage(error, payload.mode));
    } finally {
      this.isSavingDraft.set(false);
    }
  }

  /**
   * Adds what the user is currently looking at: their explicit tick-box selection when
   * there is one, otherwise every game matching the active filters - which is usually far
   * more than the page on screen, so the server resolves the set rather than the client.
   */
  /**
   * Deletes a database this same request just created, when adding games to it failed. Best
   * effort: if the delete itself fails there is nothing more useful to do than leave the
   * empty database for the user to remove manually, which is the pre-fix behaviour, not a
   * regression - so failures here are swallowed rather than compounding the error shown for
   * the original, more important failure.
   */
  private async rollBackCreatedDatabase(databaseId: string): Promise<void> {
    try {
      await firstValueFrom(this.userDatabasesApi.delete(databaseId));
      await this.reloadDatabases();
    } catch {
      // Swallowed - see method doc.
    }
  }

  private async addCurrentSelectionToDatabase(targetDatabaseId: string): Promise<void> {
    const explicitIds = this.selectedGameIds();

    const result = await firstValueFrom(
      this.userDatabasesApi.addGamesFromSelection(targetDatabaseId, {
        sourceUserDatabaseId:
          this.currentGamesSource === 'userDatabase'
            ? this.activeUserDatabaseId() ?? undefined
            : undefined,
        gameIds: explicitIds.length > 0 ? explicitIds : undefined,
        filters: toExplorerGamesFiltersQuery(this.gamesFilters())
      })
    );

    this.savedGamesUsed.set(result.savedGamesUsed);
    this.savedGamesLimit.set(result.savedGamesLimit);
    this.selectedGameIds.set([]);
  }

  private resolveSaveErrorMessage(error: unknown, mode: 'merge' | 'create'): string {
    if (error instanceof HttpErrorResponse) {
      const payload = error.error as { code?: string; message?: string } | string | null;

      if (payload && typeof payload === 'object' && payload.code === 'SAVED_GAMES_LIMIT' && payload.message) {
        return payload.message;
      }

      if (mode === 'create') {
        const rawMessage = this.extractErrorMessage(error);
        if (error.status === 409 || rawMessage.toLowerCase().includes('already exists')) {
          return 'You already have a database with this name.';
        }
      }
    }

    return 'Saving games failed. Please try again.';
  }

  protected onSelectedGameIdsChanged(gameIds: string[]): void {
    this.selectedGameIds.set(gameIds);
  }

  /** Keeps the "saved games" figure in the save dialog honest without polling. */
  private async refreshAccountUsage(): Promise<void> {
    if (!this.authState.isAuthenticated()) {
      this.savedGamesUsed.set(0);
      this.savedGamesLimit.set(0);
      return;
    }

    try {
      const summary = await firstValueFrom(this.accountApi.getSummary());
      this.savedGamesUsed.set(summary.savedGamesUsed);
      this.savedGamesLimit.set(summary.savedGamesLimit);
    } catch {
      // Usage display is advisory; the server is the authority when saving.
    }
  }

  protected closeSaveDraftModal(): void {
    this.saveDraftCompleted.set(false);
  }

  protected async toggleCurrentDatabaseBookmark(): Promise<void> {
    const databaseId = this.activeUserDatabaseId();
    if (!databaseId) {
      return;
    }

    const database = this.panelDatabases().find(db => db.id === databaseId);
    if (!database) {
      return;
    }

    await this.toggleDatabaseBookmark(database);
  }

  protected onRemoveSelectedGamesRequested(): void {
    if (this.selectedGameIds().length === 0) {
      return;
    }

    this.openDeleteConfirmation('games');
  }

  private async removeSelectedGames(): Promise<void> {
    const databaseId = this.activeUserDatabaseId();
    const gameIds = this.selectedGameIds();

    if (!databaseId || gameIds.length === 0) {
      return;
    }

    try {
      await firstValueFrom(this.userDatabasesApi.removeGames(databaseId, gameIds));

      this.selectedGameIds.set([]);
      this.explorerBoardApi.invalidateMoveTreeCache();
      await this.reloadGamesAndMoveTree();
      await this.reloadDatabases();
      await this.refreshAccountUsage();
    } catch {
      this.showImportError('Unable to remove the selected games. Please try again.');
    }
  }

  protected async toggleDatabaseBookmark(database: Database): Promise<void> {
    if (!this.authState.isAuthenticated()) {
      this.signInRequested.emit();
      return;
    }

    // Optimistic: the bookmark icon is a direct, reversible toggle, so waiting on a round
    // trip before it responds would feel broken. Reverted below if the server disagrees.
    const previous = this.panelDatabases();
    this.panelDatabases.set(
      previous.map(db => (db.id === database.id ? { ...db, isBookmarked: !db.isBookmarked } : db))
    );

    try {
      if (database.isBookmarked) {
        await firstValueFrom(this.userDatabasesApi.removeBookmark(database.id));
      } else {
        await firstValueFrom(this.userDatabasesApi.addBookmark(database.id));
      }

      await this.reloadDatabases();
    } catch {
      this.panelDatabases.set(previous);
      this.showImportError('Unable to update bookmark. Please try again.');
    }
  }

  protected onRecordedGameAbandoned(): void {
    this.hasAbandonedRecordedGame.set(true);
  }

  protected onMoveRowsChanged(moveRows: MoveRow[]): void {
    this.moveRows = moveRows;
  }

  protected onCurrentPlyChanged(ply: number): void {
    this.currentPly = ply;
  }

  protected onBoardFenChanged(fen: string): void {
    const nextFen = (fen ?? '').trim();
    const currentFen = this.boardFen().trim();
    if (nextFen === currentFen) {
      return;
    }

    this.boardFen.set(nextFen);
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

    if (file.size > ExplorerPageComponent.maxUploadBytes) {
      const sizeMb = Math.ceil(file.size / (1024 * 1024));
      this.showImportError(
        `That PGN is ${sizeMb} MB. The upload limit is 100 MB - please split it into smaller files.`
      );
      return;
    }

    await this.runDraftImport(file);
  }

  private async runDraftImport(file: File): Promise<void> {
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
      } catch {
        // Import should still work even if live progress transport is unavailable.
        progressConnected = false;
      }

      // Send the file to the backend, it will process asynchronously. 
      await firstValueFrom(this.draftImportApi.importDraft(file));

    } catch (error) {
      this.showImportError(this.resolveImportErrorMessage(error, progressConnected));
      this.isImporting.set(false);
    }
    // Finally block omitted because isImporting needs to stay true 
    // while the background process completes via SignalR!
  }

  private applyImportedDraftState(result: DraftImportProgressUpdate): void {
    this.explorerBoardApi.invalidateMoveTreeCache();
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
        ).pipe(takeUntil(this.cancelGamesRequests)),
        // A cancelled request completes without emitting; null says so rather than throwing.
        { defaultValue: null }
      );

      if (response === null) {
        return;
      }

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
        ).pipe(takeUntil(this.cancelGamesRequests)),
        // A cancelled request completes without emitting; null says so rather than throwing.
        { defaultValue: null }
      );

      if (response === null) {
        return;
      }

      this.draftGames.set(response.items);
      this.draftGamesTotalCount.set(response.totalCount);
      this.gamesLoaded = true;
      this.clearSelectedGameIfMissing(response.items);
    } catch {
      this.showImportError('Unable to load games from selected database.');
    }
  }

  private async loadCurrentGamesPage(): Promise<void> {
    // Whatever the list ends up showing was loaded with these filters, so this is the one
    // place that needs to record them - it covers apply, reset, sorting, paging and
    // switching databases alike.
    this.appliedGamesFilters.set({ ...this.gamesFilters() });

    // Abort whatever is still in flight before starting the replacement, so a slow earlier
    // sort can neither overwrite this one's results nor clear its loading state.
    this.cancelGamesRequests.next();
    const version = ++this.gamesRequestVersion;

    if (this.currentGamesSource === 'userDatabase') {
      const userDatabaseId = this.activeUserDatabaseId();
      if (!userDatabaseId) {
        this.gamesLoaded = false;
        this.draftGames.set([]);
        this.draftGamesTotalCount.set(0);
        return;
      }

      this.isLoadingGames.set(true);
      try {
        await this.loadUserDatabaseGamesPage(userDatabaseId);
      } finally {
        this.clearLoadingIfCurrent(version);
      }

      return;
    }

    if (this.currentGamesSource === 'imported') {
      this.isLoadingGames.set(true);
      try {
        await this.loadDraftGamesPage();
      } finally {
        this.clearLoadingIfCurrent(version);
      }
    }
  }

  /**
   * Only the newest request may turn the spinner off. A superseded one reaching its finally
   * block would otherwise clear the loading state while its replacement is still running,
   * flashing the list back to a resolved-looking state mid-flight.
   */
  private clearLoadingIfCurrent(version: number): void {
    if (version === this.gamesRequestVersion) {
      this.isLoadingGames.set(false);
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
      this.hasAbandonedRecordedGame.set(false);
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

      if (error.status === 413) {
        return 'Import failed: This PGN is too large for web upload.';
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

  /**
   * A single list for guests and signed-in users alike: every public database, plus the
   * caller's own private ones. Signing in previously swapped this for "mine + bookmarks",
   * which silently dropped other people's public databases from the panel.
   */
  private async loadDatabases(): Promise<void> {
    try {
      const databases = await firstValueFrom(this.userDatabasesApi.getVisible());
      const mapped = databases.map(db => this.toPanelDatabase(db));

      this.panelDatabases.set(mapped);
      this.myDatabases.set(
        mapped.filter(db => db.isOwner).map(db => ({ id: db.id, name: db.name }))
      );
      this.loadedForCurrentSession.set(true);

      await this.initializeGamesSourceForSession(mapped);
    } catch {
      this.panelDatabases.set([]);
      this.myDatabases.set([]);
      this.loadedForCurrentSession.set(false);
    }
  }

  private async reloadDatabases(): Promise<void> {
    try {
      const databases = await firstValueFrom(this.userDatabasesApi.getVisible());
      const mapped = databases.map(db => this.toPanelDatabase(db));

      this.panelDatabases.set(mapped);
      this.myDatabases.set(
        mapped.filter(db => db.isOwner).map(db => ({ id: db.id, name: db.name }))
      );
    } catch {
      this.showImportError('Unable to refresh the database list.');
    }
  }

  private toPanelDatabase(db: UserDatabaseListItemDto): Database {
    return {
      id: db.id,
      name: db.name,
      isPublic: db.isPublic,
      owner: db.ownerUserName || db.ownerUserId,
      creationDate: new Date(db.createdAtUtc),
      contentUpdatedDate: new Date(db.contentUpdatedAtUtc),
      gamesCount: db.gameCount,
      isOwner: db.isOwner,
      isBookmarked: db.isBookmarked
    };
  }

  private async openUserDatabase(database: Database): Promise<void> {
    this.clearImportError();
    this.selectedGameId.set(null);
    this.selectedGameReplay.set(null);
    this.hasAbandonedRecordedGame.set(false);
    this.moveRows = [];
    this.currentPly = 0;
    this.selectedGameIds.set([]);
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
    this.hasAbandonedRecordedGame.set(false);
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
        this.explorerBoardApi.invalidateMoveTreeCache();
      } catch {
        this.showImportError('Unable to clear imported draft games.');
      }
    }
  }

  private resetCurrentGamesView(): void {
    this.activeUserDatabaseId.set(null);
    this.selectedGameIds.set([]);
    this.appliedGamesFilters.set(createDefaultExplorerGamesFilterState());
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
    this.hasAbandonedRecordedGame.set(false);
    this.moveRows = [];
    this.currentPly = 0;
    this.clearMoveTree();
  }

  private async loadMoveTree(): Promise<void> {
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
      this.explorerBoardApi.invalidateMoveTreeCache();
      await this.reloadDatabases();
    } catch {
      this.panelDatabases.set(previousPanelDatabases);
      this.myDatabases.set(previousMyDatabases);

      if (deletingActive) {
        void this.openUserDatabase(database);
      }

      this.showImportError('Unable to delete database. Please try again.');
    }
  }







  private openDeleteConfirmation(kind: 'draft' | 'database' | 'games'): void {
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
    this.hasAbandonedRecordedGame.set(false);
    this.moveRows = [];
    this.currentPly = 0;
  }

}
