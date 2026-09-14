import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { LoginRequest, RegisterRequest } from '../../core/models';
import { LoginComponent } from './login';

/** An auth client that records what reached it, so a test can prove nothing did. */
class FakeAuthService {
  readonly logins: LoginRequest[] = [];
  readonly registrations: RegisterRequest[] = [];

  login(request: LoginRequest): Promise<void> {
    this.logins.push(request);

    return Promise.resolve();
  }

  register(request: RegisterRequest): Promise<void> {
    this.registrations.push(request);

    return Promise.resolve();
  }
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  email: { set(value: string): void };
  password: { set(value: string): void };
  displayName: { set(value: string): void };
  registering: () => boolean;
  showPassword: () => boolean;
  error: () => string | null;
  submit: () => Promise<void>;
  toggleMode: () => void;
  togglePasswordVisibility: () => void;
}

/**
 * The sign-in form.
 *
 * Its `required` and `minlength` attributes were decoration: FormsModule marks the form
 * `novalidate`, and no code here read the form's validity, so an empty form went to the server and
 * came back as a 400 in the server's own words. What is pinned here is that an obviously incomplete
 * form is answered immediately and never sent -- and, just as importantly, that the form does not
 * start refusing things the server would have accepted.
 */
describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: Internals;
  let auth: FakeAuthService;

  beforeEach(async () => {
    auth = new FakeAuthService();

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideZonelessChangeDetection(),

        // The component navigates to /rooms on success.
        provideRouter([{ path: 'rooms', children: [] }]),
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance as unknown as Internals;

    await fixture.whenStable();
  });

  /** Fills the sign-in fields with values the server would accept. */
  function fillValidCredentials(): void {
    component.email.set('uma@example.com');
    component.password.set('correct horse');
  }

  describe('before anything is sent', () => {
    it('answers an empty form without asking the server', async () => {
      await component.submit();

      expect(auth.logins).toEqual([]);
      expect(component.error()).not.toBeNull();
    });

    it('rejects an address with no @ in it', async () => {
      component.email.set('uma.example.com');
      component.password.set('correct horse');

      await component.submit();

      expect(auth.logins).toEqual([]);
    });

    it('asks for a password when one is missing', async () => {
      component.email.set('uma@example.com');

      await component.submit();

      expect(auth.logins).toEqual([]);
      expect(component.error()).toContain('password');
    });

    it('states the length a new password has to reach', async () => {
      component.toggleMode();
      component.displayName.set('Uma User');
      component.email.set('uma@example.com');
      component.password.set('short');

      await component.submit();

      // The server's rule, repeated rather than invented: mirroring it is what makes the message
      // specific, and the number has to be the server's or the form starts lying.
      expect(auth.registrations).toEqual([]);
      expect(component.error()).toContain('8');
    });

    it('asks for a display name when registering', async () => {
      component.toggleMode();
      component.email.set('uma@example.com');
      component.password.set('correct horse');

      await component.submit();

      expect(auth.registrations).toEqual([]);
      expect(component.error()).toContain('display name');
    });

    it('does not apply the new-password rule when signing in', async () => {
      // An existing account may predate the rule, and refusing to even try would lock its owner out
      // of an account the server would have let them into.
      component.email.set('uma@example.com');
      component.password.set('old');

      await component.submit();

      expect(auth.logins).toHaveLength(1);
    });
  });

  describe('once the form is complete', () => {
    it('signs in', async () => {
      fillValidCredentials();

      await component.submit();

      expect(auth.logins).toEqual([{ email: 'uma@example.com', password: 'correct horse' }]);
      expect(component.error()).toBeNull();
    });

    it('registers', async () => {
      component.toggleMode();
      component.displayName.set('Uma User');
      fillValidCredentials();

      await component.submit();

      expect(auth.registrations).toHaveLength(1);
      expect(auth.logins).toEqual([]);
    });
  });

  describe('the password field', () => {
    /** The password input, whose type is what the reveal button changes. */
    function passwordInput(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[name="password"]');
    }

    it('is masked to begin with', () => {
      expect(passwordInput().type).toBe('password');
      expect(component.showPassword()).toBe(false);
    });

    it('can be revealed and masked again', async () => {
      component.togglePasswordVisibility();
      await fixture.whenStable();

      // A field nobody can read is where a typo survives, and the answer it earns is a deliberately
      // unhelpful "email or password is incorrect".
      expect(passwordInput().type).toBe('text');

      component.togglePasswordVisibility();
      await fixture.whenStable();

      expect(passwordInput().type).toBe('password');
    });
  });
});
