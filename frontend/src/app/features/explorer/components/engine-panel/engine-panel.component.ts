import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { EngineLine, EngineOption } from '../../services/engine.models';
import { StockfishEngineService } from '../../services/stockfish-engine.service';

/** One rendered piece of a variation: either a move number, or a move that can be played. */
interface VariationToken {
  text: string;
  /** Index into the line's SAN moves, or null for a move number. */
  moveIndex: number | null;
}

/**
 * Local-engine readout that sits under the board: an on/off switch, the evaluation, the
 * top variations, and the engine's own UCI options.
 *
 * It owns no analysis state of its own - everything comes from the root-level
 * {@link StockfishEngineService}, so the engine survives the board being recreated when
 * the page switches into focus mode.
 */
@Component({
  selector: 'app-engine-panel',
  standalone: true,
  templateUrl: './engine-panel.component.html',
  styleUrl: './engine-panel.component.scss'
})
export class EnginePanelComponent implements OnInit, OnChanges {
  protected readonly engine = inject(StockfishEngineService);

  /** Selectable line counts, materialised once so the template does not rebuild it. */
  protected readonly lineChoices = Array.from(
    { length: StockfishEngineService.maxLines - StockfishEngineService.minLines + 1 },
    (_, index) => StockfishEngineService.minLines + index
  );

  /** Position to analyse. Null suspends analysis - during position setup, for instance. */
  @Input() fen: string | null = null;

  /**
   * Moves the user picked out of a variation, in SAN, from the analysed position up to and
   * including the one clicked. The board plays them; the panel does not touch the position
   * itself, so every move still goes through the same validation as a dragged piece.
   */
  @Output() readonly variationMovesSelected = new EventEmitter<string[]>();

  protected readonly isSettingsOpen = signal(false);
  protected readonly areAdvancedOptionsOpen = signal(false);

  ngOnInit(): void {
    // Starting here rather than in the service's constructor means a stored "on" setting
    // only downloads the engine once a board is actually on screen.
    this.engine.resumeIfPreviouslyEnabled();
    this.engine.setPosition(this.fen);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('fen' in changes) {
      this.engine.setPosition(this.fen);
    }
  }

  protected toggleEngine(): void {
    this.engine.toggle();

    if (this.engine.isEnabled()) {
      this.engine.setPosition(this.fen);
      return;
    }

    this.isSettingsOpen.set(false);
  }

  protected toggleEvalBar(): void {
    this.engine.toggleEvalBar();
  }

  protected toggleLines(): void {
    this.engine.toggleLines();
  }

  protected toggleSettings(): void {
    this.isSettingsOpen.update(open => !open);
  }

  protected toggleAdvancedOptions(): void {
    this.areAdvancedOptionsOpen.update(open => !open);
  }

  protected onLineCountInput(value: string): void {
    this.engine.setLineCount(Number(value));
  }

  protected onSpinOptionInput(option: EngineOption, value: string): void {
    this.engine.setOption(option.name, value);
  }

  protected onCheckOptionChange(option: EngineOption, checked: boolean): void {
    this.engine.setOption(option.name, checked ? 'true' : 'false');
  }

  protected onTextOptionChange(option: EngineOption, value: string): void {
    this.engine.setOption(option.name, value);
  }

  protected isOptionChecked(option: EngineOption): boolean {
    return this.engine.valueOf(option.name) === 'true';
  }

  /** The headline evaluation, which is always that of the engine's first line. */
  protected formatMainScore(): string {
    const best = this.engine.lines()[0];
    return best ? this.formatScore(best) : '—';
  }

  /**
   * Scores are shown from White's perspective: `+0.83` favours White, `-1.20` favours
   * Black, `#4` is mate for White in four and `-#4` mate against White.
   */
  protected formatScore(line: EngineLine): string {
    if (line.mate !== null) {
      return line.mate >= 0 ? `#${line.mate}` : `-#${Math.abs(line.mate)}`;
    }

    if (line.cp === null) {
      return '—';
    }

    const pawns = line.cp / 100;
    return `${pawns > 0 ? '+' : pawns < 0 ? '−' : ''}${Math.abs(pawns).toFixed(2)}`;
  }

  protected scoreClass(line: EngineLine): string {
    const advantage = line.mate !== null ? line.mate : (line.cp ?? 0);
    if (advantage > 0) {
      return 'is-white-better';
    }

    return advantage < 0 ? 'is-black-better' : 'is-level';
  }

  /**
   * Splits a variation into the pieces the template renders: move numbers as plain text,
   * and each move as its own clickable token. Numbering follows the move list - `14... Nf6
   * 15. c4` - so a line can be read against the game score rather than as a bare list.
   */
  protected variationTokens(line: EngineLine): VariationToken[] {
    const start = this.variationStart();
    let moveNumber = start?.moveNumber ?? 1;
    let side = start?.side ?? 'w';

    const tokens: VariationToken[] = [];

    line.pvSan.forEach((san, index) => {
      if (side === 'w') {
        tokens.push({ text: `${moveNumber}.`, moveIndex: null });
        tokens.push({ text: san, moveIndex: index });
        side = 'b';
        return;
      }

      // Only a variation that opens on Black's move needs the "14..." form; after that the
      // White move it answers is right there in the same line.
      if (index === 0) {
        tokens.push({ text: `${moveNumber}...`, moveIndex: null });
      }

      tokens.push({ text: san, moveIndex: index });
      moveNumber++;
      side = 'w';
    });

    return tokens;
  }

  /** Plays the variation up to the clicked move, so one click can follow a whole line. */
  protected onVariationMoveClick(line: EngineLine, moveIndex: number | null): void {
    if (moveIndex === null) {
      return;
    }

    this.variationMovesSelected.emit(line.pvSan.slice(0, moveIndex + 1));
  }

  protected formatDepth(): string {
    const depth = this.engine.depth();
    return depth > 0 ? `depth ${depth}` : '';
  }

  protected formatSpeed(): string {
    const nps = this.engine.nps();
    if (nps <= 0) {
      return '';
    }

    if (nps >= 1_000_000) {
      return `${(nps / 1_000_000).toFixed(1)}M nodes/s`;
    }

    return `${Math.round(nps / 1000)}k nodes/s`;
  }

  protected bounds(option: EngineOption): { min: number; max: number } {
    return this.engine.boundsFor(option);
  }

  /** Move number and side to move of the position under analysis. */
  private variationStart(): { moveNumber: number; side: 'w' | 'b' } | null {
    const fen = this.engine.analysedFen();
    if (!fen) {
      return null;
    }

    try {
      const position = new Chess(fen);
      return { moveNumber: position.moveNumber(), side: position.turn() };
    } catch {
      return null;
    }
  }
}
