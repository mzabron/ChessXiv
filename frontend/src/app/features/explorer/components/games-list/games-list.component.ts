import { Component, EventEmitter, Input, Output } from '@angular/core';
import { EmptyGamesStateComponent } from '../empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../filters-panel/filters-panel.component';
import { DatabasesPanelComponent } from '../databases-panel/databases-panel.component';
import { GamesTableComponent } from '../games-table/games-table.component';

@Component({
  selector: 'app-games-list',
  standalone: true,
  imports: [EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent],
  templateUrl: './games-list.component.html',
  styleUrl: './games-list.component.scss'
})
export class GamesListComponent {
  @Input() gamesLoaded = false;
  @Input() games: any[] = [];
  protected activeTab: 'databases' | 'games' | 'filters' = 'games';

  @Output() importDatabase = new EventEmitter<void>();
  @Output() searchCommunityDatabase = new EventEmitter<void>();

  protected selectTab(tab: 'databases' | 'games' | 'filters'): void {
    this.activeTab = tab;
  }
}
