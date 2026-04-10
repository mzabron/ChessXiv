import { Component, EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-about-modal',
  standalone: true,
  imports: [],
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
