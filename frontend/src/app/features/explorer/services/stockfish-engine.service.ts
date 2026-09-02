import { Injectable, OnDestroy, computed, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { EngineLine, EngineOption, EngineOptionType, EngineStatus } from './engine.models';

/**
 * Drives a Stockfish 18 build compiled to WebAssembly, running in a Web Worker on the
 * user's own machine.
 *
 * Why client-side: analysis is open-ended and CPU-bound, so one server cannot run it for
 * every visitor at once. The cost is that the engine binary (~7 MB) is downloaded on first
 * use, which is why it is only fetched when the user actually switches the engine on.
 *
 * Two builds ship: the multi-threaded one needs `SharedArrayBuffer`, which browsers only
 * expose to cross-origin-isolated documents (COOP + COEP headers - see angular.json). When
 * those headers are missing the single-threaded build is loaded instead. That build still
 * declares a `Threads` option, but as 1 to 1, so the panel hides the control and says why.
 *
 * Provided in root deliberately: the board is destroyed and recreated when the page enters
 * focus mode, and re-downloading and re-initialising the engine on every such switch would
 * be both slow and pointless. One engine instance outlives the components that display it.
 */
@Injectable({ providedIn: 'root' })
export class StockfishEngineService implements OnDestroy {
  private static readonly engineDirectory = 'engine/';
  private static readonly multiThreadedEngine = 'stockfish-18-lite.js';
  private static readonly singleThreadedEngine = 'stockfish-18-lite-single.js';
  private static readonly storageKey = 'chessxiv.engine-settings.v1';

  /**
   * Board navigation fires a FEN per keypress, and holding an arrow key would otherwise
   * start and abandon a search per frame. Coalescing them costs an unnoticeable delay.
   */
  private static readonly restartDebounceMs = 120;

  /** Plies of each variation kept. Beyond this the line is speculative and unreadable. */
  private static readonly pvPlyLimit = 24;

  /** Bounds on the number of displayed lines, as opposed to what the engine would allow. */
  static readonly minLines = 1;
  static readonly maxLines = 5;
  static readonly defaultLines = 3;

  /**
   * The engine reports `Hash` max as 32 TB, which is meaningful for a native binary and
   * absurd for one in a browser: the WASM heap tops out at 2 GB and the allocation simply
   * fails. Capped well under that so a slider drag cannot kill the worker.
   */
  private static readonly maxHashMb = 1024;

  /** Options the panel promotes to first-class controls; the rest render generically. */
  private static readonly primaryOptionNames = ['MultiPV', 'Threads', 'Hash'];

  /**
   * Options withheld from the panel because setting them kills this engine. Both name an
   * NNUE file to load from disk, and a WASM build has no filesystem to load it from - the
   * network is compiled into the .wasm instead. Sending either, even at its own declared
   * default, throws inside the worker and takes the engine down with it.
   */
  private static readonly unsupportedOptionNames = ['EvalFile', 'EvalFileSmall'];

  readonly isEnabled = signal(false);
  /**
   * The bar and the variation list are independent of each other, so the evaluation can be
   * followed at a glance without a block of text under the board, or the lines read without
   * the bar taking width from the board.
   */
  readonly isEvalBarVisible = signal(true);
  readonly areLinesVisible = signal(true);
  readonly status = signal<EngineStatus>('off');
  readonly errorMessage = signal<string | null>(null);
  readonly engineName = signal<string>('');

  readonly options = signal<EngineOption[]>([]);
  readonly optionValues = signal<Record<string, string>>({});

  readonly lines = signal<EngineLine[]>([]);
  readonly depth = signal(0);
  readonly nps = signal(0);
  /** The position the current numbers describe; null when there is nothing to analyse. */
  readonly analysedFen = signal<string | null>(null);
  /** Set when the board holds a position the engine cannot be asked about (illegal setup). */
  readonly positionUnsupported = signal(false);

  /** True when the browser gave us the headers the multi-threaded build needs. */
  readonly supportsMultipleThreads = typeof SharedArrayBuffer !== 'undefined' && self.crossOriginIsolated === true;

  /** The engine's own best line, which is what the bar and the headline score show. */
  readonly mainLine = computed<EngineLine | null>(() => this.lines()[0] ?? null);

  /**
   * White's share of the evaluation bar, 0-100.
   *
   * Centipawns are unbounded but the bar is not, so they are squashed through a logistic
   * curve: the difference between +1 and +2 is worth far more of the bar than the one
   * between +8 and +9, which is how a human reads an advantage too.
   */
  readonly evalBarWhitePercent = computed(() => {
    const line = this.mainLine();
    if (!line) {
      return 50;
    }

    if (line.mate !== null) {
      return line.mate > 0 ? 100 : 0;
    }

    const cp = line.cp ?? 0;
    return this.clamp(100 / (1 + Math.exp(-cp / 250)), 2, 98);
  });

  readonly advancedOptions = computed(() =>
    this.options().filter(option => !StockfishEngineService.primaryOptionNames.includes(option.name))
  );

  /**
   * What MultiPV is actually set to. With the list hidden only the evaluation is on show,
   * and that comes from the first line alone - asking for five is spent search effort that
   * nothing displays, so the extra lines are dropped until the list comes back.
   */
  readonly effectiveMultiPv = computed(() => (this.areLinesVisible() ? this.lineCount() : 1));

  readonly lineCount = computed(() => {
    const raw = Number(this.optionValues()['MultiPV']);
    return Number.isFinite(raw) ? this.clampLineCount(raw) : StockfishEngineService.defaultLines;
  });

  private worker: Worker | null = null;
  private fen: string | null = null;

  /** True between sending `go` and receiving `bestmove`; the engine is idle otherwise. */
  private isSearching = false;
  /** Set when something changed mid-search, so the `bestmove` handler restarts it. */
  private isRestartQueued = false;
  /** `setoption` is only legal while idle, so changes made mid-search wait here. */
  private pendingOptionCommands: string[] = [];
  private restartTimer: ReturnType<typeof setTimeout> | null = null;

  /** Side to move in the position being searched, used to normalise scores to White. */
  private searchSide: 'w' | 'b' = 'w';
  private searchFen: string | null = null;
  private linesByMultipv = new Map<number, EngineLine>();

  constructor() {
    this.restoreSettings();
  }

  ngOnDestroy(): void {
    this.terminate();
  }

  toggle(): void {
    if (this.isEnabled()) {
      this.disable();
      return;
    }

    this.enable();
  }

  enable(): void {
    if (this.isEnabled()) {
      return;
    }

    this.isEnabled.set(true);
    this.persistSettings();
    this.start();
  }

  disable(): void {
    if (!this.isEnabled()) {
      return;
    }

    this.isEnabled.set(false);
    this.persistSettings();
    this.terminate();
  }

  toggleEvalBar(): void {
    this.isEvalBarVisible.update(visible => !visible);
    this.persistSettings();
  }

  toggleLines(): void {
    this.areLinesVisible.update(visible => !visible);
    this.persistSettings();
    this.queueMultiPvCommand();
    this.scheduleRestart();
  }

  /** Points the engine at a new position. Safe to call for every board navigation. */
  setPosition(fen: string | null): void {
    if (fen === this.fen) {
      return;
    }

    this.fen = fen;
    this.positionUnsupported.set(false);
    this.clearResults();

    if (this.isEnabled()) {
      this.scheduleRestart();
    }
  }

  /** Number of variations shown, clamped to what the panel is willing to display. */
  setLineCount(value: number): void {
    this.setOption('MultiPV', String(this.clampLineCount(value)));
  }

  setOption(name: string, value: string): void {
    const option = this.options().find(entry => entry.name === name);
    const normalised = option ? this.normaliseOptionValue(option, value) : value;

    if (this.optionValues()[name] === normalised) {
      return;
    }

    this.optionValues.update(values => ({ ...values, [name]: normalised }));
    this.persistSettings();

    if (name === 'MultiPV') {
      // The stored value is the user's preference; what the engine is told depends on
      // whether the list is on screen at all.
      this.queueMultiPvCommand();
    } else {
      this.pendingOptionCommands.push(`setoption name ${name} value ${normalised}`);
    }

    this.scheduleRestart();
  }

  private queueMultiPvCommand(): void {
    this.pendingOptionCommands.push(`setoption name MultiPV value ${this.effectiveMultiPv()}`);
  }

  /** Fires a `button`-typed option, such as Clear Hash. Buttons carry no value. */
  triggerOption(name: string): void {
    this.pendingOptionCommands.push(`setoption name ${name}`);
    this.scheduleRestart();
  }

  /** Drops every user override, returning the engine to its own declared defaults. */
  resetOptions(): void {
    for (const option of this.options()) {
      if (option.type === 'button') {
        continue;
      }

      this.setOption(option.name, this.appDefaultFor(option));
    }
  }

  /**
   * Effective bounds for a spin option, narrowed where the engine's own limits are not
   * usable in a browser (see `maxHashMb`, and threads beyond the machine's core count).
   */
  boundsFor(option: EngineOption): { min: number; max: number } {
    const min = option.min ?? 0;
    let max = option.max ?? 100;

    if (option.name === 'Hash') {
      max = Math.min(max, StockfishEngineService.maxHashMb);
    }

    if (option.name === 'Threads') {
      max = Math.min(max, this.hardwareThreads());
    }

    return { min, max: Math.max(min, max) };
  }

  valueOf(name: string): string {
    return this.optionValues()[name] ?? '';
  }

  private start(): void {
    if (this.worker) {
      return;
    }

    this.status.set('loading');
    this.errorMessage.set(null);

    const file = this.supportsMultipleThreads
      ? StockfishEngineService.multiThreadedEngine
      : StockfishEngineService.singleThreadedEngine;

    try {
      // Resolved against the document base rather than bundled: the engine and its .wasm
      // are copied in as build assets (angular.json), not compiled into the app.
      const url = new URL(`${StockfishEngineService.engineDirectory}${file}`, document.baseURI).href;
      this.worker = new Worker(url);
    } catch (error) {
      this.failed('Could not start the engine worker.', error);
      return;
    }

    this.worker.onmessage = event => this.handleEngineMessage(event.data);
    this.worker.onerror = () => this.failed('The engine failed to load.');

    this.send('uci');
  }

  private terminate(): void {
    if (this.restartTimer !== null) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }

    this.worker?.terminate();
    this.worker = null;
    this.isSearching = false;
    this.isRestartQueued = false;
    this.pendingOptionCommands = [];
    this.status.set('off');
    this.errorMessage.set(null);
    this.clearResults();
  }

  private failed(message: string, error?: unknown): void {
    if (error) {
      console.error(message, error);
    }

    this.worker?.terminate();
    this.worker = null;
    this.isSearching = false;
    this.status.set('error');
    this.errorMessage.set(message);
  }

  private send(command: string): void {
    this.worker?.postMessage(command);
  }

  private handleEngineMessage(data: unknown): void {
    if (typeof data !== 'string') {
      return;
    }

    if (data.startsWith('id name ')) {
      this.engineName.set(data.slice('id name '.length).trim());
      return;
    }

    if (data.startsWith('option name ')) {
      this.registerOption(data);
      return;
    }

    if (data === 'uciok') {
      this.onUciOk();
      return;
    }

    if (data === 'readyok') {
      this.status.set('ready');
      this.startSearch();
      return;
    }

    if (data.startsWith('info ')) {
      this.handleInfo(data);
      return;
    }

    if (data.startsWith('bestmove')) {
      this.isSearching = false;
      if (this.isRestartQueued) {
        this.startSearch();
      }
    }
  }

  /** Parses `option name <name> type <type> [default X] [min N] [max N] [var A var B]`. */
  private registerOption(line: string): void {
    const match = /^option name (.+?) type (check|spin|combo|button|string)(.*)$/.exec(line);
    if (!match) {
      return;
    }

    const [, name, type, rest] = match;
    if (StockfishEngineService.unsupportedOptionNames.includes(name)) {
      return;
    }

    const option: EngineOption = {
      name,
      type: type as EngineOptionType,
      defaultValue: /(?:^|\s)default\s+(.*?)(?=\s+(?:min|max|var)\s|$)/.exec(rest)?.[1]?.trim() ?? ''
    };

    const min = /(?:^|\s)min\s+(-?\d+)/.exec(rest)?.[1];
    const max = /(?:^|\s)max\s+(-?\d+)/.exec(rest)?.[1];
    if (min !== undefined) {
      option.min = Number(min);
    }
    if (max !== undefined) {
      option.max = Number(max);
    }

    const choices = [...rest.matchAll(/(?:^|\s)var\s+(.*?)(?=\s+var\s|$)/g)].map(entry => entry[1].trim());
    if (choices.length > 0) {
      option.choices = choices;
    }

    this.options.update(options => [...options.filter(entry => entry.name !== name), option]);
  }

  private onUciOk(): void {
    // Values restored from a previous session are kept; anything the engine declared that
    // we have no stored value for falls back to this app's default, then the engine's own.
    const values: Record<string, string> = {};
    for (const option of this.options()) {
      if (option.type === 'button') {
        continue;
      }

      const stored = this.optionValues()[option.name];
      values[option.name] = stored !== undefined
        ? this.normaliseOptionValue(option, stored)
        : this.appDefaultFor(option);
    }

    this.optionValues.set(values);
    this.persistSettings();

    for (const [name, value] of Object.entries(values)) {
      const option = this.options().find(entry => entry.name === name);
      const outgoing = name === 'MultiPV' ? String(this.effectiveMultiPv()) : value;
      if (option && outgoing === option.defaultValue) {
        continue;
      }

      this.send(`setoption name ${name} value ${outgoing}`);
    }

    this.send('ucinewgame');
    this.send('isready');
  }

  /**
   * App-level defaults, chosen for a browser rather than for a tournament binary: three
   * lines because that is what an opening-analysis view wants, and threads and hash scaled
   * to the machine instead of Stockfish's conservative single-thread / 16 MB defaults.
   */
  private appDefaultFor(option: EngineOption): string {
    if (option.name === 'MultiPV') {
      return String(StockfishEngineService.defaultLines);
    }

    if (option.name === 'Threads') {
      const half = Math.floor(this.hardwareThreads() / 2);
      return String(this.clamp(Math.max(1, half), option.min ?? 1, this.boundsFor(option).max));
    }

    if (option.name === 'Hash') {
      return String(this.clamp(128, option.min ?? 1, this.boundsFor(option).max));
    }

    return option.defaultValue;
  }

  private normaliseOptionValue(option: EngineOption, value: string): string {
    if (option.type === 'spin') {
      const bounds = option.name === 'MultiPV'
        ? { min: StockfishEngineService.minLines, max: StockfishEngineService.maxLines }
        : this.boundsFor(option);
      const numeric = Number(value);
      const fallback = Number(option.defaultValue);
      const resolved = Number.isFinite(numeric) ? numeric : (Number.isFinite(fallback) ? fallback : bounds.min);
      return String(this.clamp(Math.round(resolved), bounds.min, bounds.max));
    }

    if (option.type === 'check') {
      return value === 'true' ? 'true' : 'false';
    }

    if (option.type === 'combo' && option.choices && !option.choices.includes(value)) {
      return option.defaultValue;
    }

    return value;
  }

  /**
   * Restarting is deferred twice over: once to coalesce rapid board navigation, and once
   * more when a search is already running, since `position` and `setoption` are only legal
   * while the engine is idle. The `bestmove` that answers our `stop` resumes the sequence.
   */
  private scheduleRestart(): void {
    if (!this.worker || this.status() === 'loading') {
      return;
    }

    if (this.restartTimer !== null) {
      clearTimeout(this.restartTimer);
    }

    this.restartTimer = setTimeout(() => {
      this.restartTimer = null;

      if (this.isSearching) {
        this.isRestartQueued = true;
        this.send('stop');
        return;
      }

      this.startSearch();
    }, StockfishEngineService.restartDebounceMs);
  }

  private startSearch(): void {
    this.isRestartQueued = false;

    if (!this.worker || this.status() !== 'ready') {
      return;
    }

    // The engine is idle here, which is the only point where option changes may be sent.
    for (const command of this.pendingOptionCommands) {
      this.send(command);
    }
    this.pendingOptionCommands = [];

    const fen = this.fen;
    if (!this.isEnabled() || !fen) {
      return;
    }

    // A position built in Set Position mode can be illegal (no king, say). Stockfish's
    // behaviour on those is undefined, so they are refused here rather than risking a
    // worker that stops responding.
    let position: Chess;
    try {
      position = new Chess(fen);
    } catch {
      this.positionUnsupported.set(true);
      this.clearResults();
      return;
    }

    this.positionUnsupported.set(false);
    this.searchFen = fen;
    this.searchSide = position.turn();
    this.linesByMultipv.clear();
    this.clearResults();
    this.analysedFen.set(fen);

    this.send(`position fen ${fen}`);
    this.send('go infinite');
    this.isSearching = true;
  }

  private handleInfo(line: string): void {
    // Bounded scores are provisional values from an aborted window search; showing them
    // makes the evaluation jump around between real updates.
    if (line.includes(' lowerbound ') || line.includes(' upperbound ')) {
      return;
    }

    const tokens = line.split(/\s+/);
    let depth: number | null = null;
    let multipv = 1;
    let cp: number | null = null;
    let mate: number | null = null;
    let pvUci: string[] | null = null;

    for (let i = 1; i < tokens.length; i++) {
      switch (tokens[i]) {
        case 'depth':
          depth = Number(tokens[++i]);
          break;
        case 'multipv':
          multipv = Number(tokens[++i]);
          break;
        case 'nps':
          this.nps.set(Number(tokens[++i]));
          break;
        case 'score':
          if (tokens[i + 1] === 'cp') {
            cp = Number(tokens[i + 2]);
          } else if (tokens[i + 1] === 'mate') {
            mate = Number(tokens[i + 2]);
          }
          i += 2;
          break;
        case 'pv':
          pvUci = tokens.slice(i + 1, i + 1 + StockfishEngineService.pvPlyLimit);
          i = tokens.length;
          break;
      }
    }

    if (depth === null || pvUci === null || pvUci.length === 0 || (cp === null && mate === null)) {
      return;
    }

    this.depth.set(depth);

    // Scores arrive relative to the side to move; flip them so a positive number always
    // means White is better, which is what every chess UI shows.
    const perspective = this.searchSide === 'w' ? 1 : -1;

    this.linesByMultipv.set(multipv, {
      multipv,
      depth,
      cp: cp === null ? null : cp * perspective,
      mate: mate === null ? null : mate * perspective,
      pvUci,
      pvSan: this.toSan(pvUci)
    });

    this.lines.set([...this.linesByMultipv.values()].sort((a, b) => a.multipv - b.multipv));
  }

  private toSan(pvUci: string[]): string[] {
    if (!this.searchFen) {
      return [];
    }

    let position: Chess;
    try {
      position = new Chess(this.searchFen);
    } catch {
      return [];
    }

    const san: string[] = [];
    for (const uci of pvUci) {
      try {
        const move = position.move({
          from: uci.slice(0, 2),
          to: uci.slice(2, 4),
          promotion: uci.length > 4 ? uci[4] : undefined
        });
        san.push(move.san);
      } catch {
        // A variation we cannot replay is truncated rather than dropped: the moves up to
        // the failure are still the engine's answer.
        break;
      }
    }

    return san;
  }

  private clearResults(): void {
    this.lines.set([]);
    this.depth.set(0);
    this.nps.set(0);
    this.analysedFen.set(null);
    this.linesByMultipv.clear();
  }

  private hardwareThreads(): number {
    const cores = navigator.hardwareConcurrency;
    return Number.isFinite(cores) && cores > 0 ? cores : 1;
  }

  private clampLineCount(value: number): number {
    return this.clamp(Math.round(value), StockfishEngineService.minLines, StockfishEngineService.maxLines);
  }

  private clamp(value: number, min: number, max: number): number {
    return Math.min(Math.max(value, min), max);
  }

  private restoreSettings(): void {
    try {
      const raw = localStorage.getItem(StockfishEngineService.storageKey);
      if (!raw) {
        return;
      }

      const parsed = JSON.parse(raw) as {
        enabled?: unknown;
        evalBar?: unknown;
        lines?: unknown;
        options?: unknown;
      };
      if (parsed.options && typeof parsed.options === 'object') {
        const values: Record<string, string> = {};
        for (const [name, value] of Object.entries(parsed.options as Record<string, unknown>)) {
          if (typeof value === 'string') {
            values[name] = value;
          }
        }
        this.optionValues.set(values);
      }

      // The stored `enabled` flag only pre-selects the switch. The engine is started by
      // the panel once it is on screen, so a stale flag can never download 7 MB in the
      // background of a page the user never scrolled to.
      this.isEnabled.set(parsed.enabled === true);

      // Both default to on, so only an explicit `false` hides either.
      this.isEvalBarVisible.set(parsed.evalBar !== false);
      this.areLinesVisible.set(parsed.lines !== false);
    } catch {
      // A corrupt or unavailable store is not worth surfacing; defaults apply.
    }
  }

  private persistSettings(): void {
    try {
      localStorage.setItem(
        StockfishEngineService.storageKey,
        JSON.stringify({
          enabled: this.isEnabled(),
          evalBar: this.isEvalBarVisible(),
          lines: this.areLinesVisible(),
          options: this.optionValues()
        })
      );
    } catch {
      // Private-mode storage failures must not break analysis.
    }
  }

  /** Starts the worker if the user left the engine switched on in a previous session. */
  resumeIfPreviouslyEnabled(): void {
    if (this.isEnabled() && !this.worker) {
      this.start();
    }
  }
}
