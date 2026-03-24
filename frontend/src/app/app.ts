import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ExplorerPageComponent } from './features/explorer/pages/explorer-page/explorer-page.component';
import { Sidebar } from './shared/components/sidebar/sidebar';
import { LoginModal } from './shared/components/login-modal/login-modal';
import { AboutModalComponent } from './shared/components/about-modal/about-modal';
import { AuthStateService } from './core/auth/auth-state.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, ExplorerPageComponent, Sidebar, LoginModal, AboutModalComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly authState = inject(AuthStateService);
  isLoginModalOpen = false;
  isFocusMode = false;
  isAboutModalOpen = false;
  readonly isResetPasswordView = window.location.pathname === '/reset-password';
  readonly resetEmail = new URLSearchParams(window.location.search).get('email') ?? '';
  readonly resetToken = new URLSearchParams(window.location.search).get('token') ?? '';

  toggleLoginModal() {
    this.isLoginModalOpen = !this.isLoginModalOpen;
  }

  toggleFocusMode() {
    this.isFocusMode = !this.isFocusMode;
  }

  toggleAboutModal() {
    this.isAboutModalOpen = !this.isAboutModalOpen;
  }

  signOut(): void {
    this.authState.logout();
    this.isLoginModalOpen = false;
  }

  onAuthenticated(): void {
    this.isLoginModalOpen = false;
  }
}
