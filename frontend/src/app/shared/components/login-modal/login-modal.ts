import { Component, EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-login-modal',
  standalone: true,
  imports: [],
  templateUrl: './login-modal.html',
  styleUrl: './login-modal.scss',
})
export class LoginModal {
  @Output() close = new EventEmitter<void>();

  onClose(event?: MouseEvent) {
    if (event) {
      event.stopPropagation();
    }
    this.close.emit();
  }
}
