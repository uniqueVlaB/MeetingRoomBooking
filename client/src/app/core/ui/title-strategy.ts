import { Injectable, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslationKey } from '../i18n/translations';
import { TranslationService } from '../i18n/translation.service';

/** The suffix every page carries, so a tab is identifiable when its title is truncated. */
const APP_NAME_KEY: TranslationKey = 'shell.brand';

/**
 * Names the browser tab after the page being shown.
 *
 * Angular's default strategy sets the route's title verbatim, which would leave every tab reading
 * "Rooms" or "Admin" with nothing saying which application they belong to; a route with no title
 * would leave the previous page's name in place. This appends the application name and falls back
 * to it alone.
 *
 * It matters more here than in most applications: the point of the system is several people --
 * often several tabs -- watching the same schedule at once, and before this they were all called
 * "Meeting Rooms".
 *
 * A route's `title` is a `TranslationKey` (see `app.routes.ts`), not display text, so this
 * translates it rather than setting it verbatim -- and re-runs whenever the language changes, not
 * only on navigation, so switching languages retitles the tab the user is already sitting on rather
 * than waiting for the next link they follow.
 */
@Injectable()
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(TranslationService);

  /** The most recent snapshot, kept so a locale change alone can redo the title without a navigation. */
  private snapshot: RouterStateSnapshot | undefined;

  constructor() {
    super();

    effect(() => {
      // Read, not just called: this is what makes the effect re-run on every locale change.
      this.i18n.locale();

      if (this.snapshot) {
        this.apply(this.snapshot);
      }
    });
  }

  /** Sets the document title from the route that has just activated. */
  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.snapshot = snapshot;
    this.apply(snapshot);
  }

  private apply(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot) as TranslationKey | undefined;
    const appName = this.i18n.t(APP_NAME_KEY);

    this.title.setTitle(key ? `${this.i18n.t(key)} · ${appName}` : appName);
  }
}
