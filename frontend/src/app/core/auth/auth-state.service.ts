import { Injectable, computed, signal } from '@angular/core';
import { jwtDecode } from 'jwt-decode';
import { Observable, firstValueFrom, from, map, switchMap } from 'rxjs';
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

  /**
   * Guests need a bearer token too: uploading and exploring a PGN happens against the
   * same staging endpoints a signed-in user hits, keyed by the token's subject. The token
   * is refused by every endpoint that writes durable data, so a guest can browse their
   * import but not save it.
   */
  async ensureGuestSession(): Promise<void> {
    if (this.isAuthenticated() || this.sessionService.hasValidGuestSession()) {
      return;
    }

    try {
      const response = await firstValueFrom(this.authApi.createGuestSession());
      this.sessionService.setGuestSession(response.accessToken, response.expiresAtUtc);
    } catch {
      // Without a guest token the explorer still works read-only against public data.
      this.sessionService.clearGuestSession();
    }
  }

  login(request: AuthLoginRequest): Observable<AuthUser> {
    return this.authApi.login(request).pipe(
      switchMap(response => from(this.applyTokenResponse(response)))
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
      switchMap(response => from(this.applyTokenResponse(response)))
    );
  }

  logout(): void {
    this.sessionService.clearSession();
    // A signed-in user's draft belongs to their account, not to the guest that follows.
    this.sessionService.clearGuestSession();
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

  /**
   * Applies a freshly issued user token and, when the caller arrived as a guest, rescues
   * their in-progress draft before the guest identity becomes unreachable.
   *
   * A guest's staging games are keyed by their throwaway token's subject; once that token
   * is gone, nobody can ever present it again, so the rows become permanently invisible to
   * everyone while still occupying storage. Discarding the guest token here used to happen
   * immediately, before the caller could possibly do anything with it - this is the one
   * point in the app where it is still usable, so the claim call happens here, in this
   * order: the new session is set first (the claim endpoint requires it), the claim is
   * awaited so the migrated rows exist before anything reloads the draft list, and only
   * then does isAuthenticated() flip - which is what triggers that reload.
   */
  private async applyTokenResponse(response: AuthTokenResponse): Promise<AuthUser> {
    const guestToken = this.sessionService.getGuestToken();

    this.sessionService.setSession(response.accessToken, response.expiresAtUtc);

    const user = this.decodeUser(response.accessToken);
    if (!user) {
      this.sessionService.clearSession();
      throw new Error('Invalid access token payload.');
    }

    if (guestToken) {
      try {
        await firstValueFrom(this.authApi.claimGuestDraft(guestToken));
      } catch {
        // Best effort: an unclaimed guest draft is not lost, it just ages out via the
        // normal idle-based staging cleanup like any other abandoned draft.
      }
    }

    this.sessionService.clearGuestSession();
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
