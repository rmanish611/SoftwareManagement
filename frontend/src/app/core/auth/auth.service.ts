import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

/** What the API returns on a successful sign-in. The refresh token stays in an HttpOnly cookie. */
export interface SignInResult {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
  readonly fullName: string;
  readonly email: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export interface SignedInUser {
  readonly fullName: string;
  readonly email: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

/**
 * Holds the access token for the lifetime of the tab and nothing longer.
 *
 * The token is deliberately not written to localStorage: anything a script on the page can read,
 * an injected script can steal. The refresh token lives in an HttpOnly cookie the browser sends
 * back on its own, so a reload restores the session without the application ever handling it.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessToken = signal<string | null>(null);
  private readonly currentUser = signal<SignedInUser | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isSignedIn = computed(() => this.currentUser() !== null);

  token(): string | null {
    return this.accessToken();
  }

  /** True when the signed-in user holds the permission. The server decides; this only hides UI. */
  can(permission: string): boolean {
    return this.currentUser()?.permissions.includes(permission) ?? false;
  }

  signIn(email: string, password: string, twoFactorCode?: string): Observable<SignInResult> {
    return this.http
      .post<SignInResult>('/api/v1/auth/login', { email, password, twoFactorCode: twoFactorCode ?? null })
      .pipe(tap((result) => this.accept(result)));
  }

  /** Exchanges the refresh cookie for a new access token, used on a page reload. */
  restore(): Observable<SignInResult> {
    return this.http.post<SignInResult>('/api/v1/auth/refresh', {}).pipe(tap((result) => this.accept(result)));
  }

  signOut(): Observable<void> {
    return this.http.post<void>('/api/v1/auth/logout', {}).pipe(tap(() => this.clear()));
  }

  private accept(result: SignInResult): void {
    this.accessToken.set(result.accessToken);
    this.currentUser.set({
      fullName: result.fullName,
      email: result.email,
      roles: result.roles,
      permissions: result.permissions,
    });
  }

  private clear(): void {
    this.accessToken.set(null);
    this.currentUser.set(null);
  }
}
