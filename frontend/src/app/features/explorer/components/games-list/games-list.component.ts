import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EmptyGamesStateComponent } from '../empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../filters-panel/filters-panel.component';
import { DatabasesPanelComponent } from '../databases-panel/databases-panel.component';
import { GamesTableComponent } from '../games-table/games-table.component';

interface UserDatabaseOption {
  id: string;
  name: string;
}

interface SaveDatabaseRequestPayload {
  mode: 'merge' | 'create';
  targetDatabaseId?: string;
  newDatabaseName?: string;
  visibility: 'private' | 'public';
}

@Component({
  selector: 'app-games-list',
  standalone: true,
  imports: [FormsModule, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent],
  templateUrl: './games-list.component.html',
  styleUrl: './games-list.component.scss'
})
export class GamesListComponent {
  @Input() gamesLoaded = false;
  @Input() games: any[] = [];
  @Input() databaseName = 'Games';
  @Input() sourceType: 'imported' | 'external' = 'imported';
  @Input() myDatabases: UserDatabaseOption[] = [];
  protected activeTab: 'databases' | 'games' | 'filters' = 'games';
  protected isSaveModalOpen = false;
  protected saveMode: 'merge' | 'create' = 'merge';
  protected selectedTargetDatabaseId = '';
  protected newDatabaseName = '';
  protected newDatabaseVisibility: 'private' | 'public' = 'private';
  protected saveModalError = '';

  @Output() importDatabase = new EventEmitter<void>();
  @Output() searchCommunityDatabase = new EventEmitter<void>();
  @Output() saveDatabase = new EventEmitter<void>();
  @Output() addBookmark = new EventEmitter<void>();
  @Output() saveDatabaseRequest = new EventEmitter<SaveDatabaseRequestPayload>();

  protected selectTab(tab: 'databases' | 'games' | 'filters'): void {
    this.activeTab = tab;
  }

  protected openSaveDatabaseModal(): void {
    this.isSaveModalOpen = true;
    this.saveMode = 'merge';
    this.selectedTargetDatabaseId = this.myDatabases[0]?.id ?? '';
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
        mode: 'merge',
        targetDatabaseId: this.selectedTargetDatabaseId,
        visibility: 'private'
      });
      this.saveDatabase.emit();
      this.closeSaveDatabaseModal();
      return;
    }

    const trimmedName = this.newDatabaseName.trim();
    if (!trimmedName) {
      this.saveModalError = 'Enter a new database name.';
      return;
    }

    this.saveDatabaseRequest.emit({
      mode: 'create',
      newDatabaseName: trimmedName,
      visibility: this.newDatabaseVisibility
    });
    this.saveDatabase.emit();
    this.closeSaveDatabaseModal();
  }
}
