import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  const successBody = {
    accessToken: 'access-token',
    expiresAtUtc: new Date().toISOString(),
    fullName: 'Owner',
    email: 'owner@softwaremanagement.test',
    roles: ['Owner'],
    permissions: ['dashboard.read', 'admin.user.read'],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts signed out', () => {
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token()).toBeNull();
  });

  it('keeps the token in memory after signing in', () => {
    auth.signIn('owner@softwaremanagement.test', 'Fixture-Pass-2026').subscribe();
    http.expectOne('/api/v1/auth/login').flush(successBody);

    expect(auth.isSignedIn()).toBe(true);
    expect(auth.token()).toBe('access-token');
    expect(auth.user()?.email).toBe('owner@softwaremanagement.test');
  });

  it('never writes the token to browser storage', () => {
    auth.signIn('owner@softwaremanagement.test', 'Fixture-Pass-2026').subscribe();
    http.expectOne('/api/v1/auth/login').flush(successBody);

    // Anything a script can read, an injected script can steal.
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(sessionStorage.getItem('accessToken')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('access-token');
  });

  it('answers permission questions from the token it was given', () => {
    auth.signIn('owner@softwaremanagement.test', 'Fixture-Pass-2026').subscribe();
    http.expectOne('/api/v1/auth/login').flush(successBody);

    expect(auth.can('admin.user.read')).toBe(true);
    expect(auth.can('finance.payment.refund')).toBe(false);
  });

  it('clears everything on sign-out', () => {
    auth.signIn('owner@softwaremanagement.test', 'Fixture-Pass-2026').subscribe();
    http.expectOne('/api/v1/auth/login').flush(successBody);

    auth.signOut().subscribe();
    http.expectOne('/api/v1/auth/logout').flush(null);

    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token()).toBeNull();
    expect(auth.can('admin.user.read')).toBe(false);
  });

  it('restores a session from the refresh cookie', () => {
    auth.restore().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(successBody);

    expect(auth.isSignedIn()).toBe(true);
  });
});
