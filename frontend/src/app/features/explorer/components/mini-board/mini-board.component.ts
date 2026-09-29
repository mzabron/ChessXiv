import { Component, computed, input } from '@angular/core';
import { PIECE_URLS } from '../chessboard/piece-images';

interface MiniSquare {
  square: string;
  isLight: boolean;
  pieceUrl: string | null;
  isHighlighted: boolean;
}

const FILES = 'abcdefgh';

/**
 * A static, non-interactive picture of a position - used to preview where an engine line
 * leads without moving the real board there.
 *
 * Deliberately separate from the board component: that one owns move validation, drag and
 * drop, setup mode and history, none of which a 12rem thumbnail needs.
 */
@Component({
  selector: 'app-mini-board',
  standalone: true,
  templateUrl: './mini-board.component.html',
  styleUrl: './mini-board.component.scss'
})
export class MiniBoardComponent {
  readonly fen = input.required<string>();
  /** Matches the real board's orientation, so the preview reads the same way round. */
  readonly flipped = input(false);
  /** The move that produced this position, drawn as two tinted squares. */
  readonly lastMove = input<{ from: string; to: string } | null>(null);

  protected readonly squares = computed<MiniSquare[]>(() => {
    const pieces = this.placement(this.fen());
    const lastMove = this.lastMove();
    const flipped = this.flipped();
    const result: MiniSquare[] = [];

    for (let row = 0; row < 8; row++) {
      for (let column = 0; column < 8; column++) {
        const fileIndex = flipped ? 7 - column : column;
        const rank = flipped ? row + 1 : 8 - row;
        const square = `${FILES[fileIndex]}${rank}`;
        const piece = pieces.get(square);

        result.push({
          square,
          isLight: (row + column) % 2 === 0,
          pieceUrl: piece ? PIECE_URLS[piece] ?? null : null,
          isHighlighted: square === lastMove?.from || square === lastMove?.to
        });
      }
    }

    return result;
  });

  /** Square to piece code (`wk`, `bp`, ...) from the placement field of a FEN. */
  private placement(fen: string): Map<string, string> {
    const pieces = new Map<string, string>();
    const rows = fen.split(' ')[0]?.split('/') ?? [];

    rows.forEach((row, rowIndex) => {
      let fileIndex = 0;
      for (const symbol of row) {
        if (/\d/.test(symbol)) {
          fileIndex += Number(symbol);
          continue;
        }

        const colour = symbol === symbol.toUpperCase() ? 'w' : 'b';
        pieces.set(`${FILES[fileIndex]}${8 - rowIndex}`, `${colour}${symbol.toLowerCase()}`);
        fileIndex++;
      }
    });

    return pieces;
  }
}
