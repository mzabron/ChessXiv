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
  private readonly baseUrl = this.resolveBaseUrl();

  register(request: AuthRegisterRequest): Observable<AuthRegisterResponse> {
    return this.http.post<AuthRegisterResponse>(`${this.baseUrl}/auth/register`, request);
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

  private resolveBaseUrl(): string {
    const host = window.location.hostname;
    const isLocalHost = host === 'localhost' || host === '127.0.0.1' || host === '::1';

    if (isLocalHost) {
      return `http://${host}:5027/api`;
    }

    return '/api';
  }
}
