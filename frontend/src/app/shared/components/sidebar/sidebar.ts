import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  @Input() isAuthenticated = false;
  @Input() userName = '';

  @Output() loginClick = new EventEmitter<void>();
  @Output() signOutClick = new EventEmitter<void>();
  @Output() toggleLayout = new EventEmitter<void>();
  @Output() aboutClick = new EventEmitter<void>();

  protected isUserMenuOpen = false;

  protected get userInitial(): string {
    const trimmed = this.userName.trim();
    if (!trimmed) {
      return '?';
    }

    return trimmed[0].toUpperCase();
  }

  protected onUserButtonClick(event: MouseEvent): void {
    event.stopPropagation();

    if (this.isAuthenticated) {
      this.isUserMenuOpen = !this.isUserMenuOpen;
      return;
    }

    this.loginClick.emit();
  }

  protected onSignOutClick(event: MouseEvent): void {
    event.stopPropagation();
    this.isUserMenuOpen = false;
    this.signOutClick.emit();
  }

  @HostListener('document:click')
  protected closeUserMenu(): void {
    this.isUserMenuOpen = false;
  }

  onToggleLayout() {
    this.toggleLayout.emit();
  }

  onAboutClick() {
    this.aboutClick.emit();
  }

  toggleDarkMode() {
    document.body.classList.toggle('dark-theme');
  }
}
