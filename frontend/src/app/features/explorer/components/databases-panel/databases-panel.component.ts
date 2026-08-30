import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface Database {
  id: string;
  name: string;
  owner: string;
  isPublic: boolean;
  creationDate: Date;
  /** When games were last added or removed; independent of renames. */
  contentUpdatedDate: Date;
  gamesCount: number;
  /** Set from the server rather than by comparing display names, which are not unique. */
  isOwner: boolean;
  isBookmarked: boolean;
}

/** Which slice of the visible databases the panel is showing. */
export type DatabaseScope = 'all' | 'mine' | 'bookmarks';

@Component({
  selector: 'app-databases-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './databases-panel.component.html',
  styleUrl: './databases-panel.component.scss'
})
export class DatabasesPanelComponent {
  @Input() currentUser = '';
  @Input() activeDatabaseId: string | null = null;
  /** Guests may browse databases but cannot create one. */
  @Input() isRegisteredUser = false;
  @Input() set databases(value: Database[]) {
    this.databasesSignal.set(value ?? []);
  }
  @Output() openDatabase = new EventEmitter<Database>();
  @Output() deleteDatabase = new EventEmitter<Database>();
  @Output() refreshDatabases = new EventEmitter<void>();
  @Output() updateDatabase = new EventEmitter<{ database: Database; name: string; isPublic: boolean }>();
  @Output() createDatabase = new EventEmitter<{ name: string; isPublic: boolean }>();
  @Output() signInRequested = new EventEmitter<void>();
  @Output() toggleBookmark = new EventEmitter<Database>();

  /** "All" is the default: discovery matters more on arrival than your own list. */
  scope = signal<DatabaseScope>('all');
  searchQuery = signal('');
  sortOption = signal<'createdDesc' | 'createdAsc' | 'nameAsc' | 'nameDesc' | 'gamesDesc' | 'gamesAsc'>('createdDesc');
  isSortMenuOpen = signal(false);
  isRefreshing = signal(false);
  isSettingsOpen = signal(false);
  settingsName = signal('');
  settingsVisibility = signal<'private' | 'public'>('private');
  selectedDatabase = signal<Database | null>(null);

  isCreateOpen = signal(false);
  createName = signal('');
  createVisibility = signal<'private' | 'public'>('private');
  createError = signal('');

  private readonly databasesSignal = signal<Database[]>([]);
  private refreshTimerId: number | null = null;

  /**
   * Three views over the same list the server returns:
   *  - "Mine" is what the user owns.
   *  - "Bookmarks" is other people's databases they saved. Your own are excluded, since
   *    they already have a tab; this one is for things you would otherwise hunt for.
   *  - "All" is everything the caller may open: every public database plus their own
   *    private ones. It is the default, and the only way to discover someone else's
   *    database in the first place.
   */
  scopedDatabases = computed(() => {
    const all = this.databasesSignal();

    switch (this.scope()) {
      case 'all':
        return all;
      case 'mine':
        return all.filter(db => db.isOwner);
      case 'bookmarks':
        return all.filter(db => db.isBookmarked && !db.isOwner);
    }
  });

  ownedCount = computed(() => this.databasesSignal().filter(db => db.isOwner).length);
  bookmarkedCount = computed(() => this.databasesSignal().filter(db => db.isBookmarked && !db.isOwner).length);
  allCount = computed(() => this.databasesSignal().length);

  filteredAndSortedDatabases = computed(() => {
    let result = this.scopedDatabases();
    const query = this.searchQuery().toLowerCase().trim();

    if (query) {
      result = result.filter(db =>
        db.name.toLowerCase().includes(query) ||
        db.owner.toLowerCase().includes(query)
      );
    }

    result = [...result].sort((a, b) => {
      const option = this.sortOption();

      switch (option) {
        case 'createdAsc':
          return a.creationDate.getTime() - b.creationDate.getTime();
        case 'createdDesc':
          return b.creationDate.getTime() - a.creationDate.getTime();
        case 'nameAsc':
          return a.name.localeCompare(b.name);
        case 'nameDesc':
          return b.name.localeCompare(a.name);
        case 'gamesAsc':
          return a.gamesCount - b.gamesCount;
        case 'gamesDesc':
          return b.gamesCount - a.gamesCount;
        default:
          return 0;
      }
    });

    return result;
  });

  selectScope(scope: DatabaseScope): void {
    this.scope.set(scope);
  }

  onToggleBookmark(database: Database, event: Event): void {
    event.stopPropagation();
    this.toggleBookmark.emit(database);
  }

  toggleSortMenu(): void {
    this.isSortMenuOpen.update(open => !open);
  }

  selectSort(option: 'createdDesc' | 'createdAsc' | 'nameAsc' | 'nameDesc' | 'gamesDesc' | 'gamesAsc'): void {
    this.sortOption.set(option);
    this.isSortMenuOpen.set(false);
  }

  requestRefresh(): void {
    if (this.refreshTimerId !== null) {
      window.clearTimeout(this.refreshTimerId);
    }

    this.isRefreshing.set(true);
    this.refreshDatabases.emit();
    this.refreshTimerId = window.setTimeout(() => {
      this.isRefreshing.set(false);
      this.refreshTimerId = null;
    }, 900);
  }

  onCreateDatabaseClicked(): void {
    if (!this.isRegisteredUser) {
      this.signInRequested.emit();
      return;
    }

    this.createName.set('');
    this.createVisibility.set('private');
    this.createError.set('');
    this.isCreateOpen.set(true);
  }

  closeCreateDatabase(): void {
    this.isCreateOpen.set(false);
  }

  confirmCreateDatabase(): void {
    const trimmedName = this.createName().trim();
    if (!trimmedName) {
      this.createError.set('Enter a database name.');
      return;
    }

    this.createDatabase.emit({ name: trimmedName, isPublic: this.createVisibility() === 'public' });
    this.closeCreateDatabase();
  }

  openSettings(database: Database): void {
    this.selectedDatabase.set(database);
    this.settingsName.set(database.name);
    this.settingsVisibility.set(database.isPublic ? 'public' : 'private');
    this.isSettingsOpen.set(true);
  }

  closeSettings(): void {
    this.isSettingsOpen.set(false);
  }

  confirmSettings(): void {
    const selected = this.selectedDatabase();
    if (!selected) {
      return;
    }

    this.updateDatabase.emit({
      database: selected,
      name: this.settingsName().trim() || selected.name,
      isPublic: this.settingsVisibility() === 'public'
    });
    this.closeSettings();
  }

  confirmDelete(): void {
    const selected = this.selectedDatabase();
    if (!selected) {
      return;
    }

    this.deleteDatabase.emit(selected);
    this.closeSettings();
  }
}
