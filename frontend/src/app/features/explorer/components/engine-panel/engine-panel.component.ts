import { Component, DestroyRef, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, computed, inject, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { EngineBuild, EngineLine, EngineOption } from '../../services/engine.models';
import { StockfishEngineService } from '../../services/stockfish-engine.service';
import { MiniBoardComponent } from '../mini-board/mini-board.component';

/** One move of a displayed variation, with the move number printed before it, if any. */
interface VariationMove {
  /** `14.` before a White move, `14...` before a Black one that opens the line, else null. */
  prefix: string | null;
  san: string;
  /** 0-based index into the line's moves; playing it means playing every move up to here. */
  ply: number;
}

interface DisplayedLine {
  line: EngineLine;
  moves: VariationMove[];
}

interface PreviewTarget {
  multipv: number;
  ply: number;
  /** Where the hovered move sits on screen, to place the preview next to it. */
  anchor: DOMRect;
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
  imports: [MiniBoardComponent],
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

  /** The board's orientation, so a position preview is drawn the same way round. */
  @Input() flipped = false;

  /**
   * The moves of a line up to and including the one the user clicked, in SAN. The board plays
   * them; the panel never touches the position itself, so each move goes through the same
   * validation as a dragged piece.
   */
  @Output() readonly lineMovesSelected = new EventEmitter<string[]>();

  /** Plain-language names for the options whose UCI names are jargon. */
  private static readonly optionLabels: Record<string, string> = {
    Hash: 'Memory (MB)',
    UCI_LimitStrength: 'Limit strength',
    UCI_Elo: 'Target rating'
  };

  /** Preview width in px, including its frame; used to keep it inside the viewport. */
  private static readonly previewSize = 208;
  private static readonly previewGap = 8;

  protected readonly isSettingsOpen = signal(false);

  protected readonly builds = Object.values(StockfishEngineService.builds);

  /**
   * Lines split into individually clickable moves. Computed once per engine update rather
   * than per change-detection pass, since the engine updates several times a second.
   */
  protected readonly displayedLines = computed<DisplayedLine[]>(() => {
    const start = this.variationStart();
    return this.engine.lines().map(line => ({ line, moves: this.variationMoves(line, start) }));
  });

  /**
   * Which move is hovered, by line and ply rather than by position: the engine rewrites its
   * lines while the pointer rests on one, and the preview should follow what is now written
   * under the pointer, not the move that was there when it arrived.
   */
  private readonly previewTarget = signal<PreviewTarget | null>(null);

  protected readonly preview = computed(() => {
    const target = this.previewTarget();
    const fen = this.engine.analysedFen();
    if (!target || !fen) {
      return null;
    }

    const line = this.engine.lines().find(entry => entry.multipv === target.multipv);
    if (!line || target.ply >= line.pvSan.length) {
      return null;
    }

    const position = this.positionAfter(fen, line.pvUci.slice(0, target.ply + 1));
    if (!position) {
      return null;
    }

    return { ...position, ...this.previewPlacement(target.anchor) };
  });

  constructor() {
    // A fixed-position preview would otherwise stay put while the move it belongs to scrolls
    // away. Captured, because the page scrolls inside a container rather than the window, and
    // scroll events do not bubble.
    const hidePreview = () => this.previewTarget.set(null);
    window.addEventListener('scroll', hidePreview, { capture: true, passive: true });
    inject(DestroyRef).onDestroy(() => window.removeEventListener('scroll', hidePreview, { capture: true }));
  }

  ngOnInit(): void {
    // Starting here rather than in the service's constructor means a stored "on" setting
    // only downloads the engine once a board is actually on screen.
    this.engine.resumeIfPreviouslyEnabled();
    this.engine.setPosition(this.fen);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('fen' in changes) {
      this.engine.setPosition(this.fen);
      // The lines about to arrive describe a new position; a preview left over from the old
      // one would pop up under a pointer that has not moved.
      this.previewTarget.set(null);
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

  protected onBuildSelect(build: EngineBuild): void {
    this.engine.setBuild(build);
  }

  protected toggleSettings(): void {
    this.isSettingsOpen.update(open => !open);
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
   * Clicking a move plays the line up to and including it, so the board lands on exactly the
   * position the hover preview showed. Clicking the first move is a single step, which is how
   * a line usually gets explored; clicking further along jumps straight to where it leads.
   */
  protected onMoveClick(line: EngineLine, ply: number): void {
    const moves = line.pvSan.slice(0, ply + 1);
    if (moves.length === 0) {
      return;
    }

    this.previewTarget.set(null);
    this.lineMovesSelected.emit(moves);
  }

  protected showPreview(line: EngineLine, ply: number, event: Event): void {
    const anchor = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.previewTarget.set({ multipv: line.multipv, ply, anchor });
  }

  protected hidePreview(): void {
    this.previewTarget.set(null);
  }

  protected isPreviewed(line: EngineLine, ply: number): boolean {
    const target = this.previewTarget();
    return target?.multipv === line.multipv && target.ply === ply;
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

    // "Mn/s" rather than "M nodes/s": the row is narrow, and the short form is what every
    // other engine readout uses.
    if (nps >= 1_000_000) {
      return `${(nps / 1_000_000).toFixed(1)} Mn/s`;
    }

    return `${Math.round(nps / 1000)} kn/s`;
  }

  /**
   * The engine's name with the platform words dropped: "Stockfish 19 Lite WASM Multithreaded"
   * is accurate but spends the whole bar saying things shown elsewhere - that it runs in the
   * browser is on the line below it, and the threading is visible in the Threads control and
   * in the nodes/s figure. The exact string it reported stays on the hover title.
   */
  protected condensedEngineName(): string {
    return this.engine.displayName().replace(/\s+(WASM|Multithreaded)\b/g, '').trim();
  }

  protected labelFor(option: EngineOption): string {
    return EnginePanelComponent.optionLabels[option.name] ?? option.name;
  }

  protected bounds(option: EngineOption): { min: number; max: number } {
    return this.engine.boundsFor(option);
  }

  /**
   * Splits a variation into moves numbered the way the move list numbers them - `14... Nf6
   * 15. c4` - so it can be read against the game score rather than as a bare list of moves.
   */
  private variationMoves(line: EngineLine, start: { moveNumber: number; side: 'w' | 'b' } | null): VariationMove[] {
    let moveNumber = start?.moveNumber ?? 1;
    let side = start?.side ?? 'w';

    return line.pvSan.map((san, ply) => {
      let prefix: string | null = null;

      if (side === 'w') {
        prefix = `${moveNumber}.`;
        side = 'b';
      } else {
        // Only a variation that opens on Black's move needs the "14..." form; after that the
        // White move it answers is right there in the same line.
        prefix = ply === 0 ? `${moveNumber}...` : null;
        moveNumber++;
        side = 'w';
      }

      return { prefix, san, ply };
    });
  }

  /** Replays engine moves from `fen`; null if any of them does not replay. */
  private positionAfter(fen: string, movesUci: string[]): { fen: string; lastMove: { from: string; to: string } } | null {
    try {
      const position = new Chess(fen);
      let lastMove = { from: '', to: '' };

      for (const uci of movesUci) {
        const move = position.move({
          from: uci.slice(0, 2),
          to: uci.slice(2, 4),
          promotion: uci.length > 4 ? uci[4] : undefined
        });
        lastMove = { from: move.from, to: move.to };
      }

      return { fen: position.fen(), lastMove };
    } catch {
      return null;
    }
  }

  /**
   * Above the hovered move, where it covers the board rather than the other lines being
   * read; below it when there is no room above. Clamped horizontally to the viewport.
   */
  private previewPlacement(anchor: DOMRect): { top: number; left: number } {
    const size = EnginePanelComponent.previewSize;
    const gap = EnginePanelComponent.previewGap;

    const centred = anchor.left + anchor.width / 2 - size / 2;
    const left = Math.min(Math.max(centred, gap), window.innerWidth - size - gap);

    const above = anchor.top - size - gap;
    const top = above >= gap ? above : anchor.bottom + gap;

    return { top, left };
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
