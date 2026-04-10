import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { AccountApiService } from '../../../core/auth/account-api.service';
import { AccountSummary } from '../../../core/auth/account.models';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  private readonly fb = inject(FormBuilder);
  private readonly accountApi = inject(AccountApiService);

  @Input() isAuthenticated = false;
  @Input() userName = '';

  @Output() loginClick = new EventEmitter<void>();
  @Output() signOutClick = new EventEmitter<void>();
  @Output() toggleLayout = new EventEmitter<void>();
  @Output() aboutClick = new EventEmitter<void>();

  protected isUserMenuOpen = false;
  protected readonly accountSummary = signal<AccountSummary | null>(null);
  protected readonly isLoadingSummary = signal(false);
  protected readonly accountError = signal<string | null>(null);
  protected readonly accountInfo = signal<string | null>(null);
  protected readonly activePanel = signal<'none' | 'change-email' | 'change-password' | 'delete-account'>('none');

  protected readonly changeEmailForm = this.fb.nonNullable.group({
    newEmail: ['', [Validators.required, Validators.email]],
    currentPassword: ['', [Validators.required]]
  });

  protected readonly changePasswordForm = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmNewPassword: ['', [Validators.required]]
  });

  protected readonly deleteAccountForm = this.fb.nonNullable.group({
    password: ['', [Validators.required]]
  });

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
      if (this.isUserMenuOpen) {
        this.accountError.set(null);
        this.accountInfo.set(null);
        this.activePanel.set('none');
        this.loadSummary();
      }
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
    this.activePanel.set('none');
  }

  protected openPanel(panel: 'change-email' | 'change-password' | 'delete-account'): void {
    this.accountError.set(null);
    this.accountInfo.set(null);

    if (panel === 'change-email') {
      const summary = this.accountSummary();
      this.changeEmailForm.patchValue({
        newEmail: summary?.email ?? '',
        currentPassword: ''
      });
    }

    if (panel === 'change-password') {
      this.changePasswordForm.reset({
        currentPassword: '',
        newPassword: '',
        confirmNewPassword: ''
      });
    }

    this.activePanel.set(panel);
  }

  protected closePanel(): void {
    this.accountError.set(null);
    this.accountInfo.set(null);
    this.changePasswordForm.reset({
      currentPassword: '',
      newPassword: '',
      confirmNewPassword: ''
    });
    this.activePanel.set('none');
  }

  protected submitChangeEmail(): void {
    if (this.changeEmailForm.invalid) {
      this.changeEmailForm.markAllAsTouched();
      return;
    }

    const raw = this.changeEmailForm.getRawValue();
    this.accountError.set(null);
    this.accountInfo.set(null);

    this.accountApi.changeEmail({
      newEmail: raw.newEmail.trim(),
      currentPassword: raw.currentPassword
    }).subscribe({
      next: (message) => {
        this.accountInfo.set(message || 'Check your email inbox to confirm address change.');
        this.changeEmailForm.reset({
          newEmail: '',
          currentPassword: ''
        });
        this.activePanel.set('none');
      },
      error: (error) => {
        this.accountError.set(this.extractErrorMessage(error, 'Unable to change email.'));
      }
    });
  }

  protected submitChangePassword(): void {
    if (this.changePasswordForm.invalid) {
      this.changePasswordForm.markAllAsTouched();
      return;
    }

    const raw = this.changePasswordForm.getRawValue();

    if (raw.newPassword !== raw.confirmNewPassword) {
      this.accountError.set('New password and retyped password do not match.');
      return;
    }

    this.accountError.set(null);
    this.accountInfo.set(null);

    this.accountApi.changePassword({
      currentPassword: raw.currentPassword,
      newPassword: raw.newPassword
    }).subscribe({
      next: (message) => {
        this.accountInfo.set(message || 'Password updated successfully.');
        this.accountError.set(null);
        this.changePasswordForm.reset({
          currentPassword: '',
          newPassword: '',
          confirmNewPassword: ''
        });
        this.activePanel.set('none');
      },
      error: (error) => {
        this.accountError.set(this.extractErrorMessage(error, 'Unable to change password.'));
      }
    });
  }

  protected submitDeleteAccount(): void {
    if (this.deleteAccountForm.invalid) {
      this.deleteAccountForm.markAllAsTouched();
      return;
    }

    const raw = this.deleteAccountForm.getRawValue();
    this.accountError.set(null);
    this.accountInfo.set(null);

    this.accountApi.deleteAccount({
      password: raw.password
    }).subscribe({
      next: () => {
        this.signOutClick.emit();
        this.isUserMenuOpen = false;
      },
      error: (error) => {
        this.accountError.set(this.extractErrorMessage(error, 'Unable to delete account.'));
      }
    });
  }

  protected formatQuota(used: number, limit: number): string {
    const usedText = used.toLocaleString();
    const limitText = limit === 2147483647 ? 'unlimited' : limit.toLocaleString();
    return `${usedText}/${limitText}`;
  }

  private loadSummary(): void {
    this.isLoadingSummary.set(true);
    this.accountError.set(null);

    this.accountApi.getSummary()
      .pipe(finalize(() => this.isLoadingSummary.set(false)))
      .subscribe({
        next: (summary) => {
          this.accountSummary.set(summary);
        },
        error: () => {
          this.accountSummary.set(null);
          this.accountError.set('Unable to load account details.');
        }
      });
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const payload = (error as { error: unknown }).error;

      if (typeof payload === 'string' && payload.trim().length > 0) {
        const parsedMessage = this.tryExtractMessageFromJsonString(payload);
        return parsedMessage ?? payload;
      }

      if (typeof payload === 'object' && payload !== null && 'errors' in payload) {
        const errors = (payload as { errors?: unknown }).errors;
        if (Array.isArray(errors) && errors.length > 0) {
          return String(errors[0]);
        }
      }

      if (typeof payload === 'object' && payload !== null && 'Errors' in payload) {
        const errors = (payload as { Errors?: unknown }).Errors;
        if (Array.isArray(errors) && errors.length > 0) {
          return String(errors[0]);
        }
      }
    }

    return fallback;
  }

  private tryExtractMessageFromJsonString(raw: string): string | null {
    try {
      const parsed = JSON.parse(raw) as unknown;
      if (typeof parsed !== 'object' || parsed === null) {
        return null;
      }

      if ('errors' in parsed) {
        const errors = (parsed as { errors?: unknown }).errors;
        if (Array.isArray(errors) && errors.length > 0) {
          return String(errors[0]);
        }
      }

      if ('Errors' in parsed) {
        const errors = (parsed as { Errors?: unknown }).Errors;
        if (Array.isArray(errors) && errors.length > 0) {
          return String(errors[0]);
        }
      }

      return null;
    } catch {
      return null;
    }
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
