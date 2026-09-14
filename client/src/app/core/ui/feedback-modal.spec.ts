import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { beforeEach, describe, expect, it } from 'vitest';
import { FeedbackModalComponent } from './feedback-modal';
import { REVIEWER_NOTES } from './reviewer-notes';

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  notes: string;
  open(): void;
  close(): void;
}

/**
 * The read-only note for a reviewer, opened from the red button in the topbar.
 *
 * There is no input here to test -- the text is baked in at `reviewer-notes.ts` -- so what is worth
 * pinning down is that the dialog actually shows that text, and opens and closes correctly as a
 * native modal.
 */
describe('FeedbackModalComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  it('shows the reviewer notes written into the source', async () => {
    const fixture = TestBed.createComponent(FeedbackModalComponent);
    const component = fixture.componentInstance as unknown as Internals;
    await fixture.whenStable();

    expect(component.notes).toBe(REVIEWER_NOTES);
    expect(fixture.nativeElement.textContent).toContain(REVIEWER_NOTES);
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
