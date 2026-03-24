import { Injectable, computed, signal } from '@angular/core';
import { jwtDecode } from 'jwt-decode';
import { Observable, map } from 'rxjs';
import { AuthApiService } from './auth-api.service';
import {
  AuthLoginRequest,
  AuthRegisterResponse,
  AuthRegisterRequest,
  ChangePendingEmailRequest,
  ConfirmEmailRequest,
  AuthTokenResponse,
  AuthUser,
  ForgotPasswordRequest,
  ResendEmailConfirmationRequest,
  ResetPasswordRequest
} from './auth.models';
import { AuthSessionService } from './auth-session.service';

interface JwtPayload {
  sub?: string;
  email?: string;
  unique_name?: string;
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'?: string;
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthStateService {
  private readonly currentUserSignal = signal<AuthUser | null>(null);

  readonly currentUser = computed(() => this.currentUserSignal());
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);
  readonly userName = computed(() => this.currentUserSignal()?.userName ?? null);

  constructor(
    private readonly authApi: AuthApiService,
    private readonly sessionService: AuthSessionService
  ) {
    this.restoreSession();
  }

  login(request: AuthLoginRequest): Observable<AuthUser> {
    return this.authApi.login(request).pipe(
      map(response => this.applyTokenResponse(response))
    );
  }

  register(request: AuthRegisterRequest): Observable<AuthRegisterResponse> {
    return this.authApi.register(request);
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<string> {
    return this.authApi.forgotPassword(request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<string> {
    return this.authApi.resetPassword(request);
  }

  resendConfirmation(request: ResendEmailConfirmationRequest): Observable<string> {
    return this.authApi.resendConfirmation(request);
  }

  changePendingEmail(request: ChangePendingEmailRequest): Observable<string> {
    return this.authApi.changePendingEmail(request);
  }

  confirmEmail(request: ConfirmEmailRequest): Observable<AuthUser> {
    return this.authApi.confirmEmail(request).pipe(
      map(response => this.applyTokenResponse(response))
    );
  }

  logout(): void {
    this.sessionService.clearSession();
    this.currentUserSignal.set(null);
  }

  getAccessToken(): string | null {
    return this.sessionService.getAccessToken();
  }

  private restoreSession(): void {
    if (!this.sessionService.hasValidSession()) {
      this.sessionService.clearSession();
      this.currentUserSignal.set(null);
      return;
    }

    const token = this.sessionService.getAccessToken();
    if (!token) {
      this.currentUserSignal.set(null);
      return;
    }

    const user = this.decodeUser(token);
    if (!user) {
      this.sessionService.clearSession();
      this.currentUserSignal.set(null);
      return;
    }

    this.currentUserSignal.set(user);
  }

  private applyTokenResponse(response: AuthTokenResponse): AuthUser {
    this.sessionService.setSession(response.accessToken, response.expiresAtUtc);

    const user = this.decodeUser(response.accessToken);
    if (!user) {
      this.sessionService.clearSession();
      throw new Error('Invalid access token payload.');
    }

    this.currentUserSignal.set(user);
    return user;
  }

  private decodeUser(accessToken: string): AuthUser | null {
    try {
      const payload = jwtDecode<JwtPayload>(accessToken);
      const userId = payload.sub ?? payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
      const userName = payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'] ?? payload.unique_name;
      const email = payload.email;

      if (!userId || !userName || !email) {
        return null;
      }

      return {
        userId,
        userName,
        email
      };
    } catch {
      return null;
    }
  }
}
