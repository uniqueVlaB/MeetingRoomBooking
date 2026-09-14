import { TestBed } from '@angular/core/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { AppTitleStrategy } from './core/ui/title-strategy';

/** Stands in for a routed screen; the shell is what is under test, not the page it frames. */
@Component({ selector: 'app-stub', template: '<h1>Stub</h1>' })
class StubComponent {}

/** A signed-in, non-administrator session, so the shell renders its navigation. */
const auth = {
  isAuthenticated: signal(true),
  isAdmin: signal(false),
  displayName: signal('Uma User'),
  logout: () => Promise.resolve(),
};

/**
 * The application shell.
 *
 * Both behaviours pinned here are invisible when they break. Focus silently stays in the header
 * after a route change, so a screen reader announces nothing and Tab carries on through navigation
 * the user has already passed; and every tab keeps whatever title the last page set, which in a
 * system meant to be used in several tabs at once leaves them indistinguishable.
 */
describe('App', () => {
  let router: Router;

  beforeEach(async () => {
    TestBed.resetTestingModule();

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([
          { path: 'rooms', title: 'Rooms', component: StubComponent },
          { path: 'my-bookings', title: 'My bookings', component: StubComponent },
          { path: 'untitled', component: StubComponent },
        ]),
        { provide: TitleStrategy, useClass: AppTitleStrategy },
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
  });

  it('moves focus into the page when the route changes', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    await router.navigateByUrl('/rooms');
    await fixture.whenStable();

    const main = fixture.nativeElement.querySelector('main');

    expect(document.activeElement).toBe(main);

    // And again on the next navigation: focus lands back in the header on every link the user
    // follows, so doing this once at start-up would fix nothing.
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a[href="/my-bookings"]');
    link.focus();

    expect(document.activeElement).toBe(link);

    await router.navigateByUrl('/my-bookings');
    await fixture.whenStable();

    expect(document.activeElement).toBe(main);
  });

  it('offers a skip link to that same target', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const skip: HTMLAnchorElement = fixture.nativeElement.querySelector('.skip-link');
    const main: HTMLElement = fixture.nativeElement.querySelector('main');

    // The link is only useful if it points at something that can hold focus, which for a <main> is
    // only true with a tabindex.
    expect(skip.getAttribute('href')).toBe('#main-content');
    expect(main.id).toBe('main-content');
    expect(main.getAttribute('tabindex')).toBe('-1');
  });

  describe('the tab title', () => {
    it('names the page and the application', async () => {
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();

      await router.navigateByUrl('/rooms');
      await fixture.whenStable();

      expect(TestBed.inject(Title).getTitle()).toBe('Rooms · Meeting Rooms');
    });

    it('falls back to the application alone rather than keeping the last page name', async () => {
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();

      await router.navigateByUrl('/rooms');
      await fixture.whenStable();

      await router.navigateByUrl('/untitled');
      await fixture.whenStable();

      // Angular's default strategy leaves the previous title in place for a route without one,
      // which would have this tab still claiming to be the room list.
      expect(TestBed.inject(Title).getTitle()).toBe('Meeting Rooms');
    });
  });
});
