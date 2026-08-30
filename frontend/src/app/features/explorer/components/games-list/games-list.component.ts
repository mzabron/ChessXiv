import { DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EmptyGamesStateComponent } from '../empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../filters-panel/filters-panel.component';
import { Database, DatabasesPanelComponent } from '../databases-panel/databases-panel.component';
import { GamesTableComponent } from '../games-table/games-table.component';
import { DraftGameListItem, DraftGamesResultSortMode, DraftGamesSortBy, DraftGamesSortDirection } from '../../services/draft-import-api.service';
import { ExplorerGamesFilterState, createDefaultExplorerGamesFilterState } from '../../services/games-filters.models';
import { ReorderableTabsDirective } from '../../../../shared/directives/reorderable-tabs.directive';

interface UserDatabaseOption {
  id: string;
  name: string;
}

export type GamesPanelTab = 'games' | 'filters' | 'databases';

/** Whether the modal is promoting the whole draft or copying a filtered selection. */
type SaveModalMode = 'saveDraft' | 'addSelection';

interface SaveDatabaseRequestPayload {
  intent: SaveModalMode;
  mode: 'merge' | 'create';
  targetDatabaseId?: string;
  newDatabaseName?: string;
  visibility: 'private' | 'public';
}

@Component({
  selector: 'app-games-list',
  standalone: true,
  imports: [DecimalPipe, FormsModule, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent, ReorderableTabsDirective],
  templateUrl: './games-list.component.html',
  styleUrl: './games-list.component.scss'
})
export class GamesListComponent {
  @Input() gamesLoaded = false;
  @Input() games: DraftGameListItem[] = [];
  @Input() totalCount = 0;
  @Input() page = 1;
  @Input() pageSize = 18;
  @Input() resultSortMode: DraftGamesResultSortMode = 'default';
  @Input() sortBy: DraftGamesSortBy = 'createdAt';
  @Input() sortDirection: DraftGamesSortDirection = 'desc';
  @Input() selectedGameId: string | null = null;
  @Input() databaseName = 'Games';
  @Input() sourceType: 'imported' | 'userDatabase' = 'imported';
  @Input() activeDatabaseId: string | null = null;
  @Input() myDatabases: UserDatabaseOption[] = [];
  @Input() panelDatabases: Database[] = [];
  @Input() currentUserName = '';
  @Input() boardFen = '';
  /** Live form state, two-way bound to the filters panel. */
  @Input() filters: ExplorerGamesFilterState = createDefaultExplorerGamesFilterState();
  /** The filters the visible list was actually loaded with. */
  @Input() appliedFilters: ExplorerGamesFilterState = createDefaultExplorerGamesFilterState();
  /** Guests may explore and import, but only a registered account can save. */
  @Input() isRegisteredUser = false;
  @Input() savedGamesUsed = 0;
  @Input() savedGamesLimit = 0;
  @Input() selectedGameIds: string[] = [];
  @Input() isLoadingGames = false;
  protected activeTab: GamesPanelTab = 'games';

  /** Order is owned by ReorderableTabsDirective, which also persists it. */
  protected readonly tabOrderStorageKey = 'chessxiv.explorer.games-tab-order';
  protected readonly defaultTabOrder: GamesPanelTab[] = ['games', 'filters', 'databases'];

  protected readonly tabLabels: Record<GamesPanelTab, string> = {
    games: 'Games',
    filters: 'Filters',
    databases: 'Databases'
  };
  protected isSaveModalOpen = false;
  protected modalMode: SaveModalMode = 'saveDraft';
  protected saveMode: 'merge' | 'create' = 'merge';
  protected selectedTargetDatabaseId = '';
  protected newDatabaseName = '';
  protected newDatabaseVisibility: 'private' | 'public' = 'private';
  protected saveModalError = '';

  @Output() importDatabase = new EventEmitter<void>();
  @Output() addBookmark = new EventEmitter<void>();
  @Output() openDatabase = new EventEmitter<Database>();
  @Output() deleteDatabase = new EventEmitter<Database>();
  /**
   * Also emitted right when the save/add modal opens: a database created moments earlier in
   * the Databases tab (or from another tab/session) might not have propagated into this
   * component's `myDatabases` input yet, and the modal is where staleness is costliest - it
   * would silently offer to save into a database that quietly isn't in the list.
   */
  @Output() refreshDatabases = new EventEmitter<void>();
  @Output() updateDatabase = new EventEmitter<{ database: Database; name: string; isPublic: boolean }>();
  @Output() createDatabase = new EventEmitter<{ name: string; isPublic: boolean }>();
  @Output() toggleBookmark = new EventEmitter<Database>();
  @Output() removeSelectedGames = new EventEmitter<void>();
  @Output() closeDatabase = new EventEmitter<void>();
  @Output() saveDatabaseRequest = new EventEmitter<SaveDatabaseRequestPayload>();
  @Output() signInRequested = new EventEmitter<void>();
  @Output() selectedGameIdsChange = new EventEmitter<string[]>();
  @Output() gamesResultSortModeChange = new EventEmitter<DraftGamesResultSortMode>();
  @Output() gamesSortChange = new EventEmitter<{ sortBy: DraftGamesSortBy; sortDirection: DraftGamesSortDirection }>();
  @Output() gamesPageSizeChange = new EventEmitter<number>();
  @Output() gamesPageChange = new EventEmitter<number>();
  @Output() gameSelected = new EventEmitter<DraftGameListItem>();
  @Output() filtersChange = new EventEmitter<ExplorerGamesFilterState>();
  @Output() filtersApply = new EventEmitter<ExplorerGamesFilterState>();
  @Output() filtersReset = new EventEmitter<void>();

  protected selectTab(tab: GamesPanelTab): void {
    this.activeTab = tab;
  }







  /**
   * Whether filters are actually narrowing the games currently shown.
   *
   * Reads `appliedFilters`, not the live form: typing into a field or ticking a box changes
   * the draft state, but the list is unaffected until Apply. Marking the tab on the draft
   * claimed the list was filtered when it was not. Sort order and paging are also excluded
   * - they change presentation, not which games are included.
   */
  protected get hasActiveFilters(): boolean {
    const f = this.appliedFilters;

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
  }

  /** Only the owner may remove games; a public database you merely read is off limits. */
  protected get isActiveDatabaseOwned(): boolean {
    return this.panelDatabases.some(db => db.id === this.activeDatabaseId && db.isOwner);
  }

  /** Removing games is destructive and owner-only, so it needs an explicit selection. */
  protected get canRemoveSelectedGames(): boolean {
    return this.isRegisteredUser
      && this.sourceType === 'userDatabase'
      && this.isActiveDatabaseOwned
      && this.selectedGameIds.length > 0;
  }

  /**
   * Databases the user can actually save into. Excludes the database currently being
   * browsed when adding a selection to it - copying a database's own games back into
   * itself is a no-op at best (they are already there) and confusing at worst.
   */
  protected get availableTargetDatabases(): UserDatabaseOption[] {
    if (this.modalMode !== 'addSelection' || this.sourceType !== 'userDatabase') {
      return this.myDatabases;
    }

    return this.myDatabases.filter(db => db.id !== this.activeDatabaseId);
  }

  /** Describes what "Add" will copy, so the user is not guessing before confirming. */
  protected get selectionSummary(): string {
    const explicitCount = this.selectedGameIds.length;

    if (explicitCount > 0) {
      return `${explicitCount} selected ${explicitCount === 1 ? 'game' : 'games'}`;
    }

    return `All ${this.totalCount} games matching the current filters`;
  }

  protected openSaveDatabaseModal(): void {
    this.openModal('saveDraft');
  }

  protected openAddToDatabaseModal(): void {
    this.openModal('addSelection');
  }

  private openModal(mode: SaveModalMode): void {
    this.isSaveModalOpen = true;
    this.modalMode = mode;
    this.refreshDatabases.emit();

    const targets = mode === 'addSelection' && this.sourceType === 'userDatabase'
      ? this.myDatabases.filter(db => db.id !== this.activeDatabaseId)
      : this.myDatabases;

    this.saveMode = targets.length > 0 ? 'merge' : 'create';
    this.selectedTargetDatabaseId = targets[0]?.id ?? '';
    this.newDatabaseName = '';
    this.newDatabaseVisibility = 'private';
    this.saveModalError = '';
  }

  protected closeSaveDatabaseModal(): void {
    this.isSaveModalOpen = false;
    this.saveModalError = '';
  }

  protected confirmSaveDatabase(): void {
    this.saveModalError = '';

    if (this.saveMode === 'merge') {
      if (!this.selectedTargetDatabaseId) {
        this.saveModalError = 'Select a target database.';
        return;
      }

      this.saveDatabaseRequest.emit({
        intent: this.modalMode,
        mode: 'merge',
        targetDatabaseId: this.selectedTargetDatabaseId,
        visibility: 'private'
      });
      this.closeSaveDatabaseModal();
      return;
    }

    const trimmedName = this.newDatabaseName.trim();
    if (!trimmedName) {
      this.saveModalError = 'Enter a new database name.';
      return;
    }

    this.saveDatabaseRequest.emit({
      intent: this.modalMode,
      mode: 'create',
      newDatabaseName: trimmedName,
      visibility: this.newDatabaseVisibility
    });
    this.closeSaveDatabaseModal();
  }
}
