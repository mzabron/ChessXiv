import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, Output, SimpleChanges, ViewChild, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ExplorerBoardApiService } from '../../services/explorer-board-api.service';
import { MoveRow } from '../move-list/move-list.component';

interface ChessPiece {
  id: string;
  type: string;
  x: number;
  y: number;
}

interface SetupSnapshot {
  startFen: string;
  currentFen: string;
  fenHistory: string[];
  sanHistory: string[];
  currentPly: number;
  pieces: ChessPiece[];
}

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

const PIECE_URLS: Record<string, string> = {
  'wk': 'https://upload.wikimedia.org/wikipedia/commons/4/42/Chess_klt45.svg',
  'wq': 'https://upload.wikimedia.org/wikipedia/commons/1/15/Chess_qlt45.svg',
  'wr': 'https://upload.wikimedia.org/wikipedia/commons/7/72/Chess_rlt45.svg',
  'wb': 'https://upload.wikimedia.org/wikipedia/commons/b/b1/Chess_blt45.svg',
  'wn': 'https://upload.wikimedia.org/wikipedia/commons/7/70/Chess_nlt45.svg',
  'wp': 'https://upload.wikimedia.org/wikipedia/commons/4/45/Chess_plt45.svg',
  'bk': 'https://upload.wikimedia.org/wikipedia/commons/f/f0/Chess_kdt45.svg',
  'bq': 'https://upload.wikimedia.org/wikipedia/commons/4/47/Chess_qdt45.svg',
  'br': 'https://upload.wikimedia.org/wikipedia/commons/f/ff/Chess_rdt45.svg',
  'bb': 'https://upload.wikimedia.org/wikipedia/commons/9/98/Chess_bdt45.svg',
  'bn': 'https://upload.wikimedia.org/wikipedia/commons/e/ef/Chess_ndt45.svg',
  'bp': 'https://upload.wikimedia.org/wikipedia/commons/c/c7/Chess_pdt45.svg',
};

@Component({
  selector: 'app-chessboard',
  standalone: true,
  templateUrl: './chessboard.component.html',
  styleUrl: './chessboard.component.scss'
})
export class ChessboardComponent implements OnChanges {
  private readonly boardApi = inject(ExplorerBoardApiService);

  @ViewChild('boardGrid', { static: true })
  private readonly boardGridRef!: ElementRef<HTMLElement>;

  protected readonly ranks = [8, 7, 6, 5, 4, 3, 2, 1];
  protected readonly files = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];
  protected readonly whiteSetupPieces = ['wk', 'wq', 'wr', 'wb', 'wn', 'wp'];
  protected readonly blackSetupPieces = ['bk', 'bq', 'br', 'bb', 'bn', 'bp'];

  @Input() navigationRequest: { ply: number; version: number } | null = null;

  @Output() readonly moveRowsChanged = new EventEmitter<MoveRow[]>();
  @Output() readonly currentPlyChanged = new EventEmitter<number>();

  pieces: ChessPiece[] = [];
  protected selectedSquare: string | null = null;
  protected statusMessage: string | null = null;
  protected isSubmittingMove = false;
  protected isSetupMode = false;
  protected setupTool: 'hand' | 'delete' | 'place' = 'hand';
  protected setupSelectedPieceType: string | null = null;
  protected setupCastling = {
    whiteKingSide: true,
    whiteQueenSide: true,
    blackKingSide: true,
    blackQueenSide: true
  };
  protected setupEnPassant = '-';
  protected setupSideToMove: 'w' | 'b' = 'w';

  private startFen = START_FEN;
  private currentFen = START_FEN;
  private fenHistory: string[] = [START_FEN];
  private sanHistory: string[] = [];
  private currentPly = 0;
  private isFlipped = false;
  private setupSnapshot: SetupSnapshot | null = null;
  private activeDrag: {
    pieceId: string;
    sourceSquare: string;
    startX: number;
    startY: number;
    deltaX: number;
    deltaY: number;
    moved: boolean;
  } | null = null;
  private suppressNextSquareClick = false;

  constructor() {
    this.pieces = this.parseFenToPieces(this.currentFen);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('navigationRequest' in changes && this.navigationRequest) {
      this.navigateToPly(this.navigationRequest.ply);
    }
  }

  getPieceUrl(type: string): string {
    return PIECE_URLS[type];
  }

  getPieceTransform(piece: ChessPiece): string {
    const displayX = this.isFlipped ? 7 - piece.x : piece.x;
    const displayY = this.isFlipped ? 7 - piece.y : piece.y;
    if (this.activeDrag?.pieceId === piece.id) {
      return `translate(calc(${displayX * 100}% + ${this.activeDrag.deltaX}px), calc(${displayY * 100}% + ${this.activeDrag.deltaY}px))`;
    }

    return `translate(${displayX * 100}%, ${displayY * 100}%)`;
  }

  protected isPieceDragging(piece: ChessPiece): boolean {
    return this.activeDrag?.pieceId === piece.id;
  }

  protected flipBoard(): void {
    this.isFlipped = !this.isFlipped;
  }

  protected onSquareClick(fileIndex: number, rankIndex: number): void {
    if (this.suppressNextSquareClick) {
      this.suppressNextSquareClick = false;
      return;
    }

    const square = this.displayCoordsToSquare(fileIndex, rankIndex);

    if (this.isSetupMode) {
      this.handleSetupSquareClick(square);
      return;
    }

    this.handleSquareInteraction(square);
  }

  protected onPiecePointerDown(piece: ChessPiece, event: PointerEvent): void {
    event.stopPropagation();

    if (this.isSetupMode) {
      const setupSource = this.coordsToSquare(piece.x, piece.y);

      if (this.setupTool === 'delete') {
        this.removePieceAtSquare(setupSource);
        return;
      }

      if (this.setupTool !== 'hand') {
        return;
      }

      this.activeDrag = {
        pieceId: piece.id,
        sourceSquare: setupSource,
        startX: event.clientX,
        startY: event.clientY,
        deltaX: 0,
        deltaY: 0,
        moved: false
      };
      return;
    }

    const sourceSquare = this.coordsToSquare(piece.x, piece.y);
    if (!this.canSelectSquare(sourceSquare) || this.isSubmittingMove) {
      return;
    }

    this.activeDrag = {
      pieceId: piece.id,
      sourceSquare,
      startX: event.clientX,
      startY: event.clientY,
      deltaX: 0,
      deltaY: 0,
      moved: false
    };
  }

  @HostListener('window:pointermove', ['$event'])
  protected onWindowPointerMove(event: PointerEvent): void {
    if (!this.activeDrag) {
      return;
    }

    this.activeDrag.deltaX = event.clientX - this.activeDrag.startX;
    this.activeDrag.deltaY = event.clientY - this.activeDrag.startY;

    if (!this.activeDrag.moved) {
      const dragDistance = Math.abs(this.activeDrag.deltaX) + Math.abs(this.activeDrag.deltaY);
      this.activeDrag.moved = dragDistance > 4;
    }
  }

  @HostListener('window:pointerup', ['$event'])
  protected onWindowPointerUp(event: PointerEvent): void {
    if (!this.activeDrag) {
      return;
    }

    const drag = this.activeDrag;
    this.activeDrag = null;

    if (!drag.moved) {
      if (this.isSetupMode) {
        return;
      }

      this.handleSquareInteraction(drag.sourceSquare);
      return;
    }

    this.suppressNextSquareClick = true;
    const targetSquare = this.getSquareFromClientPoint(event.clientX, event.clientY);
    if (!targetSquare || targetSquare === drag.sourceSquare || this.isSubmittingMove) {
      return;
    }

    if (this.isSetupMode) {
      this.movePiece(drag.sourceSquare, targetSquare);
      return;
    }

    this.selectedSquare = null;
    void this.tryApplyMove(drag.sourceSquare, targetSquare);
  }

  @HostListener('window:pointercancel')
  protected onWindowPointerCancel(): void {
    this.activeDrag = null;
  }

  protected isSelectedDisplaySquare(fileIndex: number, rankIndex: number): boolean {
    if (!this.selectedSquare) {
      return false;
    }

    return this.displayCoordsToSquare(fileIndex, rankIndex) === this.selectedSquare;
  }

  protected getDisplayedFileLabel(fileIndex: number): string {
    return this.files[this.isFlipped ? 7 - fileIndex : fileIndex] ?? '';
  }

  protected getDisplayedRankLabel(rankIndex: number): number {
    return this.isFlipped ? rankIndex + 1 : 8 - rankIndex;
  }

  protected goPreviousMove(): void {
    if (this.isSetupMode) {
      return;
    }

    this.navigateToPly(this.currentPly - 1);
  }

  protected goNextMove(): void {
    if (this.isSetupMode) {
      return;
    }

    this.navigateToPly(this.currentPly + 1);
  }

  protected goToGameStart(): void {
    if (this.isSetupMode) {
      return;
    }

    this.navigateToPly(0);
  }

  protected goToGameEnd(): void {
    if (this.isSetupMode) {
      return;
    }

    this.navigateToPly(this.sanHistory.length);
  }

  protected setPosition(): void {
    this.startSetupMode();
  }

  protected clearPosition(): void {
    if (this.isSetupMode) {
      this.pieces = [];
      this.normalizeSetupMetadata();
      return;
    }

    this.resetGame();
  }

  protected startSetupMode(): void {
    if (this.isSetupMode) {
      return;
    }

    this.setupSnapshot = {
      startFen: this.startFen,
      currentFen: this.currentFen,
      fenHistory: [...this.fenHistory],
      sanHistory: [...this.sanHistory],
      currentPly: this.currentPly,
      pieces: this.pieces.map(piece => ({ ...piece }))
    };

    this.isSetupMode = true;
    this.selectedSquare = null;
    this.statusMessage = null;
    this.setupTool = 'hand';
    this.setupSelectedPieceType = null;
    this.activeDrag = null;
    this.initializeSetupMetadataFromFen(this.currentFen);
  }

  protected cancelSetupMode(): void {
    if (!this.isSetupMode || !this.setupSnapshot) {
      return;
    }

    this.startFen = this.setupSnapshot.startFen;
    this.currentFen = this.setupSnapshot.currentFen;
    this.fenHistory = [...this.setupSnapshot.fenHistory];
    this.sanHistory = [...this.setupSnapshot.sanHistory];
    this.currentPly = this.setupSnapshot.currentPly;
    this.pieces = this.setupSnapshot.pieces.map(piece => ({ ...piece }));

    this.exitSetupMode();
    this.emitNavigationState();
  }

  protected saveSetupMode(): void {
    if (!this.isSetupMode) {
      return;
    }

    const builtFen = this.buildFenFromSetup();
    this.startFen = builtFen;
    this.currentFen = builtFen;
    this.fenHistory = [builtFen];
    this.sanHistory = [];
    this.currentPly = 0;
    this.pieces = this.parseFenToPieces(this.currentFen);

    this.exitSetupMode();
    this.statusMessage = null;
    this.emitMoveRows();
    this.emitNavigationState();
  }

  protected selectSetupPiece(type: string): void {
    this.setupTool = 'place';
    this.setupSelectedPieceType = type;
  }

  protected selectSetupTool(tool: 'hand' | 'delete'): void {
    this.setupTool = tool;
    this.setupSelectedPieceType = null;
  }

  protected isSetupPieceSelected(type: string): boolean {
    return this.setupTool === 'place' && this.setupSelectedPieceType === type;
  }

  protected isSetupToolSelected(tool: 'hand' | 'delete'): boolean {
    return this.setupTool === tool;
  }

  protected toggleCastlingRight(key: 'whiteKingSide' | 'whiteQueenSide' | 'blackKingSide' | 'blackQueenSide'): void {
    if (!this.canSetupCastle(key)) {
      return;
    }

    this.setupCastling[key] = !this.setupCastling[key];
  }

  protected canSetupCastle(key: 'whiteKingSide' | 'whiteQueenSide' | 'blackKingSide' | 'blackQueenSide'): boolean {
    return this.computeCastleAvailability(key);
  }

  protected onSetupEnPassantSelected(value: string): void {
    this.setupEnPassant = value && value !== '-' ? value : '-';
  }

  protected getSetupEnPassantOptions(): string[] {
    const options: string[] = [];

    if (this.setupSideToMove === 'w') {
      for (let file = 0; file < 8; file++) {
        const blackPawnSquare = this.coordsToSquare(file, 3);
        const blackPawn = this.getPieceAtSquare(blackPawnSquare);
        if (!blackPawn || blackPawn.type !== 'bp') {
          continue;
        }

        const leftWhite = file > 0 ? this.getPieceAtSquare(this.coordsToSquare(file - 1, 3)) : null;
        const rightWhite = file < 7 ? this.getPieceAtSquare(this.coordsToSquare(file + 1, 3)) : null;
        const canCapture = leftWhite?.type === 'wp' || rightWhite?.type === 'wp';
        if (!canCapture) {
          continue;
        }

        options.push(this.coordsToSquare(file, 2));
      }
    } else {
      for (let file = 0; file < 8; file++) {
        const whitePawnSquare = this.coordsToSquare(file, 4);
        const whitePawn = this.getPieceAtSquare(whitePawnSquare);
        if (!whitePawn || whitePawn.type !== 'wp') {
          continue;
        }

        const leftBlack = file > 0 ? this.getPieceAtSquare(this.coordsToSquare(file - 1, 4)) : null;
        const rightBlack = file < 7 ? this.getPieceAtSquare(this.coordsToSquare(file + 1, 4)) : null;
        const canCapture = leftBlack?.type === 'bp' || rightBlack?.type === 'bp';
        if (!canCapture) {
          continue;
        }

        options.push(this.coordsToSquare(file, 5));
      }
    }

    return options;
  }

  protected setSetupSideToMove(side: 'w' | 'b'): void {
    this.setupSideToMove = side;
    this.normalizeSetupEnPassantSelection();
  }

  protected applyStartingPositionPreset(): void {
    this.pieces = this.parseFenToPieces(START_FEN);
    this.setupCastling = {
      whiteKingSide: true,
      whiteQueenSide: true,
      blackKingSide: true,
      blackQueenSide: true
    };
    this.setupEnPassant = '-';
    this.setupSideToMove = 'w';
    this.setupTool = 'hand';
    this.setupSelectedPieceType = null;
    this.normalizeSetupMetadata();
  }

  private handleSquareInteraction(square: string): void {
    if (this.isSubmittingMove) {
      return;
    }

    if (!this.selectedSquare) {
      if (!this.canSelectSquare(square)) {
        return;
      }

      this.selectedSquare = square;
      this.statusMessage = null;
      return;
    }

    if (this.selectedSquare === square) {
      this.selectedSquare = null;
      return;
    }

    const from = this.selectedSquare;
    this.selectedSquare = null;
    void this.tryApplyMove(from, square);
  }

  private async tryApplyMove(from: string, to: string): Promise<void> {
    this.isSubmittingMove = true;
    this.statusMessage = null;

    try {
      const response = await firstValueFrom(
        this.boardApi.applyMove({
          fen: this.currentFen,
          from,
          to,
          promotion: this.isPawnPromotion(from, to) ? 'q' : null
        })
      );

      if (!response.isValid || !response.fen) {
        this.statusMessage = null;
        return;
      }

      this.applySuccessfulMove(response.fen, response.san ?? `${from}${to}`);
    } catch (error) {
      this.statusMessage = this.resolveBackendErrorMessage(error);
    } finally {
      this.isSubmittingMove = false;
    }
  }

  private resolveBackendErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Unable to validate move against backend. Ensure API is running and restart ng serve (proxy enabled).';
      }

      if (typeof error.error === 'string' && error.error.trim().length > 0) {
        return error.error;
      }

      if (typeof error.error?.detail === 'string' && error.error.detail.trim().length > 0) {
        return error.error.detail;
      }
    }

    return 'Unable to validate move against backend.';
  }

  private resetGame(): void {
    this.sanHistory = [];
    this.fenHistory = [this.startFen];
    this.currentPly = 0;
    this.currentFen = this.startFen;
    this.pieces = this.parseFenToPieces(this.currentFen);
    this.selectedSquare = null;
    this.statusMessage = null;
    this.emitNavigationState();
    this.emitMoveRows();
  }

  private applySuccessfulMove(nextFen: string, san: string): void {
    const continuationFen = this.fenHistory[this.currentPly + 1];
    if (this.currentPly < this.sanHistory.length && continuationFen === nextFen) {
      this.currentPly++;
      this.currentFen = nextFen;
      this.pieces = this.parseFenToPieces(this.currentFen);
      this.emitNavigationState();
      return;
    }

    if (this.currentPly < this.sanHistory.length) {
      this.sanHistory = this.sanHistory.slice(0, this.currentPly);
      this.fenHistory = this.fenHistory.slice(0, this.currentPly + 1);
    }

    this.sanHistory = [...this.sanHistory, san];
    this.fenHistory = [...this.fenHistory, nextFen];
    this.currentPly = this.sanHistory.length;
    this.currentFen = nextFen;
    this.pieces = this.parseFenToPieces(this.currentFen);
    this.emitMoveRows();
    this.emitNavigationState();
  }

  private navigateToPly(targetPly: number): void {
    if (!Number.isFinite(targetPly)) {
      return;
    }

    const clamped = Math.max(0, Math.min(Math.trunc(targetPly), this.sanHistory.length));
    if (clamped === this.currentPly) {
      return;
    }

    this.currentPly = clamped;
    this.currentFen = this.fenHistory[this.currentPly] ?? START_FEN;
    this.pieces = this.parseFenToPieces(this.currentFen);
    this.selectedSquare = null;
    this.statusMessage = null;
    this.emitNavigationState();
  }

  private emitNavigationState(): void {
    this.currentPlyChanged.emit(this.currentPly);
  }

  private emitMoveRows(): void {
    const rows: MoveRow[] = [];

    const startSide = this.getStartSideToMove();
    let side: 'w' | 'b' = startSide;
    let moveNumber = 1;
    let openRow: MoveRow | null = null;

    for (let i = 0; i < this.sanHistory.length; i++) {
      const san = this.sanHistory[i] ?? '';
      const ply = i + 1;

      if (side === 'w') {
        openRow = {
          number: moveNumber,
          white: san,
          black: '',
          whitePly: ply,
          blackPly: null
        };
        side = 'b';
        continue;
      }

      if (!openRow) {
        rows.push({
          number: moveNumber,
          white: '...',
          black: san,
          whitePly: null,
          blackPly: ply
        });
      } else {
        openRow.black = san;
        openRow.blackPly = ply;
        rows.push(openRow);
      }

      moveNumber++;
      openRow = null;
      side = 'w';
    }

    if (openRow) {
      rows.push(openRow);
    }

    this.moveRowsChanged.emit(rows);
  }

  private getStartSideToMove(): 'w' | 'b' {
    const parts = this.startFen.split(' ');
    return parts[1] === 'b' ? 'b' : 'w';
  }

  private handleSetupSquareClick(square: string): void {
    if (this.setupTool === 'delete') {
      this.removePieceAtSquare(square);
      return;
    }

    if (this.setupTool === 'place' && this.setupSelectedPieceType) {
      this.placePiece(square, this.setupSelectedPieceType);
    }
  }

  private placePiece(square: string, type: string): void {
    if (!this.isValidPieceType(type)) {
      return;
    }

    this.removePieceAtSquare(square);
    const coords = this.squareToCoords(square);
    if (!coords) {
      return;
    }

    this.pieces = [
      ...this.pieces,
      {
        id: `${type}-${square}`,
        type,
        x: coords.x,
        y: coords.y
      }
    ];

    this.normalizeSetupMetadata();
  }

  private removePieceAtSquare(square: string): void {
    this.pieces = this.pieces.filter(piece => this.coordsToSquare(piece.x, piece.y) !== square);
    this.normalizeSetupMetadata();
  }

  private movePiece(fromSquare: string, toSquare: string): void {
    if (fromSquare === toSquare) {
      return;
    }

    const movingPiece = this.getPieceAtSquare(fromSquare);
    if (!movingPiece) {
      return;
    }

    this.removePieceAtSquare(toSquare);
    const coords = this.squareToCoords(toSquare);
    if (!coords) {
      return;
    }

    this.pieces = this.pieces.map(piece => {
      if (piece.id !== movingPiece.id) {
        return piece;
      }

      return {
        ...piece,
        id: `${piece.type}-${toSquare}`,
        x: coords.x,
        y: coords.y
      };
    });

    this.normalizeSetupMetadata();
  }

  private initializeSetupMetadataFromFen(fen: string): void {
    const parts = fen.trim().split(' ');
    const sideToMove = parts[1] ?? 'w';
    const castling = parts[2] ?? '-';
    const enPassant = parts[3] ?? '-';

    this.setupCastling = {
      whiteKingSide: castling.includes('K'),
      whiteQueenSide: castling.includes('Q'),
      blackKingSide: castling.includes('k'),
      blackQueenSide: castling.includes('q')
    };

    this.setupEnPassant = this.isValidSquareNotation(enPassant) ? enPassant : '-';
    this.setupSideToMove = sideToMove === 'b' ? 'b' : 'w';
    this.normalizeSetupMetadata();
  }

  private buildFenFromSetup(): string {
    const board: string[][] = Array.from({ length: 8 }, () => Array.from({ length: 8 }, () => ''));

    for (const piece of this.pieces) {
      const fenChar = this.pieceTypeToFenChar(piece.type);
      if (!fenChar) {
        continue;
      }

      const rank = 7 - piece.y;
      const file = piece.x;
      if (rank < 0 || rank > 7 || file < 0 || file > 7) {
        continue;
      }

      board[rank][file] = fenChar;
    }

    const placementParts: string[] = [];
    for (let rank = 7; rank >= 0; rank--) {
      let row = '';
      let empties = 0;

      for (let file = 0; file < 8; file++) {
        const piece = board[rank][file];
        if (!piece) {
          empties++;
          continue;
        }

        if (empties > 0) {
          row += String(empties);
          empties = 0;
        }

        row += piece;
      }

      if (empties > 0) {
        row += String(empties);
      }

      placementParts.push(row);
    }

    const castling =
      `${this.setupCastling.whiteKingSide ? 'K' : ''}` +
      `${this.setupCastling.whiteQueenSide ? 'Q' : ''}` +
      `${this.setupCastling.blackKingSide ? 'k' : ''}` +
      `${this.setupCastling.blackQueenSide ? 'q' : ''}`;

    const sideToMove = this.setupSideToMove;
    const fenCastling = castling || '-';
    const fenEnPassant = this.isValidSquareNotation(this.setupEnPassant) ? this.setupEnPassant : '-';

    return `${placementParts.join('/')} ${sideToMove} ${fenCastling} ${fenEnPassant} 0 1`;
  }

  private pieceTypeToFenChar(type: string): string | null {
    const map: Record<string, string> = {
      wk: 'K',
      wq: 'Q',
      wr: 'R',
      wb: 'B',
      wn: 'N',
      wp: 'P',
      bk: 'k',
      bq: 'q',
      br: 'r',
      bb: 'b',
      bn: 'n',
      bp: 'p'
    };

    return map[type] ?? null;
  }

  private squareToCoords(square: string): { x: number; y: number } | null {
    if (!this.isValidSquareNotation(square)) {
      return null;
    }

    const x = square.charCodeAt(0) - 'a'.charCodeAt(0);
    const rank = Number(square[1]);
    return { x, y: 8 - rank };
  }

  private isValidSquareNotation(value: string): boolean {
    return /^[a-h][1-8]$/.test(value);
  }

  private isValidPieceType(value: string): boolean {
    return Object.hasOwn(PIECE_URLS, value);
  }

  private normalizeSetupEnPassantSelection(): void {
    const options = this.getSetupEnPassantOptions();
    if (this.setupEnPassant === '-') {
      return;
    }

    if (!options.includes(this.setupEnPassant)) {
      this.setupEnPassant = '-';
    }
  }

  private normalizeSetupCastlingAvailability(): void {
    if (!this.computeCastleAvailability('whiteKingSide')) {
      this.setupCastling.whiteKingSide = false;
    }

    if (!this.computeCastleAvailability('whiteQueenSide')) {
      this.setupCastling.whiteQueenSide = false;
    }

    if (!this.computeCastleAvailability('blackKingSide')) {
      this.setupCastling.blackKingSide = false;
    }

    if (!this.computeCastleAvailability('blackQueenSide')) {
      this.setupCastling.blackQueenSide = false;
    }
  }

  private normalizeSetupMetadata(): void {
    this.normalizeSetupCastlingAvailability();
    this.normalizeSetupEnPassantSelection();
  }

  private computeCastleAvailability(key: 'whiteKingSide' | 'whiteQueenSide' | 'blackKingSide' | 'blackQueenSide'): boolean {
    const whiteKingOnE1 = this.getPieceTypeAtSquare('e1') === 'wk';
    const blackKingOnE8 = this.getPieceTypeAtSquare('e8') === 'bk';

    if (key === 'whiteKingSide') {
      return whiteKingOnE1 && this.getPieceTypeAtSquare('h1') === 'wr';
    }

    if (key === 'whiteQueenSide') {
      return whiteKingOnE1 && this.getPieceTypeAtSquare('a1') === 'wr';
    }

    if (key === 'blackKingSide') {
      return blackKingOnE8 && this.getPieceTypeAtSquare('h8') === 'br';
    }

    return blackKingOnE8 && this.getPieceTypeAtSquare('a8') === 'br';
  }

  private getPieceTypeAtSquare(square: string): string | null {
    return this.getPieceAtSquare(square)?.type ?? null;
  }

  private exitSetupMode(): void {
    this.isSetupMode = false;
    this.setupSnapshot = null;
    this.setupTool = 'hand';
    this.setupSelectedPieceType = null;
    this.setupEnPassant = '-';
    this.setupSideToMove = 'w';
    this.activeDrag = null;
    this.selectedSquare = null;
  }

  private canSelectSquare(square: string): boolean {
    const piece = this.getPieceAtSquare(square);
    if (!piece) {
      return false;
    }

    const sideToMove = this.getSideToMove();
    return sideToMove === 'w' ? piece.type.startsWith('w') : piece.type.startsWith('b');
  }

  private getPieceAtSquare(square: string): ChessPiece | undefined {
    return this.pieces.find(piece => this.coordsToSquare(piece.x, piece.y) === square);
  }

  private getSideToMove(): 'w' | 'b' {
    const fenParts = this.currentFen.split(' ');
    return fenParts[1] === 'b' ? 'b' : 'w';
  }

  private isPawnPromotion(from: string, to: string): boolean {
    const piece = this.getPieceAtSquare(from);
    if (!piece || (piece.type !== 'wp' && piece.type !== 'bp')) {
      return false;
    }

    const targetRank = to[1];
    return (piece.type === 'wp' && targetRank === '8') || (piece.type === 'bp' && targetRank === '1');
  }

  private displayCoordsToSquare(displayX: number, displayY: number): string {
    const boardX = this.isFlipped ? 7 - displayX : displayX;
    const boardY = this.isFlipped ? 7 - displayY : displayY;
    return this.coordsToSquare(boardX, boardY);
  }

  private getSquareFromClientPoint(clientX: number, clientY: number): string | null {
    const rect = this.boardGridRef.nativeElement.getBoundingClientRect();
    if (clientX < rect.left || clientX >= rect.right || clientY < rect.top || clientY >= rect.bottom) {
      return null;
    }

    const displayX = Math.floor(((clientX - rect.left) / rect.width) * 8);
    const displayY = Math.floor(((clientY - rect.top) / rect.height) * 8);
    return this.displayCoordsToSquare(displayX, displayY);
  }

  private coordsToSquare(x: number, y: number): string {
    const file = this.files[x];
    const rank = 8 - y;
    return `${file}${rank}`;
  }

  private parseFenToPieces(fen: string): ChessPiece[] {
    const placement = fen.split(' ')[0] ?? '';
    const ranks = placement.split('/');
    if (ranks.length !== 8) {
      return [];
    }

    const pieces: ChessPiece[] = [];
    for (let y = 0; y < 8; y++) {
      const rank = ranks[y] ?? '';
      let x = 0;

      for (const ch of rank) {
        if (ch >= '1' && ch <= '8') {
          x += Number(ch);
          continue;
        }

        const pieceType = this.mapFenCharToPieceType(ch);
        if (!pieceType) {
          continue;
        }

        pieces.push({
          id: `${pieceType}-${x}-${y}`,
          type: pieceType,
          x,
          y
        });

        x++;
      }
    }

    return pieces;
  }

  private mapFenCharToPieceType(ch: string): string | null {
    const map: Record<string, string> = {
      K: 'wk',
      Q: 'wq',
      R: 'wr',
      B: 'wb',
      N: 'wn',
      P: 'wp',
      k: 'bk',
      q: 'bq',
      r: 'br',
      b: 'bb',
      n: 'bn',
      p: 'bp'
    };

    return map[ch] ?? null;
  }
}
