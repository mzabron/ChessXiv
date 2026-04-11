import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AccountSummary,
  ChangeAccountEmailRequest,
  ChangeAccountPasswordRequest,
  ConfirmAccountEmailChangeRequest,
  DeleteAccountRequest
} from './account.models';

@Injectable({ providedIn: 'root' })
export class AccountApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getSummary(): Observable<AccountSummary> {
    return this.http.get<AccountSummary>(`${this.baseUrl}/account/summary`);
  }

  changeEmail(request: ChangeAccountEmailRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/account/change-email`, request, {
      responseType: 'text'
    });
  }

  changePassword(request: ChangeAccountPasswordRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/account/change-password`, request, {
      responseType: 'text'
    });
  }

  deleteAccount(request: DeleteAccountRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/account/delete`, request, {
      responseType: 'text'
    });
  }

  confirmEmailChange(request: ConfirmAccountEmailChangeRequest): Observable<string> {
    return this.http.post(`${this.baseUrl}/account/confirm-email-change`, request, {
      responseType: 'text'
    });
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
