import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth/auth.service';
import { FeedbackModalComponent } from './core/ui/feedback-modal';
import { TranslationService } from './core/i18n/translation.service';
import { Locale, TranslationKey } from './core/i18n/translations';
import { ThemePreference, ThemeService } from './core/theme/theme.service';

/**
 * Each language's own name for itself in the switch -- fixed text, not looked up through
 * `TranslationService.t()`.
 *
 * Every other label in the shell follows the current locale, but this one specifically must not: a
 * Ukrainian speaker who lands on the application with English showing needs to recognise their own
 * language's *button* well enough to press it, and a label that itself renders in English until
 * they do ("UK") defeats the point of offering the switch in the first place.
 */
const LOCALE_NAMES: Record<Locale, string> = {
  en: 'EN',
  uk: 'УКР',
};

/** The topbar switch's label for each theme preference, for the same reason as `LOCALE_LABELS`. */
const THEME_LABELS: Record<ThemePreference, TranslationKey> = {
  auto: 'shell.theme.auto',
  light: 'shell.theme.light',
  dark: 'shell.theme.dark',
};

/** Application shell: the header, the navigation, and the routed view. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FeedbackModalComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly router = inject(Router);

  // Not `viewChild.required`: the router's first NavigationEnd can arrive before this component's
  // view has been queried, and a required query throws rather than returning undefined.
  private readonly main = viewChild<ElementRef<HTMLElement>>('main');

  private readonly feedbackModal = viewChild<FeedbackModalComponent>('feedbackModal');

  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(TranslationService);
  protected readonly theme = inject(ThemeService);

  /** The languages offered in the topbar switch, in the order they are shown. */
  protected readonly locales: readonly Locale[] = ['en', 'uk'];

  /** The theme choices offered in the topbar switch, in the order they are shown. */
  protected readonly themes: readonly ThemePreference[] = ['auto', 'light', 'dark'];

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

  /** Opens the personal notes pad. */
  protected openFeedback(): void {
    this.feedbackModal()?.open();
  }

  /** A language's own, untranslated name for the switch button. */
  protected localeName(locale: Locale): string {
    return LOCALE_NAMES[locale];
  }

  /** The translation key for a theme preference's name in the switch. */
  protected themeLabel(mode: ThemePreference): TranslationKey {
    return THEME_LABELS[mode];
  }

  /** Signs out and returns to the login page. */
  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
