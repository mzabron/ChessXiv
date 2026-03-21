import { Component, ElementRef, HostListener, ViewChild, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChessboardComponent } from '../../components/chessboard/chessboard.component';
import { GamesListComponent } from '../../components/games-list/games-list.component';
import { GamesTreeComponent } from '../../components/games-tree/games-tree.component';
import { MoveListComponent } from '../../components/move-list/move-list.component';
import { EmptyGamesStateComponent } from '../../components/empty-games-state/empty-games-state.component';
import { FiltersPanelComponent } from '../../components/filters-panel/filters-panel.component';
import { DatabasesPanelComponent } from '../../components/databases-panel/databases-panel.component';
import { GamesTableComponent } from '../../components/games-table/games-table.component';

@Component({
  selector: 'app-explorer-page',
  standalone: true,
  imports: [CommonModule, ChessboardComponent, GamesTreeComponent, GamesListComponent, MoveListComponent, EmptyGamesStateComponent, FiltersPanelComponent, DatabasesPanelComponent, GamesTableComponent],
  templateUrl: './explorer-page.component.html',
  styleUrl: './explorer-page.component.scss'
})
export class ExplorerPageComponent {
  @Input() isFocusMode = false;

  @ViewChild('layoutRoot', { static: true })
  private readonly layoutRoot!: ElementRef<HTMLElement>;

  protected gamesLoaded = false;
  protected mockGames: any[] = [
    {
      year: 2023,
      white: 'Carlsen, M.',
      whiteElo: 2853,
      result: '1-0',
      black: 'Nakamura, H.',
      blackElo: 2789,
      eco: 'C65',
      event: 'Norway Chess',
      moveCount: 42
    },
    {
      year: 2023,
      white: 'Caruana, F.',
      whiteElo: 2782,
      result: '1/2-1/2',
      black: 'Ding, L.',
      blackElo: 2788,
      eco: 'D37',
      event: 'World Championship',
      moveCount: 68
    },
    {
      year: 2024,
      white: 'Fiorito, F.',
      whiteElo: 2470,
      result: '0-1',
      black: 'Carlsen, M.',
      blackElo: 2832,
      eco: 'D31',
      event: 'World Blitz',
      moveCount: 38
    }
  ];
  protected isResizing = false;
  protected leftPaneWidth = 620;

  protected focusRightTab: 'databases' | 'tree' | 'moves' | 'games' | 'filters' = 'tree';

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
    this.gamesLoaded = true;
  }

  protected searchCommunityDatabase(): void {
    // Placeholder action for searching a remote community database.
    console.log('Search database (community database)');
  }
}
