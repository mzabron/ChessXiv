import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly tokenStorageKey = 'chessxiv.auth.token';
  private readonly expiresStorageKey = 'chessxiv.auth.expiresAtUtc';

  /**
   * Guest tokens live in sessionStorage rather than localStorage: closing the tab drops
   * the token, which makes the guest's uploaded draft unreachable straight away. The
   * backend then sweeps the orphaned staging rows once they go idle.
   */
  private readonly guestTokenStorageKey = 'chessxiv.guest.token';
  private readonly guestExpiresStorageKey = 'chessxiv.guest.expiresAtUtc';

  /** The signed-in user's token when there is one, otherwise the anonymous guest token. */
  getAccessToken(): string | null {
    return localStorage.getItem(this.tokenStorageKey) ?? this.getGuestToken();
  }

  getUserAccessToken(): string | null {
    return localStorage.getItem(this.tokenStorageKey);
  }

  getGuestToken(): string | null {
    return sessionStorage.getItem(this.guestTokenStorageKey);
  }

  setGuestSession(accessToken: string, expiresAtUtc: string): void {
    sessionStorage.setItem(this.guestTokenStorageKey, accessToken);
    sessionStorage.setItem(this.guestExpiresStorageKey, expiresAtUtc);
  }

  clearGuestSession(): void {
    sessionStorage.removeItem(this.guestTokenStorageKey);
    sessionStorage.removeItem(this.guestExpiresStorageKey);
  }

  hasValidGuestSession(now: Date = new Date()): boolean {
    return AuthSessionService.isUnexpired(
      this.getGuestToken(),
      sessionStorage.getItem(this.guestExpiresStorageKey),
      now
    );
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
    return AuthSessionService.isUnexpired(this.getUserAccessToken(), this.getExpiresAtUtc(), now);
  }

  private static isUnexpired(token: string | null, expiresAtUtc: string | null, now: Date): boolean {
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
