import { Directive, ElementRef, EventEmitter, Input, OnDestroy, Output, inject } from '@angular/core';

/**
 * Drag-to-reorder for a strip of tabs, with the order persisted per strip.
 *
 * Lives here rather than in a component because two separate tab strips need it (the games
 * panel and the focus-mode side panel) and the logic is identical - only the tab identifiers
 * and the storage key differ.
 *
 * Uses pointer events rather than HTML5 drag-and-drop. Native DnD hands the drag image to
 * the browser, which lets the tab float freely in both axes and away from the strip; a tab
 * only ever changes its horizontal position, so the drag should not suggest otherwise. Here
 * the dragged tab is translated along X only, and clamped to the strip's own bounds.
 *
 * The order is stored under `storageKey` and rebuilt from `defaultOrder` on read, so a stale
 * or hand-edited value can never drop a tab or introduce one that no longer exists.
 */
@Directive({
  selector: '[appReorderableTabs]',
  standalone: true,
  exportAs: 'reorderableTabs'
})
export class ReorderableTabsDirective<T extends string = string> implements OnDestroy {
  /** Movement below this many pixels stays a click rather than becoming a drag. */
  private static readonly dragThresholdPx = 4;

  @Input({ required: true }) storageKey!: string;

  @Input({ required: true })
  set defaultOrder(value: readonly T[]) {
    this.knownTabs = [...value];
    this.order = this.readPersistedOrder();
    this.orderChange.emit(this.order);
  }

  @Output() readonly orderChange = new EventEmitter<T[]>();

  order: T[] = [];
  /** The tab being dragged, once past the threshold. Null while it is still just a click. */
  draggedTab: T | null = null;
  dragOverTab: T | null = null;
  /** Horizontal offset applied to the dragged tab, in pixels. */
  dragOffsetX = 0;

  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private knownTabs: T[] = [];
  private pointerStartX = 0;
  private candidateTab: T | null = null;
  private suppressNextClick = false;
  private activeCleanup: (() => void) | null = null;

  ngOnDestroy(): void {
    this.activeCleanup?.();
  }

  onPointerDown(tab: T, event: PointerEvent): void {
    // Left button only; right-click and middle-click have their own meanings.
    if (event.button !== 0) {
      return;
    }

    this.candidateTab = tab;
    this.pointerStartX = event.clientX;
    this.suppressNextClick = false;

    const onMove = (moveEvent: PointerEvent) => this.onPointerMove(moveEvent);
    const onUp = (upEvent: PointerEvent) => this.onPointerUp(upEvent);

    this.activeCleanup = () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
      window.removeEventListener('pointercancel', onUp);
      this.activeCleanup = null;
    };

    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    window.addEventListener('pointercancel', onUp);
  }

  /**
   * Swallows the click that follows a drag, so releasing over a different tab reorders
   * without also switching to whichever tab happened to be under the pointer.
   */
  onClickCapture(event: MouseEvent): boolean {
    if (!this.suppressNextClick) {
      return true;
    }

    this.suppressNextClick = false;
    event.preventDefault();
    event.stopPropagation();
    return false;
  }

  private onPointerMove(event: PointerEvent): void {
    if (!this.candidateTab) {
      return;
    }

    const deltaX = event.clientX - this.pointerStartX;

    if (!this.draggedTab) {
      if (Math.abs(deltaX) < ReorderableTabsDirective.dragThresholdPx) {
        return;
      }

      this.draggedTab = this.candidateTab;
    }

    // Vertical movement is ignored entirely, and the tab cannot leave the strip.
    this.dragOffsetX = this.clampToStrip(deltaX);
    this.dragOverTab = this.findTabUnderPointer(event.clientX, this.draggedTab);
  }

  private onPointerUp(event: PointerEvent): void {
    this.activeCleanup?.();

    const dragged = this.draggedTab;
    const target = dragged ? this.findTabUnderPointer(event.clientX, dragged) : null;

    this.candidateTab = null;
    this.draggedTab = null;
    this.dragOverTab = null;
    this.dragOffsetX = 0;

    if (!dragged) {
      return;
    }

    // A completed drag must not also register as a tab selection.
    this.suppressNextClick = true;

    if (!target || target === dragged) {
      return;
    }

    // Indexing into `next` (which no longer holds `dragged`) with the target's index from
    // the *original* order looks off by one, but is what makes both directions behave: for
    // a target to the right the old index is one higher, so the tab lands after it; for a
    // target to the left the indices match, so it lands before it. Both are what dragging
    // that way should do.
    const next = this.order.filter(candidate => candidate !== dragged);
    next.splice(this.order.indexOf(target), 0, dragged);

    this.order = next;
    this.persistOrder(next);
    this.orderChange.emit(next);
  }

  /** Keeps the dragged tab inside the strip so it never floats off into the layout. */
  private clampToStrip(deltaX: number): number {
    const element = this.getTabElement(this.draggedTab);
    if (!element) {
      return deltaX;
    }

    const stripRect = this.host.nativeElement.getBoundingClientRect();
    const tabRect = element.getBoundingClientRect();

    // The rect already includes any offset currently applied, so remove it first.
    const restingLeft = tabRect.left - this.dragOffsetX;
    const restingRight = tabRect.right - this.dragOffsetX;

    return Math.min(
      Math.max(deltaX, stripRect.left - restingLeft),
      stripRect.right - restingRight
    );
  }

  /**
   * The tab the pointer is over, ignoring the one being dragged.
   *
   * That exclusion is essential: the dragged tab carries a translateX, so its own rect
   * tracks the pointer exactly. Including it meant the hit test matched the dragged tab
   * itself whenever it came earlier in DOM order - which made dragging leftwards work
   * (targets are scanned first) while dragging rightwards silently did nothing.
   */
  private findTabUnderPointer(clientX: number, exclude: T | null): T | null {
    const elements = this.getTabElements();

    for (let index = 0; index < elements.length; index++) {
      const tab = this.order[index];
      if (tab === undefined || tab === exclude) {
        continue;
      }

      const rect = elements[index].getBoundingClientRect();
      if (clientX >= rect.left && clientX <= rect.right) {
        return tab;
      }
    }

    return null;
  }

  private getTabElements(): HTMLElement[] {
    return Array.from(this.host.nativeElement.children) as HTMLElement[];
  }

  private getTabElement(tab: T | null): HTMLElement | null {
    if (!tab) {
      return null;
    }

    return this.getTabElements()[this.order.indexOf(tab)] ?? null;
  }

  private readPersistedOrder(): T[] {
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (!raw) {
        return [...this.knownTabs];
      }

      const parsed = JSON.parse(raw) as unknown;
      if (!Array.isArray(parsed)) {
        return [...this.knownTabs];
      }

      const stored = parsed.filter((tab): tab is T => this.knownTabs.includes(tab as T));
      const missing = this.knownTabs.filter(tab => !stored.includes(tab));

      return [...stored, ...missing];
    } catch {
      return [...this.knownTabs];
    }
  }

  private persistOrder(order: T[]): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(order));
    } catch {
      // Persisting is a convenience; blocked or full storage must not break reordering.
    }
  }
}
