import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AuthRegisterResponse,
  AuthLoginRequest,
  AuthRegisterRequest,
  AuthTokenResponse,
  ChangePendingEmailRequest,
  ConfirmEmailRequest,
  ForgotPasswordRequest,
  ResendEmailConfirmationRequest,
  ResetPasswordRequest
} from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  register(request: AuthRegisterRequest): Observable<AuthRegisterResponse> {
    return this.http.post<AuthRegisterResponse>(`${this.baseUrl}/auth/register`, request);
  }

  createGuestSession(): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/auth/guest-session`, {});
  }

  login(request: AuthLoginRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/auth/login`, request);
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/auth/forgot-password`, request, {
      responseType: 'text'
    });
  }

  resetPassword(request: ResetPasswordRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/auth/reset-password`, request, {
      responseType: 'text'
    });
  }

  resendConfirmation(request: ResendEmailConfirmationRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/auth/resend-confirmation`, request, {
      responseType: 'text'
    });
  }

  changePendingEmail(request: ChangePendingEmailRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/auth/change-pending-email`, request, {
      responseType: 'text'
    });
  }

  confirmEmail(request: ConfirmEmailRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/auth/confirm-email`, request);
  }

  /**
   * Reassigns a guest's staging games onto the account they just signed into. The route
   * lives under /api/pgn (that's where the staging rows live), but the operation itself is
   * part of the auth transition, so it's called from here rather than a drafts-feature
   * service.
   */
  claimGuestDraft(guestToken: string): Observable<{ claimed: boolean; gameCount: number }> {
    return this.http.post<{ claimed: boolean; gameCount: number }>(`${this.baseUrl}/pgn/drafts/claim`, {
      guestToken
    });
  }
}
