import { Component, EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  @Output() loginClick = new EventEmitter<void>();
  @Output() toggleLayout = new EventEmitter<void>();

  onLoginClick() {
    this.loginClick.emit();
  }

  onToggleLayout() {
    this.toggleLayout.emit();
  }

  toggleDarkMode() {
    document.body.classList.toggle('dark-theme');
  }
}
