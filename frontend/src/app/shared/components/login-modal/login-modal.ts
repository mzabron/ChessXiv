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
  private readonly authState = inject(AuthStateService);
  private readonly fb = inject(FormBuilder);

  @Output() close = new EventEmitter<void>();
  @Output() authenticated = new EventEmitter<void>();
  @Input() initialMode: 'login' | 'register' | 'forgot' | 'reset' = 'login';
  @Input() resetEmail = '';
  @Input() resetToken = '';
  @Input() showCloseButton = true;

  protected readonly mode = signal<'login' | 'register' | 'forgot' | 'reset'>('login');
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly infoMessage = signal<string | null>(null);

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

  protected switchMode(nextMode: 'login' | 'register' | 'forgot' | 'reset'): void {
    this.mode.set(nextMode);
    this.errorMessage.set(null);
    this.infoMessage.set(null);
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
        next: () => {
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
        next: (message) => {
          this.infoMessage.set(message);
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
}
