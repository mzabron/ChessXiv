import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ExplorerGamesFilterState, createDefaultExplorerGamesFilterState } from '../../services/games-filters.models';

@Component({
  selector: 'app-filters-panel',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './filters-panel.component.html',
  styleUrl: './filters-panel.component.scss'
})
export class FiltersPanelComponent implements OnChanges {
  @Input() gamesLoaded = false;
  @Input() boardFen = '';
  @Input() value: ExplorerGamesFilterState = createDefaultExplorerGamesFilterState();

  @Output() valueChange = new EventEmitter<ExplorerGamesFilterState>();
  @Output() apply = new EventEmitter<ExplorerGamesFilterState>();
  @Output() reset = new EventEmitter<void>();

  protected model: ExplorerGamesFilterState = createDefaultExplorerGamesFilterState();
  protected validationError = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (!('value' in changes)) {
      return;
    }

    this.model = { ...this.value };
  }

  protected onModelChanged(): void {
    this.validationError = '';
    this.valueChange.emit({ ...this.model });
  }

  protected applyFilters(): void {
    if (!this.gamesLoaded) {
      return;
    }

    const normalized = this.normalizeModel(this.model);
    const validationError = this.validate(normalized);
    if (validationError) {
      this.validationError = validationError;
      return;
    }

    this.validationError = '';
    this.model = normalized;
    this.valueChange.emit({ ...normalized });
    this.apply.emit({ ...normalized });
  }

  protected resetFilters(): void {
    this.validationError = '';
    this.reset.emit();
  }

  private validate(model: ExplorerGamesFilterState): string {
    if (model.eloEnabled && model.eloFrom !== null && model.eloTo !== null && model.eloFrom > model.eloTo) {
      return 'Elo from must be less than or equal to Elo to.';
    }

    if (model.yearEnabled && model.yearFrom !== null && model.yearTo !== null && model.yearFrom > model.yearTo) {
      return 'Year from must be less than or equal to Year to.';
    }

    if (model.moveEnabled && model.moveCountFrom !== null && model.moveCountTo !== null && model.moveCountFrom > model.moveCountTo) {
      return 'Move count from must be less than or equal to Move count to.';
    }

    if (model.searchByPosition && this.boardFen.trim().length === 0) {
      return 'Board position is unavailable. Load a game or set up a position on the board.';
    }

    return '';
  }

  private normalizeModel(model: ExplorerGamesFilterState): ExplorerGamesFilterState {
    const page = this.coercePositiveInteger(model.page, 1);
    const pageSize = this.coerceInRangeInteger(model.pageSize, 1, 200, 18);

    return {
      ...model,
      whiteFirstName: model.whiteFirstName.trim(),
      whiteLastName: model.whiteLastName.trim(),
      blackFirstName: model.blackFirstName.trim(),
      blackLastName: model.blackLastName.trim(),
      ecoCode: model.ecoCode.trim(),
      result: model.result.trim(),
      fen: model.searchByPosition ? this.boardFen.trim() : model.fen.trim(),
      eloFrom: this.coerceNullableNumber(model.eloFrom),
      eloTo: this.coerceNullableNumber(model.eloTo),
      yearFrom: this.coerceNullableNumber(model.yearFrom),
      yearTo: this.coerceNullableNumber(model.yearTo),
      moveCountFrom: model.moveEnabled ? this.coerceNullableNumber(model.moveCountFrom) : null,
      moveCountTo: model.moveEnabled ? this.coerceNullableNumber(model.moveCountTo) : null,
      page,
      pageSize
    };
  }

  private coerceNullableNumber(value: number | null): number | null {
    if (value === null || Number.isNaN(value)) {
      return null;
    }

    return value;
  }

  private coercePositiveInteger(value: number, fallback: number): number {
    if (!Number.isFinite(value)) {
      return fallback;
    }

    const normalized = Math.trunc(value);
    return normalized > 0 ? normalized : fallback;
  }

  private coerceInRangeInteger(value: number, min: number, max: number, fallback: number): number {
    if (!Number.isFinite(value)) {
      return fallback;
    }

    const normalized = Math.trunc(value);
    if (normalized < min) {
      return min;
    }

    if (normalized > max) {
      return max;
    }

    return normalized;
  }
}
