import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { describeError } from '../../core/http/describe-error';

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

  /** Whether the form is creating an account rather than signing in. */
  protected readonly registering = signal(false);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly displayName = signal('');

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Switches between signing in and registering. */
  protected toggleMode(): void {
    this.registering.update((value) => !value);
    this.error.set(null);
  }

  /** Submits the form. */
  protected async submit(): Promise<void> {
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
}
