import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ExplorerPageComponent } from './features/explorer/pages/explorer-page/explorer-page.component';
import { Sidebar } from './shared/components/sidebar/sidebar';
import { LoginModal } from './shared/components/login-modal/login-modal';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, ExplorerPageComponent, Sidebar, LoginModal],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  isLoginModalOpen = false;
  isFocusMode = false;

  toggleLoginModal() {
    this.isLoginModalOpen = !this.isLoginModalOpen;
  }

  toggleFocusMode() {
    this.isFocusMode = !this.isFocusMode;
  }
}
