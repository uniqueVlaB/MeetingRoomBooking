import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** The suffix every page carries, so a tab is identifiable when its title is truncated. */
const APP_NAME = 'Meeting Rooms';

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
 */
@Injectable()
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  /** Sets the document title from the route that has just activated. */
  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);

    this.title.setTitle(page ? `${page} · ${APP_NAME}` : APP_NAME);
  }
}
