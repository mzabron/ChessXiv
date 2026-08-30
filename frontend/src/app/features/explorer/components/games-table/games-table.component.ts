import { Component, HostListener, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DraftGameListItem, DraftGamesResultSortMode, DraftGamesSortBy, DraftGamesSortDirection } from '../../services/draft-import-api.service';
import { EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-games-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './games-table.component.html',
  styleUrl: './games-table.component.scss'
})
export class GamesTableComponent {
  @Input() games: DraftGameListItem[] = [];
  @Input() totalCount = 0;
  @Input() page = 1;
  @Input() pageSize = 18;
  @Input() resultSortMode: DraftGamesResultSortMode = 'default';
  @Input() sortBy: DraftGamesSortBy = 'createdAt';
  @Input() sortDirection: DraftGamesSortDirection = 'desc';

  @Output() resultSortModeChange = new EventEmitter<DraftGamesResultSortMode>();
  @Output() sortChange = new EventEmitter<{ sortBy: DraftGamesSortBy; sortDirection: DraftGamesSortDirection }>();
  @Output() pageSizeChange = new EventEmitter<number>();
  @Output() pageChange = new EventEmitter<number>();
  @Output() gameSelected = new EventEmitter<DraftGameListItem>();

  @Input() selectedGameId: string | null = null;
  /** Shows skeleton rows instead of an empty table while a page is in flight. */
  @Input() isLoading = false;

  /** Enough rows to fill the visible area; the count itself carries no meaning. */
  protected readonly skeletonRows = Array.from({ length: 8 }, (_, index) => index);
  /** One per data column, matching the header. */
  protected readonly skeletonCells = Array.from({ length: 9 }, (_, index) => index);

  /** Tick-box selection, used by "Add to database" to add specific games. */
  @Input() selectedGameIds: string[] = [];
  /** Hidden when the caller has nowhere to add games to, e.g. a signed-out visitor. */
  @Input() selectionEnabled = false;

  @Output() selectedGameIdsChange = new EventEmitter<string[]>();

  /**
   * Up/Down step through the loaded games, mirroring how Left/Right step through moves on
   * the board. Only the current page is walked - paging on arrow key would be a surprising
   * amount of movement for one keystroke.
   */
  @HostListener('window:keydown', ['$event'])
  protected onWindowKeyDown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') {
      return;
    }

    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) {
      return;
    }

    // Never steal the key from a form control or anything else that is focus-managed.
    const target = event.target as HTMLElement | null;
    if (target && (['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable)) {
      return;
    }

    if (this.games.length === 0) {
      return;
    }

    const currentIndex = this.games.findIndex(game => game.id === this.selectedGameId);
    const delta = event.key === 'ArrowDown' ? 1 : -1;

    // With nothing selected, Down opens the first game and Up the last.
    const nextIndex = currentIndex === -1
      ? (delta === 1 ? 0 : this.games.length - 1)
      : currentIndex + delta;

    if (nextIndex < 0 || nextIndex >= this.games.length) {
      return;
    }

    event.preventDefault();
    this.gameSelected.emit(this.games[nextIndex]);
  }

  protected isChecked(gameId: string): boolean {
    return this.selectedGameIds.includes(gameId);
  }

  protected get allOnPageChecked(): boolean {
    return this.games.length > 0 && this.games.every(game => this.isChecked(game.id));
  }

  protected toggleGameChecked(gameId: string): void {
    const next = this.isChecked(gameId)
      ? this.selectedGameIds.filter(id => id !== gameId)
      : [...this.selectedGameIds, gameId];

    this.selectedGameIdsChange.emit(next);
  }

  protected togglePageChecked(): void {
    const pageIds = this.games.map(game => game.id);

    if (this.allOnPageChecked) {
      this.selectedGameIdsChange.emit(this.selectedGameIds.filter(id => !pageIds.includes(id)));
      return;
    }

    const merged = new Set([...this.selectedGameIds, ...pageIds]);
    this.selectedGameIdsChange.emit([...merged]);
  }

  protected readonly totalPages = () => {
    const pages = Math.ceil(this.totalCount / this.pageSize);
    return Math.max(1, pages);
  };

  protected toggleSort(column: DraftGamesSortBy): void {
    if (column === 'result') {
      const nextMode = this.nextResultSortMode();
      this.resultSortModeChange.emit(nextMode);

      if (nextMode === 'default') {
        this.sortChange.emit({ sortBy: 'createdAt', sortDirection: 'desc' });
        return;
      }

      this.sortChange.emit({ sortBy: 'result', sortDirection: 'asc' });
      return;
    }

    if (this.sortBy !== column) {
      this.sortChange.emit({ sortBy: column, sortDirection: 'desc' });
      return;
    }

    if (this.sortDirection === 'desc') {
      this.sortChange.emit({ sortBy: column, sortDirection: 'asc' });
      return;
    }

    this.sortChange.emit({ sortBy: 'createdAt', sortDirection: 'desc' });
  }

  protected onPageSizeChanged(value: string): void {
    const parsed = Number.parseInt(value, 10);
    if (Number.isNaN(parsed) || parsed <= 0) {
      return;
    }

    this.pageSizeChange.emit(parsed);
  }

  protected goToPreviousPage(): void {
    if (this.page <= 1) {
      return;
    }

    this.pageChange.emit(this.page - 1);
  }

  protected goToNextPage(): void {
    const maxPage = this.totalPages();
    if (this.page >= maxPage) {
      return;
    }

    this.pageChange.emit(this.page + 1);
  }

  protected selectGame(game: DraftGameListItem): void {
    this.gameSelected.emit(game);
  }

  protected sortIndicator(column: DraftGamesSortBy): string {
    if (column === 'result') {
      if (this.resultSortMode === 'whiteFirst') {
        return ' 1-0';
      }

      if (this.resultSortMode === 'blackFirst') {
        return ' 0-1';
      }

      if (this.resultSortMode === 'drawFirst') {
        return ' 1/2';
      }

      return '';
    }

    if (this.sortBy !== column) {
      return '';
    }

    return this.sortDirection === 'asc' ? ' ↑' : ' ↓';
  }

  private nextResultSortMode(): DraftGamesResultSortMode {
    return this.resultSortMode === 'default'
      ? 'whiteFirst'
      : this.resultSortMode === 'whiteFirst'
        ? 'blackFirst'
        : this.resultSortMode === 'blackFirst'
          ? 'drawFirst'
          : 'default';
  }
}
