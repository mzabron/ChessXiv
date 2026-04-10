import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild } from '@angular/core';

export interface MoveRow {
  number: number;
  white: string;
  black: string;
  whitePly: number | null;
  blackPly: number | null;
  whiteClk?: string | null;
  blackClk?: string | null;
}

@Component({
  selector: 'app-move-list',
  standalone: true,
  templateUrl: './move-list.component.html',
  styleUrl: './move-list.component.scss'
})
export class MoveListComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('movesTable')
  private readonly movesTableRef?: ElementRef<HTMLElement>;

  @Input() moveRows: MoveRow[] = [];
  @Input() currentPly = 0;
  @Input() gameResult: string | null = null;

  @Output() readonly plySelected = new EventEmitter<number>();

  private pendingScrollAnimationFrame: number | null = null;

  ngAfterViewInit(): void {
    this.queueScrollToCurrentPly();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('currentPly' in changes || 'moveRows' in changes) {
      this.queueScrollToCurrentPly();
    }
  }

  ngOnDestroy(): void {
    if (this.pendingScrollAnimationFrame !== null) {
      window.cancelAnimationFrame(this.pendingScrollAnimationFrame);
      this.pendingScrollAnimationFrame = null;
    }
  }

  protected selectPly(ply: number): void {
    this.plySelected.emit(ply);
  }

  private queueScrollToCurrentPly(): void {
    if (this.pendingScrollAnimationFrame !== null) {
      window.cancelAnimationFrame(this.pendingScrollAnimationFrame);
    }

    this.pendingScrollAnimationFrame = window.requestAnimationFrame(() => {
      this.pendingScrollAnimationFrame = null;
      this.scrollToCurrentPly();
    });
  }

  private scrollToCurrentPly(): void {
    const container = this.movesTableRef?.nativeElement;
    if (!container) {
      return;
    }

    if (this.currentPly <= 0) {
      container.scrollTo({ top: 0, behavior: 'smooth' });
      return;
    }

    const activeMove = container.querySelector<HTMLButtonElement>(`button.move-btn.active[data-ply="${this.currentPly}"]`)
      ?? container.querySelector<HTMLButtonElement>('button.move-btn.active');

    if (!activeMove) {
      return;
    }

    const containerRect = container.getBoundingClientRect();
    const moveRect = activeMove.getBoundingClientRect();
    const moveTop = container.scrollTop + (moveRect.top - containerRect.top);
    const targetScrollTop = moveTop - (container.clientHeight / 2) + (activeMove.offsetHeight / 2);
    const maxScrollTop = Math.max(0, container.scrollHeight - container.clientHeight);
    const clamped = Math.max(0, Math.min(targetScrollTop, maxScrollTop));

    container.scrollTo({ top: clamped, behavior: 'smooth' });
  }
}
