import { Component, ElementRef, HostListener, ViewChild, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChessboardComponent } from '../../components/chessboard/chessboard.component';
import { GamesListComponent } from '../../components/games-list/games-list.component';
import { GamesTreeComponent } from '../../components/games-tree/games-tree.component';
import { MoveListComponent } from '../../components/move-list/move-list.component';
import { EmptyGamesStateComponent } from '../../components/empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../../components/filters-panel/filters-panel.component';

@Component({
  selector: 'app-explorer-page',
  standalone: true,
  imports: [CommonModule, ChessboardComponent, GamesTreeComponent, GamesListComponent, MoveListComponent, EmptyGamesStateComponent, FiltersPanelComponent],
  templateUrl: './explorer-page.component.html',
  styleUrl: './explorer-page.component.scss'
})
export class ExplorerPageComponent {
  @Input() isFocusMode = false;
  
  @ViewChild('layoutRoot', { static: true })
  private readonly layoutRoot!: ElementRef<HTMLElement>;

  protected readonly gamesLoaded = false;
  protected isResizing = false;
  protected leftPaneWidth = 620;
  
  protected focusRightTab: 'tree' | 'moves' | 'games' | 'filters' = 'tree';

  private static readonly minBoardWidth = 320;
  private static readonly minMoveListWidth = 145;
  private static readonly boardMoveGap = 12;
  private static readonly rightColumnMinWidth = 390;
  private static readonly handleWidth = 8;

  protected startResize(event: MouseEvent): void {
    event.preventDefault();
    this.isResizing = true;
  }

  @HostListener('window:mousemove', ['$event'])
  protected onWindowMouseMove(event: MouseEvent): void {
    if (!this.isResizing) {
      return;
    }

    const layoutBounds = this.layoutRoot.nativeElement.getBoundingClientRect();
    const requestedLeftWidth = event.clientX - layoutBounds.left;
    const minLeftWidth =
      ExplorerPageComponent.minBoardWidth +
      ExplorerPageComponent.minMoveListWidth +
      ExplorerPageComponent.boardMoveGap;

    const computedStyles = window.getComputedStyle(this.layoutRoot.nativeElement);
    const gridGap = Number.parseFloat(computedStyles.columnGap) || 0;
    const totalLayoutGaps = gridGap * 2;
    const maxLeftWidth = Math.max(
      minLeftWidth,
      layoutBounds.width -
        ExplorerPageComponent.rightColumnMinWidth -
        ExplorerPageComponent.handleWidth -
        totalLayoutGaps
    );

    this.leftPaneWidth = Math.min(Math.max(requestedLeftWidth, minLeftWidth), maxLeftWidth);
  }

  @HostListener('window:mouseup')
  protected onWindowMouseUp(): void {
    this.isResizing = false;
  }

  protected importDatabase(): void {
    // Placeholder action for opening a local PGN file picker.
    console.log('Import database (.pgn)');
  }

  protected searchCommunityDatabase(): void {
    // Placeholder action for searching a remote community database.
    console.log('Search database (community database)');
  }
}
