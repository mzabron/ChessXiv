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

  @Input() navigationRequest: { ply: number; version: number } | null = null;

  @Output() readonly moveRowsChanged = new EventEmitter<MoveRow[]>();
  @Output() readonly currentPlyChanged = new EventEmitter<number>();

  pieces: ChessPiece[] = [];
  protected selectedSquare: string | null = null;
  protected statusMessage: string | null = null;
  protected isSubmittingMove = false;

  private currentFen = START_FEN;
  private fenHistory: string[] = [START_FEN];
  private sanHistory: string[] = [];
  private currentPly = 0;
  private isFlipped = false;
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
    this.handleSquareInteraction(square);
  }

  protected onPiecePointerDown(piece: ChessPiece, event: PointerEvent): void {
    event.stopPropagation();

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
      this.handleSquareInteraction(drag.sourceSquare);
      return;
    }

    this.suppressNextSquareClick = true;
    const targetSquare = this.getSquareFromClientPoint(event.clientX, event.clientY);
    if (!targetSquare || targetSquare === drag.sourceSquare || this.isSubmittingMove) {
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
    this.navigateToPly(this.currentPly - 1);
  }

  protected goNextMove(): void {
    this.navigateToPly(this.currentPly + 1);
  }

  protected goToGameStart(): void {
    this.navigateToPly(0);
  }

  protected goToGameEnd(): void {
    this.navigateToPly(this.sanHistory.length);
  }

  protected setPosition(): void {}

  protected clearPosition(): void {
    this.resetGame();
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
    this.fenHistory = [START_FEN];
    this.currentPly = 0;
    this.currentFen = START_FEN;
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

    for (let i = 0; i < this.sanHistory.length; i += 2) {
      const whitePly = i + 1;
      const blackPly = i + 2;
      rows.push({
        number: Math.floor(i / 2) + 1,
        white: this.sanHistory[i] ?? '',
        black: this.sanHistory[i + 1] ?? '',
        whitePly,
        blackPly: this.sanHistory[i + 1] ? blackPly : null
      });
    }

    this.moveRowsChanged.emit(rows);
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
