import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FeedbackModalComponent } from './feedback-modal';

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  text: { (): string; set(value: string): void };
  open(): void;
  close(): void;
}

/**
 * The personal notes pad in the topbar.
 *
 * Nothing here talks to a server -- the two things worth pinning down are that a note survives a
 * reload (it is written to storage as the user types) and that it never leaks between two different
 * component instances by way of a stale signal default.
 */
describe('FeedbackModalComponent', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('starts empty when there is no earlier note', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();

    expect(component.text()).toBe('');
  });

  it('opens the dialog and shows it as open', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();

    component.open();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialog.open).toBe(true);
  });

  it('closes on request', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();

    component.open();
    component.close();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialog.open).toBe(false);
  });

  it('saves what is typed, so a later visit restores it', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();

    component.text.set('Concurrency section could use a diagram.');
    await fixture.whenStable();

    expect(localStorage.getItem('mrb-feedback-notes')).toBe(
      'Concurrency section could use a diagram.',
    );

    const restored = TestBed.createComponent(FeedbackModalComponent);
    const restoredComponent = restored.componentInstance as unknown as Internals;
    await restored.whenStable();

    expect(restoredComponent.text()).toBe('Concurrency section could use a diagram.');
  });

  it('closes when a click lands on the backdrop rather than the card', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();
    component.open();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');

    // Simulate the browser's own behaviour: a click whose event.target is the <dialog> itself only
    // happens when the click lands on the backdrop, outside the card sitting on top of it -- a click
    // anywhere inside the card has that descendant as its target instead.
    const clickOnBackdrop = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(clickOnBackdrop, 'target', { value: dialog });
    dialog.dispatchEvent(clickOnBackdrop);

    expect(dialog.open).toBe(false);
  });
});
