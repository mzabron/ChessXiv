import { Component, EventEmitter, Output } from '@angular/core';
import { EmptyGamesStateComponent } from '../empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../filters-panel/filters-panel.component';

@Component({
  selector: 'app-games-list',
  standalone: true,
  imports: [EmptyGamesStateComponent, FiltersPanelComponent],
  templateUrl: './games-list.component.html',
  styleUrl: './games-list.component.scss'
})
export class GamesListComponent {
  protected activeTab: 'games' | 'filters' = 'games';
  protected readonly gamesLoaded = false;

  @Output() importDatabase = new EventEmitter<void>();
  @Output() searchCommunityDatabase = new EventEmitter<void>();

  protected selectTab(tab: 'games' | 'filters'): void {
    this.activeTab = tab;
  }
}
