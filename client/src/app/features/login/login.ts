import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { describeError } from '../../core/http/describe-error';

/** The shortest password the API will accept, mirrored here so the form can say so up front. */
const MIN_PASSWORD_LENGTH = 8;

/** The shortest display name the API will accept. */
const MIN_DISPLAY_NAME_LENGTH = 2;

/** Sign in, or create an account. Both paths start a session and continue to the requested page. */
@Component({
  selector: 'app-login',
  imports: [FormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** The shortest acceptable password, for the hint under the field. */
  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  /** Whether the form is creating an account rather than signing in. */
  protected readonly registering = signal(false);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly displayName = signal('');

  /**
   * Whether the password is shown as text.
   *
   * A password field the user cannot read is where typos go undetected, and the answer they get is
   * an indistinguishable "email or password is incorrect" -- which, deliberately, does not say
   * which. Being able to check what was typed is the only way out of that from this side.
   */
  protected readonly showPassword = signal(false);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Switches between signing in and registering. */
  protected toggleMode(): void {
    this.registering.update((value) => !value);
    this.error.set(null);
  }

  /** Shows or hides the password. */
  protected togglePasswordVisibility(): void {
    this.showPassword.update((value) => !value);
  }

  /** Submits the form. */
  protected async submit(): Promise<void> {
    // The template's `required` and `minlength` attributes never did anything: FormsModule marks
    // the form `novalidate` and nothing here read the form's validity, so an empty form was sent to
    // the server and came back as a 400 rendered in the server's words. Checking first turns that
    // into an immediate, specific sentence about the field that is wrong.
    const problem = this.validate();

    if (problem) {
      this.error.set(problem);
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      if (this.registering()) {
        await this.auth.register({
          email: this.email(),
          displayName: this.displayName(),
          password: this.password(),
        });
      } else {
        await this.auth.login({ email: this.email(), password: this.password() });
      }

      // Continue where the guard interrupted, or go to the room list.
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/rooms';
      await this.router.navigateByUrl(returnUrl);
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * The first thing wrong with the form, or null if there is nothing to report.
   *
   * Only the rules the server itself enforces are repeated here, so the form can never refuse
   * something the API would have accepted. The email test is deliberately barely a test -- the only
   * address that is definitely wrong is one with no `@` in it, and anything stricter starts
   * rejecting valid addresses.
   */
  private validate(): string | null {
    if (this.registering() && this.displayName().trim().length < MIN_DISPLAY_NAME_LENGTH) {
      return `Enter a display name of at least ${MIN_DISPLAY_NAME_LENGTH} characters.`;
    }

    if (!this.email().includes('@')) {
      return 'Enter an email address.';
    }

    if (this.password().length === 0) {
      return 'Enter your password.';
    }

    if (this.registering() && this.password().length < MIN_PASSWORD_LENGTH) {
      return `Choose a password of at least ${MIN_PASSWORD_LENGTH} characters.`;
    }

    return null;
  }
}
