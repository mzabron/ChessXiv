import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly tokenStorageKey = 'chessxiv.auth.token';
  private readonly expiresStorageKey = 'chessxiv.auth.expiresAtUtc';

  getAccessToken(): string | null {
    return localStorage.getItem(this.tokenStorageKey);
  }

  getExpiresAtUtc(): string | null {
    return localStorage.getItem(this.expiresStorageKey);
  }

  setSession(accessToken: string, expiresAtUtc: string): void {
    localStorage.setItem(this.tokenStorageKey, accessToken);
    localStorage.setItem(this.expiresStorageKey, expiresAtUtc);
  }

  clearSession(): void {
    localStorage.removeItem(this.tokenStorageKey);
    localStorage.removeItem(this.expiresStorageKey);
  }

  hasValidSession(now: Date = new Date()): boolean {
    const token = this.getAccessToken();
    const expiresAtUtc = this.getExpiresAtUtc();

    if (!token || !expiresAtUtc) {
      return false;
    }

    const expiry = new Date(expiresAtUtc);
    if (Number.isNaN(expiry.getTime())) {
      return false;
    }

    return expiry.getTime() > now.getTime();
  }
}
