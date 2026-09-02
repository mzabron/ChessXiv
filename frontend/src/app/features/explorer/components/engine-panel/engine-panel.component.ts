import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { EngineLine, EngineOption } from '../../services/engine.models';
import { StockfishEngineService } from '../../services/stockfish-engine.service';

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
   * The first move of a line the user clicked, in SAN. The board plays it; the panel never
   * touches the position itself, so the move goes through the same validation as a dragged
   * piece.
   */
  @Output() readonly lineMoveSelected = new EventEmitter<string>();

  /**
   * Plain-language names for options whose UCI names are jargon, and a sentence each on what
   * they actually do - shown on the help icon beside every control, because "nodestime" and
   * "MultiPV" tell a user nothing about whether they want them.
   */
  private static readonly optionLabels: Record<string, string> = {
    Hash: 'Memory (MB)',
    UCI_LimitStrength: 'Limit strength',
    UCI_Elo: 'Target rating',
    UCI_ShowWDL: 'Win / draw / loss'
  };

  private static readonly optionHelp: Record<string, string> = {
    MultiPV:
      'How many different continuations the engine reports. The search effort is split ' +
      'between them, so five lines are each analysed less deeply than one.',
    Threads:
      'CPU cores the engine may use. More cores means more positions searched per second. ' +
      'Leaving one free keeps the rest of the page responsive.',
    Hash:
      'Memory for the engine\'s table of positions it has already evaluated. Too little and ' +
      'it keeps recalculating the same positions.',
    'Clear Hash':
      'Empties that table, so the next evaluation starts with no memory of earlier analysis.',
    UCI_LimitStrength:
      'Makes the engine play at the target rating below instead of at full strength.',
    UCI_Elo:
      'The rating the engine plays at once "Limit strength" is on. It does nothing while ' +
      'that box is unchecked.',
    UCI_ShowWDL:
      'Adds the engine\'s estimated win / draw / loss chances beside the evaluation.'
  };

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
   * Renders a variation the way the move list does - `14... Nf6 15. c4` - so it can be read
   * against the game score rather than as a bare list of moves.
   */
  protected formatVariation(line: EngineLine): string {
    const start = this.variationStart();
    let moveNumber = start?.moveNumber ?? 1;
    let side = start?.side ?? 'w';
    const parts: string[] = [];

    line.pvSan.forEach((san, index) => {
      if (side === 'w') {
        parts.push(`${moveNumber}. ${san}`);
        side = 'b';
        return;
      }

      // Only a variation that opens on Black's move needs the "14..." form; after that the
      // White move it answers is right there in the same line.
      parts.push(index === 0 ? `${moveNumber}... ${san}` : san);
      moveNumber++;
      side = 'w';
    });

    return parts.join(' ');
  }

  /**
   * Playing a whole line at once would move the board several plies away from what the user
   * is looking at, so a click takes the first move only - the position advances one step and
   * the engine re-analyses from there, which is how a line gets explored in practice.
   */
  protected onLineClick(line: EngineLine): void {
    const firstMove = line.pvSan[0];
    if (firstMove) {
      this.lineMoveSelected.emit(firstMove);
    }
  }

  protected firstMoveOf(line: EngineLine): string {
    return line.pvSan[0] ?? '';
  }

  /** Win/draw/loss for a line, as whole percentages. Null unless UCI_ShowWDL is on. */
  protected formatWdl(line: EngineLine): string | null {
    if (!line.wdl) {
      return null;
    }

    const asPercent = (perMille: number) => Math.round(perMille / 10);
    return `${asPercent(line.wdl.win)} / ${asPercent(line.wdl.draw)} / ${asPercent(line.wdl.loss)}`;
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

  protected labelFor(option: EngineOption): string {
    return EnginePanelComponent.optionLabels[option.name] ?? option.name;
  }

  /** Help text for a control, with the raw UCI name so it stays traceable to the protocol. */
  protected helpFor(name: string): string | null {
    const help = EnginePanelComponent.optionHelp[name];
    return help ? `${name} — ${help}` : null;
  }

  /** Win/draw/loss for the engine's best line, shown in the bar when UCI_ShowWDL is on. */
  protected mainWdl(): string | null {
    const line = this.engine.mainLine();
    return line ? this.formatWdl(line) : null;
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
