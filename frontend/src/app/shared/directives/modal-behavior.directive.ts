import { Directive, ElementRef, EventEmitter, HostListener, OnDestroy, OnInit, Output, inject } from '@angular/core';

/**
 * Gives any dialog the keyboard behaviour a dialog is expected to have: Escape closes it,
 * Tab stays inside it, and focus returns to whatever opened it.
 *
 * Applied to the dialog element itself (the panel, not the backdrop). Without this, Tab
 * walks straight out of an open modal into the page behind it, which is both disorienting
 * and a genuine accessibility failure - a keyboard user could not reliably reach the modal's
 * own buttons.
 */
@Directive({
  selector: '[appModalBehavior]',
  standalone: true
})
export class ModalBehaviorDirective implements OnInit, OnDestroy {
  /** Emitted on Escape. The host decides what closing means. */
  @Output() readonly dismiss = new EventEmitter<void>();

  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private previouslyFocused: HTMLElement | null = null;

  private static readonly focusableSelector = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    'details > summary',
    '[tabindex]:not([tabindex="-1"])'
  ].join(',');

  ngOnInit(): void {
    this.previouslyFocused = document.activeElement as HTMLElement | null;

    // Defer: the dialog's children may not be rendered on the same tick it appears.
    queueMicrotask(() => {
      const focusable = this.getFocusableElements();
      (focusable[0] ?? this.host.nativeElement).focus?.();
    });
  }

  ngOnDestroy(): void {
    // Returning focus is what keeps keyboard context after the dialog goes away.
    this.previouslyFocused?.focus?.();
  }

  @HostListener('keydown', ['$event'])
  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.dismiss.emit();
      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const focusable = this.getFocusableElements();
    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    // Wrap around at both ends rather than letting focus escape the dialog.
    if (event.shiftKey && (active === first || !this.host.nativeElement.contains(active))) {
      event.preventDefault();
      last.focus();
      return;
    }

    if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private getFocusableElements(): HTMLElement[] {
    return Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>(ModalBehaviorDirective.focusableSelector)
    ).filter(element => element.offsetParent !== null || element === document.activeElement);
  }
}
