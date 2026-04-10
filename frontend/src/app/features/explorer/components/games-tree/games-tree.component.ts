import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ExplorerMoveTreeMoveDto, ExplorerMoveTreeResponse } from '../../services/explorer-board-api.service';

@Component({
  selector: 'app-games-tree',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './games-tree.component.html',
  styleUrl: './games-tree.component.scss'
})
export class GamesTreeComponent {
  @Input() sourceType: 'imported' | 'external' | 'userDatabase' = 'imported';
  @Input() gamesLoaded = false;
  @Input() loading = false;
  @Input() error: string | null = null;
  @Input() moveTree: ExplorerMoveTreeResponse | null = null;
  @Output() readonly moveSelected = new EventEmitter<string>();

  protected get hasMoves(): boolean {
    return (this.moveTree?.moves.length ?? 0) > 0;
  }

  protected get emptyMessage(): string {
    if (!this.gamesLoaded) {
      return 'Open a database or import games to see move tree.';
    }

    if (this.sourceType === 'external') {
      return 'Move tree is available for imported and user-database games only.';
    }

    return 'No next moves found for this position in filtered games.';
  }

  protected trackByMove(index: number, item: ExplorerMoveTreeMoveDto): string {
    return `${item.moveSan}:${index}`;
  }

  protected shareOfPosition(move: ExplorerMoveTreeMoveDto): number {
    const total = this.moveTree?.totalGamesInPosition ?? 0;
    if (total <= 0 || move.games <= 0) {
      return 0;
    }

    return Math.round((move.games * 10000) / total) / 100;
  }

  protected onMoveClicked(move: ExplorerMoveTreeMoveDto): void {
    const san = move.moveSan?.trim();
    if (!san) {
      return;
    }

    this.moveSelected.emit(san);
  }
}
