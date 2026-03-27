import { Component, Input } from '@angular/core';
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
