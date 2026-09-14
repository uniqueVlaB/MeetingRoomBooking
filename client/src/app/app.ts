import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth/auth.service';

/** Application shell: the header, the navigation, and the routed view. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly router = inject(Router);

  // Not `viewChild.required`: the router's first NavigationEnd can arrive before this component's
  // view has been queried, and a required query throws rather than returning undefined.
  private readonly main = viewChild<ElementRef<HTMLElement>>('main');

  protected readonly auth = inject(AuthService);

  constructor() {
    // Changing route in a single-page application replaces the content but leaves focus wherever
    // the user left it -- usually on the link they just followed, in a header that is still there.
    // A screen reader therefore announces nothing, and the next Tab continues through the header
    // rather than into the page that just arrived.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.main()?.nativeElement.focus({ preventScroll: true }));
  }

  /** Signs out and returns to the login page. */
  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
