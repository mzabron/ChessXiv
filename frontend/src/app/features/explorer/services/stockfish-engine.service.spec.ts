import { TestBed } from '@angular/core/testing';
import { StockfishEngineService } from './stockfish-engine.service';

/**
 * The engine is driven over the UCI text protocol, so it can be exercised end to end by
 * replaying what a real Stockfish 18 build emits. Every fixture below is copied verbatim
 * from `node stockfish/scripts/cli.js`, which is what keeps these tests honest about the
 * format rather than about my reading of it.
 */
const UCI_OPTION_LINES = [
  'id name Stockfish 18 WASM Multithreaded',
  'option name Threads type spin default 1 min 1 max 32',
  'option name Hash type spin default 16 min 1 max 33554432',
  'option name Clear Hash type button',
  'option name Ponder type check default false',
  'option name MultiPV type spin default 1 min 1 max 256',
  'option name Skill Level type spin default 20 min 0 max 20',
  'option name Move Overhead type spin default 10 min 0 max 5000',
  'option name UCI_LimitStrength type check default false',
  'option name UCI_ShowWDL type check default false',
  'option name UCI_Elo type spin default 1320 min 1320 max 3190',
  'option name EvalFile type string default nn-c288c895ea92.nnue'
];

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
/** After 1. e4 e5 - White to move, so scores arrive already in White's perspective. */
const AFTER_E4_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3';
/** After 1. e4 - Black to move, so the engine's scores need flipping for display. */
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

class FakeWorker {
  static instances: FakeWorker[] = [];

  readonly sent: string[] = [];
  terminated = false;
  onmessage: ((event: { data: unknown }) => void) | null = null;
  onerror: ((event: unknown) => void) | null = null;

  constructor(readonly url: string) {
    FakeWorker.instances.push(this);
  }

  postMessage(command: string): void {
    this.sent.push(command);
  }

  terminate(): void {
    this.terminated = true;
  }

  emit(...lines: string[]): void {
    for (const line of lines) {
      this.onmessage?.({ data: line });
    }
  }
}

describe('StockfishEngineService', () => {
  let service: StockfishEngineService;
  let originalWorker: unknown;

  /** Brings the engine to the point where it is searching a position. */
  function bootEngine(fen: string): FakeWorker {
    service.setPosition(fen);
    service.enable();

    const worker = FakeWorker.instances.at(-1)!;
    worker.emit(...UCI_OPTION_LINES, 'uciok');
    worker.emit('readyok');
    return worker;
  }

  beforeEach(() => {
    localStorage.clear();
    FakeWorker.instances = [];
    originalWorker = (globalThis as Record<string, unknown>)['Worker'];
    (globalThis as Record<string, unknown>)['Worker'] = FakeWorker;

    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    service = TestBed.inject(StockfishEngineService);
  });

  afterEach(() => {
    vi.useRealTimers();
    (globalThis as Record<string, unknown>)['Worker'] = originalWorker;
  });

  it('parses the engine\'s declared options, including bounds and types', () => {
    bootEngine(START_FEN);

    const threads = service.options().find(option => option.name === 'Threads');
    expect(threads).toEqual({ name: 'Threads', type: 'spin', defaultValue: '1', min: 1, max: 32 });

    expect(service.options().find(option => option.name === 'Clear Hash')?.type).toBe('button');
  });

  it('hides options that cannot be observed in an analysis panel', () => {
    bootEngine(START_FEN);
    const names = service.options().map(option => option.name);

    // Ponder needs a GUI that plays games; Skill Level is dead weight next to UCI_Elo, which
    // overrides it outright the moment UCI_LimitStrength is on.
    expect(names).not.toContain('Ponder');
    expect(names).not.toContain('Skill Level');
    expect(names).not.toContain('UCI_ShowWDL');
    expect(names).toContain('UCI_Elo');
  });

  it('withholds the NNUE file options, which crash a WASM build outright', () => {
    const worker = bootEngine(START_FEN);

    // Sending either - even at the engine's own default - throws inside the worker, since
    // there is no filesystem to load a network from. They must not reach the panel either.
    expect(service.options().map(option => option.name)).not.toContain('EvalFile');
    expect(worker.sent.some(command => command.includes('EvalFile'))).toBe(false);
  });

  it('sends only the options it actually overrides', () => {
    const worker = bootEngine(START_FEN);
    const sent = worker.sent.filter(command => command.startsWith('setoption'));

    // MultiPV is overridden (3, against a default of 1); Skill Level is left alone.
    expect(sent).toContain('setoption name MultiPV value 3');
    expect(sent.some(command => command.includes('UCI_Elo'))).toBe(false);
  });

  it('separates the options that act from the options that hold a value', () => {
    bootEngine(START_FEN);

    // Clear Hash does something once; the rest hold a value. The panel puts them in
    // different places, so the service is what tells them apart.
    expect(service.actionOptions().map(option => option.name)).toEqual(['Clear Hash']);
    expect(service.otherOptions().map(option => option.name)).toEqual(['UCI_LimitStrength', 'UCI_Elo']);
  });

  it('starts a search on the requested position once the engine reports ready', () => {
    const worker = bootEngine(AFTER_E4_E5);

    expect(service.status()).toBe('ready');
    expect(worker.sent).toContain(`position fen ${AFTER_E4_E5}`);
    expect(worker.sent).toContain('go infinite');
  });

  it('keeps three lines by default and reports each variation in SAN', () => {
    const worker = bootEngine(AFTER_E4_E5);

    expect(service.lineCount()).toBe(3);
    expect(worker.sent).toContain('setoption name MultiPV value 3');

    worker.emit(
      'info depth 10 seldepth 20 multipv 1 score cp 54 nodes 54455 nps 864365 hashfull 15 time 63 pv g1f3 b8c6 f1b5',
      'info depth 10 seldepth 17 multipv 2 score cp 16 nodes 54455 nps 864365 hashfull 15 time 63 pv b1c3 b8c6 g1f3',
      'info depth 10 seldepth 18 multipv 3 score cp 9 nodes 54455 nps 864365 hashfull 15 time 63 pv d2d4 e5d4 g1f3'
    );

    const lines = service.lines();
    expect(lines.map(line => line.multipv)).toEqual([1, 2, 3]);
    expect(lines[0].pvSan).toEqual(['Nf3', 'Nc6', 'Bb5']);
    expect(lines[2].pvSan).toEqual(['d4', 'exd4', 'Nf3']);
    expect(service.depth()).toBe(10);
    expect(service.nps()).toBe(864365);
  });

  it('reports scores from White\'s perspective whichever side is to move', () => {
    const white = bootEngine(AFTER_E4_E5);
    white.emit('info depth 12 multipv 1 score cp 45 nodes 1 nps 1 pv g1f3');
    expect(service.lines()[0].cp).toBe(45);

    service.disable();

    // Same +cp from the engine, but with Black to move it means Black is better.
    const black = bootEngine(AFTER_E4);
    black.emit('info depth 12 multipv 1 score cp 45 nodes 1 nps 1 pv e7e5');
    expect(service.lines()[0].cp).toBe(-45);

    black.emit('info depth 20 multipv 1 score mate 3 nodes 1 nps 1 pv e7e5');
    expect(service.lines()[0].mate).toBe(-3);
    expect(service.lines()[0].cp).toBeNull();
  });

  it('ignores bounded scores, which are provisional rather than evaluations', () => {
    const worker = bootEngine(AFTER_E4_E5);

    worker.emit('info depth 12 multipv 1 score cp 45 nodes 1 nps 1 pv g1f3');
    worker.emit('info depth 13 multipv 1 score cp 900 lowerbound nodes 2 nps 1 pv g1f3');

    expect(service.lines()[0].cp).toBe(45);
    expect(service.depth()).toBe(12);
  });

  it('stops the running search before switching position, and restarts only once idle', () => {
    const worker = bootEngine(AFTER_E4_E5);
    worker.sent.length = 0;

    service.setPosition(AFTER_E4);
    vi.advanceTimersByTime(200);

    // The engine is mid-search, so it may only be told to stop: `position` would be illegal.
    expect(worker.sent).toEqual(['stop']);

    worker.emit('bestmove g1f3 ponder b8c6');
    expect(worker.sent).toEqual(['stop', `position fen ${AFTER_E4}`, 'go infinite']);
  });

  it('coalesces rapid navigation into a single search', () => {
    const worker = bootEngine(AFTER_E4_E5);
    worker.emit('bestmove g1f3');
    worker.sent.length = 0;

    service.setPosition(AFTER_E4);
    service.setPosition(START_FEN);
    service.setPosition(AFTER_E4_E5);
    vi.advanceTimersByTime(200);

    expect(worker.sent.filter(command => command === 'go infinite')).toHaveLength(1);
    expect(worker.sent).toContain(`position fen ${AFTER_E4_E5}`);
  });

  it('clamps the line count to what the panel displays, not what the engine allows', () => {
    bootEngine(AFTER_E4_E5);

    service.setLineCount(9);
    expect(service.lineCount()).toBe(5);

    service.setLineCount(0);
    expect(service.lineCount()).toBe(1);
  });

  it('caps hash and threads at values a browser can actually honour', () => {
    bootEngine(AFTER_E4_E5);

    const hash = service.options().find(option => option.name === 'Hash')!;
    // Stockfish advertises 32 TB; the WASM heap tops out long before that.
    expect(service.boundsFor(hash).max).toBe(1024);

    service.setOption('Hash', '99999');
    expect(service.valueOf('Hash')).toBe('1024');

    const threads = service.options().find(option => option.name === 'Threads')!;
    expect(service.boundsFor(threads).max).toBeLessThanOrEqual(navigator.hardwareConcurrency || 1);
  });

  it('refuses positions that are not legal chess rather than hanging the worker', () => {
    const worker = bootEngine(AFTER_E4_E5);
    worker.emit('bestmove g1f3');
    worker.sent.length = 0;

    service.setPosition('8/8/8/8/8/8/8/8 w - - 0 1');
    vi.advanceTimersByTime(200);

    expect(service.positionUnsupported()).toBe(true);
    expect(worker.sent).not.toContain('go infinite');
  });

  it('remembers the switch and the option values across a reload', () => {
    bootEngine(AFTER_E4_E5);
    service.setOption('Threads', '3');
    service.setLineCount(4);

    // A fresh service, as after a page reload, restores what was stored.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const reloaded = TestBed.inject(StockfishEngineService);

    expect(reloaded.isEnabled()).toBe(true);
    expect(reloaded.valueOf('Threads')).toBe('3');
    expect(reloaded.valueOf('MultiPV')).toBe('4');
  });

  it('drops to a single line while the list is hidden, and restores it after', () => {
    const worker = bootEngine(AFTER_E4_E5);
    worker.emit('bestmove g1f3');
    worker.sent.length = 0;

    // Nothing displays lines 2 to 5 with the list hidden, so searching for them is waste.
    service.toggleLines();
    vi.advanceTimersByTime(200);
    expect(worker.sent).toContain('setoption name MultiPV value 1');
    // The user's own preference is untouched, only what the engine was told.
    expect(service.lineCount()).toBe(3);

    worker.emit('bestmove g1f3');
    worker.sent.length = 0;
    service.toggleLines();
    vi.advanceTimersByTime(200);
    expect(worker.sent).toContain('setoption name MultiPV value 3');
  });

  it('maps the evaluation onto the bar, with mate pinning it to one end', () => {
    const worker = bootEngine(AFTER_E4_E5);
    expect(service.evalBarWhitePercent()).toBe(50);

    worker.emit('info depth 12 multipv 1 score cp 0 nodes 1 nps 1 pv g1f3');
    expect(service.evalBarWhitePercent()).toBe(50);

    worker.emit('info depth 12 multipv 1 score cp 300 nodes 1 nps 1 pv g1f3');
    const white = service.evalBarWhitePercent();
    expect(white).toBeGreaterThan(50);
    expect(white).toBeLessThan(100);

    worker.emit('info depth 12 multipv 1 score mate 2 nodes 1 nps 1 pv g1f3');
    expect(service.evalBarWhitePercent()).toBe(100);

    worker.emit('info depth 12 multipv 1 score mate -2 nodes 1 nps 1 pv g1f3');
    expect(service.evalBarWhitePercent()).toBe(0);
  });

  it('remembers the bar and list toggles independently of each other', () => {
    bootEngine(AFTER_E4_E5);
    service.toggleLines();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const reloaded = TestBed.inject(StockfishEngineService);

    expect(reloaded.areLinesVisible()).toBe(false);
    expect(reloaded.isEvalBarVisible()).toBe(true);
  });

  it('shuts the worker down when switched off', () => {
    const worker = bootEngine(AFTER_E4_E5);

    service.disable();

    expect(worker.terminated).toBe(true);
    expect(service.status()).toBe('off');
    expect(service.lines()).toEqual([]);
  });
});
