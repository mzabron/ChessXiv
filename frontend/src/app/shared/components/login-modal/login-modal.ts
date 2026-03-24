import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthStateService } from '../../../core/auth/auth-state.service';

@Component({
  selector: 'app-login-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login-modal.html',
  styleUrl: './login-modal.scss',
})
export class LoginModal implements OnInit {
  private static readonly verifyEmailMessage = 'Please confirm your email address before signing in.';

  private readonly authState = inject(AuthStateService);
  private readonly fb = inject(FormBuilder);

  @Output() close = new EventEmitter<void>();
  @Output() authenticated = new EventEmitter<void>();
  @Input() initialMode: 'login' | 'register' | 'forgot' | 'reset' | 'verify-email' = 'login';
  @Input() resetEmail = '';
  @Input() resetToken = '';
  @Input() showCloseButton = true;

  protected readonly mode = signal<'login' | 'register' | 'forgot' | 'reset' | 'verify-email'>('login');
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly infoMessage = signal<string | null>(null);
  protected readonly pendingConfirmationIdentifier = signal('');
  protected readonly showChangePendingEmail = signal(false);
  protected readonly pendingEmailChangePassword = signal('');

  protected readonly loginForm = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    password: ['', [Validators.required]]
  });

  protected readonly registerForm = this.fb.nonNullable.group({
    username: ['', [Validators.required, Validators.minLength(3)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]]
  });

  protected readonly forgotForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  protected readonly resetForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    token: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]]
  });

  protected readonly changePendingEmailForm = this.fb.nonNullable.group({
    newEmail: ['', [Validators.required, Validators.email]],
    password: ['']
  });

  ngOnInit(): void {
    this.mode.set(this.initialMode);

    if (this.resetEmail || this.resetToken) {
      this.resetForm.patchValue({
        email: this.resetEmail,
        token: this.resetToken
      });
    }
  }

  onClose(event?: MouseEvent) {
    if (event) {
      event.stopPropagation();
    }
    this.close.emit();
  }

  protected switchMode(nextMode: 'login' | 'register' | 'forgot' | 'reset' | 'verify-email'): void {
    this.mode.set(nextMode);
    this.errorMessage.set(null);
    this.infoMessage.set(null);

    if (nextMode !== 'verify-email') {
      this.showChangePendingEmail.set(false);
      this.pendingEmailChangePassword.set('');
    }
  }

  protected submitLogin(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    const raw = this.loginForm.getRawValue();
    this.startSubmitting();

    this.authState.login({
      login: raw.username.trim(),
      password: raw.password
    })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: () => {
          this.authenticated.emit();
        },
        error: (error) => {
          const errorCode = this.extractErrorCode(error);
          if (errorCode === 'EMAIL_NOT_CONFIRMED') {
            const pendingIdentifier = this.extractPendingIdentifier(error) || raw.username.trim();
            this.pendingConfirmationIdentifier.set(pendingIdentifier);
            this.changePendingEmailForm.patchValue({
              newEmail: this.extractPendingIdentifier(error) || '',
              password: ''
            });
            this.pendingEmailChangePassword.set(raw.password);
            this.showChangePendingEmail.set(false);
            this.switchMode('verify-email');
            this.infoMessage.set(LoginModal.verifyEmailMessage);
            return;
          }

          this.errorMessage.set(this.extractErrorMessage(error, 'Unable to sign in.'));
        }
      });
  }

  protected submitRegister(): void {
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    const raw = this.registerForm.getRawValue();
    if (raw.password !== raw.confirmPassword) {
      this.errorMessage.set('Password confirmation does not match.');
      return;
    }

    this.startSubmitting();

    this.authState.register({
      login: raw.username.trim(),
      email: raw.email.trim(),
      password: raw.password
    })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (response) => {
          if (response.requiresEmailConfirmation) {
            const pendingIdentifier = response.email || raw.email.trim();
            this.pendingConfirmationIdentifier.set(pendingIdentifier);
            this.changePendingEmailForm.patchValue({
              newEmail: pendingIdentifier,
              password: ''
            });
            this.pendingEmailChangePassword.set(raw.password);
            this.showChangePendingEmail.set(false);
            this.switchMode('verify-email');
            this.infoMessage.set(LoginModal.verifyEmailMessage);
            return;
          }

          this.authenticated.emit();
        },
        error: (error) => {
          this.errorMessage.set(this.extractErrorMessage(error, 'Unable to create account.'));
        }
      });
  }

  protected submitForgotPassword(): void {
    if (this.forgotForm.invalid) {
      this.forgotForm.markAllAsTouched();
      return;
    }

    const raw = this.forgotForm.getRawValue();
    this.startSubmitting();

    this.authState.forgotPassword({
      email: raw.email.trim()
    })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: () => {
          this.infoMessage.set(LoginModal.verifyEmailMessage);
          this.errorMessage.set(null);
        },
        error: (error) => {
          this.errorMessage.set(this.extractErrorMessage(error, 'Unable to send password reset instructions.'));
        }
      });
  }

  protected submitResetPassword(): void {
    if (this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }

    const raw = this.resetForm.getRawValue();
    if (raw.newPassword !== raw.confirmPassword) {
      this.errorMessage.set('Password confirmation does not match.');
      return;
    }

    this.startSubmitting();
    this.authState.resetPassword({
      email: raw.email.trim(),
      token: raw.token.trim(),
      newPassword: raw.newPassword
    })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (message) => {
          this.infoMessage.set(message || 'Password has been reset. You can now sign in.');
          this.errorMessage.set(null);
          this.switchMode('login');
          this.loginForm.patchValue({ username: raw.email.trim(), password: '' });
        },
        error: (error) => {
          this.errorMessage.set(this.extractErrorMessage(error, 'Unable to reset password.'));
        }
      });
  }

  protected openForgotPassword(): void {
    this.switchMode('forgot');
    const currentLogin = this.loginForm.getRawValue().username.trim();

    if (currentLogin.includes('@')) {
      this.forgotForm.patchValue({ email: currentLogin });
    }
  }

  protected submitChangePendingEmail(): void {
    if (this.changePendingEmailForm.invalid) {
      this.changePendingEmailForm.markAllAsTouched();
      return;
    }

    const identifier = this.pendingConfirmationIdentifier().trim();
    if (!identifier) {
      this.errorMessage.set('Missing email or username for changing email address.');
      return;
    }

    const raw = this.changePendingEmailForm.getRawValue();
    const password = this.pendingEmailChangePassword() || raw.password;

    if (!password.trim()) {
      this.errorMessage.set('Please sign in again before changing your email address.');
      return;
    }

    this.startSubmitting();
    this.authState.changePendingEmail({
      usernameOrEmail: identifier,
      password,
      newEmail: raw.newEmail.trim()
    })
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: () => {
          this.pendingConfirmationIdentifier.set(raw.newEmail.trim());
          this.changePendingEmailForm.patchValue({
            newEmail: raw.newEmail.trim(),
            password: ''
          });
          this.pendingEmailChangePassword.set(password);
          this.showChangePendingEmail.set(false);
          this.infoMessage.set(LoginModal.verifyEmailMessage);
          this.errorMessage.set(null);
        },
        error: (error) => {
          if (this.extractHttpStatus(error) === 401) {
            this.pendingEmailChangePassword.set('');
            this.errorMessage.set('Please enter your password to confirm email change.');
            return;
          }

          this.errorMessage.set(this.extractErrorMessage(error, 'Unable to update email address.'));
        }
      });
  }

  protected openChangePendingEmail(): void {
    this.showChangePendingEmail.set(true);
  }

  protected cancelChangePendingEmail(): void {
    this.showChangePendingEmail.set(false);
    this.changePendingEmailForm.patchValue({ password: '' });
    this.errorMessage.set(null);
  }

  protected requiresPasswordInputForPendingEmailChange(): boolean {
    return this.pendingEmailChangePassword().trim().length === 0;
  }

  protected shouldShowRegisterEmailError(): boolean {
    const emailControl = this.registerForm.controls.email;
    return emailControl.invalid && (emailControl.touched || emailControl.dirty);
  }

  protected getRegisterEmailError(): string {
    const emailControl = this.registerForm.controls.email;

    if (emailControl.hasError('required')) {
      return 'Email is required.';
    }

    if (emailControl.hasError('email')) {
      return 'Wrong email address format.';
    }

    return 'Invalid email address.';
  }

  private startSubmitting(): void {
    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.infoMessage.set(null);
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (typeof error === 'object' && error !== null && 'error' in error) {
      const payload = (error as { error: unknown }).error;

      if (typeof payload === 'string' && payload.trim().length > 0) {
        return payload;
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

  private extractErrorCode(error: unknown): string | null {
    if (typeof error !== 'object' || error === null || !('error' in error)) {
      return null;
    }

    const payload = (error as { error: unknown }).error;
    if (typeof payload === 'object' && payload !== null && 'code' in payload) {
      const code = (payload as { code?: unknown }).code;
      if (typeof code === 'string' && code.trim().length > 0) {
        return code;
      }
    }

    return null;
  }

  private extractPendingIdentifier(error: unknown): string | null {
    if (typeof error !== 'object' || error === null || !('error' in error)) {
      return null;
    }

    const payload = (error as { error: unknown }).error;
    if (typeof payload === 'object' && payload !== null && 'email' in payload) {
      const email = (payload as { email?: unknown }).email;
      if (typeof email === 'string' && email.trim().length > 0) {
        return email;
      }
    }

    return null;
  }

  private extractHttpStatus(error: unknown): number | null {
    if (typeof error !== 'object' || error === null) {
      return null;
    }

    if ('status' in error) {
      const status = (error as { status?: unknown }).status;
      if (typeof status === 'number') {
        return status;
      }
    }

    return null;
  }
}
