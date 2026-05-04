import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface Database {
  id: string;
  name: string;
  owner: string;
  isPublic: boolean;
  creationDate: Date;
  gamesCount: number;
}

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
  @Input() set databases(value: Database[]) {
    this.databasesSignal.set(value ?? []);
  }
  @Output() openDatabase = new EventEmitter<Database>();
  @Output() deleteDatabase = new EventEmitter<Database>();
  @Output() refreshDatabases = new EventEmitter<void>();
  @Output() updateDatabase = new EventEmitter<{ database: Database; name: string; isPublic: boolean }>();

  searchQuery = signal('');
  sortOption = signal<'createdDesc' | 'createdAsc' | 'nameAsc' | 'nameDesc' | 'gamesDesc' | 'gamesAsc'>('createdDesc');
  isSortMenuOpen = signal(false);
  isRefreshing = signal(false);
  isSettingsOpen = signal(false);
  settingsName = signal('');
  settingsVisibility = signal<'private' | 'public'>('private');
  selectedDatabase = signal<Database | null>(null);

  private readonly databasesSignal = signal<Database[]>([]);
  private refreshTimerId: number | null = null;

  filteredAndSortedDatabases = computed(() => {
    let result = this.databasesSignal();
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
