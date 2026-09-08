import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, SignInResult } from '../../core/auth/auth.service';
import { AdminShell } from './admin-shell';

describe('AdminShell', () => {
  let fixture: ComponentFixture<AdminShell>;
  let auth: AuthService;

  const element = (): HTMLElement => fixture.nativeElement as HTMLElement;

  const signInAs = (permissions: string[]): void => {
    const result: SignInResult = {
      accessToken: 'token',
      expiresAtUtc: new Date().toISOString(),
      fullName: 'Test Person',
      email: 'test@softwaremanagement.test',
      roles: ['Owner'],
      permissions,
    };

    // The service accepts a result the same way it does after a real sign-in.
    (auth as unknown as { accept(r: SignInResult): void })['accept'](result);
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminShell],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    auth = TestBed.inject(AuthService);
    fixture = TestBed.createComponent(AdminShell);
  });

  it('renders the admin frame', async () => {
    signInAs(['dashboard.read']);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="admin-shell"]')).not.toBeNull();
  });

  it('names the signed-in person', async () => {
    signInAs(['dashboard.read']);
    await fixture.whenStable();

    expect(element().querySelector('[data-testid="signed-in-as"]')?.textContent).toContain('Test Person');
  });

  it('hides navigation the permissions do not allow', async () => {
    signInAs(['dashboard.read']);
    await fixture.whenStable();

    const links = [...element().querySelectorAll('.admin__nav a')].map((a) => a.textContent?.trim());

    expect(links).toContain('Dashboard');
    expect(links).not.toContain('Users');
    expect(links).not.toContain('Sign-in log');
  });

  it('shows the administration links to someone who holds those permissions', async () => {
    signInAs(['dashboard.read', 'admin.user.read', 'admin.audit.read']);
    await fixture.whenStable();

    const links = [...element().querySelectorAll('.admin__nav a')].map((a) => a.textContent?.trim());

    expect(links).toContain('Users');
    expect(links).toContain('Sign-in log');
  });

  it('offers a sign-out control', async () => {
    signInAs(['dashboard.read']);
    await fixture.whenStable();

    const button = element().querySelector('.admin__user button');

    expect(button?.textContent?.trim()).toBe('Sign out');
  });
});
