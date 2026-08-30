import { Component, EventEmitter, Output } from '@angular/core';
import { ModalBehaviorDirective } from '../../directives/modal-behavior.directive';

@Component({
  selector: 'app-about-modal',
  standalone: true,
  imports: [ModalBehaviorDirective],
  templateUrl: './about-modal.html',
  styleUrl: './about-modal.scss',
})
export class AboutModalComponent {
  @Output() close = new EventEmitter<void>();

  onClose(event?: MouseEvent) {
    if (event) {
      event.stopPropagation();
    }
    this.close.emit();
  }
}
