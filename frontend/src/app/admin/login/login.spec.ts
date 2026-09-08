import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const fillAndSubmit = async (email: string, password: string): Promise<void> => {
    const emailInput = element().querySelector<HTMLInputElement>('#email')!;
    const passwordInput = element().querySelector<HTMLInputElement>('#password')!;

    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));

    element().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideZonelessChangeDetection(),
        // A wildcard route so the successful-sign-in navigation resolves instead of throwing
        // NG04002 into an unhandled rejection.
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('renders the sign-in form', () => {
    expect(element().querySelector('[data-testid="admin-login"]')).not.toBeNull();
    expect(element().querySelector('#email')).not.toBeNull();
    expect(element().querySelector('#password')).not.toBeNull();
    expect(element().querySelector('h1')?.textContent).toContain('Sign in');
  });

  it('posts the credentials to the sign-in endpoint', async () => {
    await fillAndSubmit('owner@softwaremanagement.test', 'Fixture-Pass-2026');

    const request = http.expectOne('/api/v1/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.email).toBe('owner@softwaremanagement.test');

    request.flush({
      accessToken: 'token',
      expiresAtUtc: new Date().toISOString(),
      fullName: 'Owner',
      email: 'owner@softwaremanagement.test',
      roles: ['Owner'],
      permissions: ['dashboard.read'],
    });
    await fixture.whenStable();
  });

  it('shows one message for a wrong password that does not say which half was wrong', async () => {
    await fillAndSubmit('owner@softwaremanagement.test', 'wrong-password');

    http.expectOne('/api/v1/auth/login').flush(
      { title: 'Sign-in failed' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await fixture.whenStable();

    const error = element().querySelector('[data-testid="login-error"]');
    expect(error?.textContent?.trim()).toBe('Email or password is incorrect.');
    expect(error?.textContent).not.toContain('user');
  });

  it('explains a lockout differently from a bad password', async () => {
    await fillAndSubmit('owner@softwaremanagement.test', 'wrong-password');

    http.expectOne('/api/v1/auth/login').flush(
      { title: 'Account locked' },
      { status: 423, statusText: 'Locked' },
    );
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="login-error"]')?.textContent)
      .toContain('Too many failed attempts');
  });

  it('does not call the API when the form is empty', async () => {
    element().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectNone('/api/v1/auth/login');
    expect(element().querySelector('#email-error')?.textContent).toContain('email address');
  });

  it('navigates to the requested page after a successful sign-in', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    await fillAndSubmit('owner@softwaremanagement.test', 'Fixture-Pass-2026');
    http.expectOne('/api/v1/auth/login').flush({
      accessToken: 'token',
      expiresAtUtc: new Date().toISOString(),
      fullName: 'Owner',
      email: 'owner@softwaremanagement.test',
      roles: ['Owner'],
      permissions: ['dashboard.read'],
    });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/admin');
  });
});
