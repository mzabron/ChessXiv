import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ExplorerMoveTreeMoveDto, ExplorerMoveTreeResponse } from '../../services/explorer-board-api.service';

type ResultKind = 'white' | 'draw' | 'black';

/** One coloured band of a move's win/draw/loss bar. Zero-count results get no band. */
interface WinBarSegment {
  readonly kind: ResultKind;
  /** Drives flex-grow, so the bands always fill the bar exactly. */
  readonly weight: number;
}

/** A percentage in the legend above the bar. Always present, whatever its size. */
interface WinFigure {
  readonly kind: ResultKind;
  readonly name: string;
  readonly label: string;
}

/** A move row, with everything the template needs already computed. */
interface MoveTreeRow {
  readonly move: ExplorerMoveTreeMoveDto;
  readonly sharePct: number;
  readonly segments: readonly WinBarSegment[];
  readonly figures: readonly WinFigure[];
  readonly barTitle: string;
}

@Component({
  selector: 'app-games-tree',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './games-tree.component.html',
  styleUrl: './games-tree.component.scss'
})
export class GamesTreeComponent {
  private static readonly resultNames: Readonly<Record<ResultKind, string>> = {
    white: 'White',
    draw: 'Draws',
    black: 'Black'
  };

  @Input() sourceType: 'imported' | 'userDatabase' = 'imported';
  @Input() gamesLoaded = false;
  @Input() loading = false;
  @Input() error: string | null = null;
  @Output() readonly moveSelected = new EventEmitter<string>();

  protected rows: readonly MoveTreeRow[] = [];
  protected totalGamesInPosition = 0;

  /**
   * Rows are derived on assignment rather than in template getters: the bar maths would
   * otherwise re-run for every move on every change-detection pass.
   */
  @Input()
  set moveTree(value: ExplorerMoveTreeResponse | null) {
    this.totalGamesInPosition = value?.totalGamesInPosition ?? 0;
    this.rows = (value?.moves ?? []).map(move => this.buildRow(move));
  }

  protected get hasMoves(): boolean {
    return this.rows.length > 0;
  }

  protected get emptyMessage(): string {
    if (!this.gamesLoaded) {
      return 'Open a database or import games to see move tree.';
    }

    return 'No next moves found for this position in filtered games.';
  }

  protected trackByRow(index: number, row: MoveTreeRow): string {
    return `${row.move.moveSan}:${index}`;
  }

  protected onMoveClicked(row: MoveTreeRow): void {
    const san = row.move.moveSan?.trim();
    if (!san) {
      return;
    }

    this.moveSelected.emit(san);
  }

  private buildRow(move: ExplorerMoveTreeMoveDto): MoveTreeRow {
    const counts: ReadonlyArray<readonly [ResultKind, number]> = [
      ['white', move.whiteWins],
      ['draw', move.draws],
      ['black', move.blackWins]
    ];
    const decided = counts.reduce((sum, [, count]) => sum + count, 0);

    return {
      move,
      sharePct: this.shareOfPosition(move),
      // Only non-zero results get a band. A zero-weight band still renders its 1px of
      // border and rounding, which is exactly the sort of stray artefact this bar must not
      // produce - and the figure beside it already says "0%".
      segments: counts
        .filter(([, count]) => count > 0)
        .map(([kind, count]) => ({ kind, weight: count })),
      // Figures, unlike bands, are unconditional: the whole point of splitting them out of
      // the bar is that a share too small to draw is still a number worth reading.
      figures: GamesTreeComponent.buildFigures(counts, decided),
      barTitle: GamesTreeComponent.buildBarTitle(move)
    };
  }

  private shareOfPosition(move: ExplorerMoveTreeMoveDto): number {
    const total = this.totalGamesInPosition;
    if (total <= 0 || move.games <= 0) {
      return 0;
    }

    return Math.round((move.games * 10000) / total) / 100;
  }

  /**
   * Whole percentages, because three of them share one narrow line.
   *
   * Rounded by largest remainder rather than independently: 39 wins and 1 loss in 40 games
   * round to 98% and 3% on their own, and a row that reads "101%" undermines every other
   * number on the panel. Distributing the leftover point to the largest fraction instead
   * keeps the three figures adding up.
   */
  private static buildFigures(
    counts: ReadonlyArray<readonly [ResultKind, number]>,
    decided: number): readonly WinFigure[] {
    if (decided <= 0) {
      return counts.map(([kind]) => ({ kind, name: GamesTreeComponent.resultNames[kind], label: '0%' }));
    }

    const exact = counts.map(([, count]) => (count * 100) / decided);
    const rounded = exact.map(Math.floor);
    let leftover = 100 - rounded.reduce((sum, value) => sum + value, 0);

    for (const { index } of exact
      .map((value, index) => ({ index, fraction: value - Math.floor(value) }))
      .sort((a, b) => b.fraction - a.fraction)) {
      if (leftover <= 0) {
        break;
      }

      rounded[index]++;
      leftover--;
    }

    return counts.map(([kind, count], index) => ({
      kind,
      name: GamesTreeComponent.resultNames[kind],
      label: GamesTreeComponent.formatPercentage(rounded[index], count, decided)
    }));
  }

  /**
   * Rounding must never turn a result that happened into one that did not: a single draw
   * in 400 games reads "<1%", not "0%", and the wins beside it read ">99%", not "100%".
   * Those two cases are the only ones where the figures stop summing to exactly 100 - the
   * alternative is a row that claims a result never occurred.
   */
  private static formatPercentage(rounded: number, count: number, decided: number): string {
    if (count <= 0) {
      return '0%';
    }

    if (rounded <= 0) {
      return '<1%';
    }

    if (rounded >= 100 && count < decided) {
      return '>99%';
    }

    return `${rounded}%`;
  }

  private static buildBarTitle(move: ExplorerMoveTreeMoveDto): string {
    const format = (pct: number, count: number) => `${Math.round(pct * 100) / 100}% (${count})`;
    return [
      `White wins ${format(move.whiteWinPct, move.whiteWins)}`,
      `draws ${format(move.drawPct, move.draws)}`,
      `black wins ${format(move.blackWinPct, move.blackWins)}`
    ].join(' · ');
  }
}
