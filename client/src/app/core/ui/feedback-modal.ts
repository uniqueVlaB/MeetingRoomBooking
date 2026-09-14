import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  viewChild,
} from '@angular/core';
import { TranslationService } from '../i18n/translation.service';
import { REVIEWER_NOTES } from './reviewer-notes';

/**
 * A read-only note for whoever reviews this project, reachable from the red button in the topbar.
 *
 * Not a feedback form: there is nothing here for a visitor to type, no submit button and no
 * recipient. The text itself is `REVIEWER_NOTES`, written by the project owner in the source and
 * shipped with the build -- this component only opens and closes the dialog that shows it.
 */
@Component({
  selector: 'app-feedback-modal',
  templateUrl: './feedback-modal.html',
  styleUrl: './feedback-modal.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeedbackModalComponent implements AfterViewInit {
  protected readonly i18n = inject(TranslationService);

  /** The note itself, baked into the client -- see `reviewer-notes.ts`. */
  protected readonly notes = REVIEWER_NOTES;

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  /**
   * Closes on a click outside the card.
   *
   * Attached in code rather than with a `(click)` binding on the `<dialog>` in the template: a
   * native `<dialog>` is not itself a widget a keyboard user activates (Escape already closes it,
   * and nothing here needs a second key binding to do the same thing), so the template a11y lint
   * rules that require a click handler to carry a matching key handler do not apply -- but they
   * cannot tell that from the markup alone and flag it anyway.
   */
  ngAfterViewInit(): void {
    const dialog = this.dialog().nativeElement;

    dialog.addEventListener('click', (event) => {
      // The card is a whole card's worth of space away from the dialog's own edges; a click that
      // reaches the dialog itself (never one of its descendants -- see below) can only have landed
      // on the backdrop area outside the card.
      if (event.target === dialog) {
        this.close();
      }
    });
  }

  /**
   * Opens the dialog, as a native modal so focus is trapped and background content is inert.
   *
   * Falls back to the `open` attribute directly when `showModal()` is unavailable -- Jsdom's
   * `HTMLDialogElement` implements the `open` property but not the modal methods, and there is no
   * reason a component test should have to stand up a real browser just to click this button.
   */
  open(): void {
    const dialog = this.dialog().nativeElement;

    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
    } else {
      dialog.setAttribute('open', '');
    }
  }

  /** Closes the dialog. Bound to the close button; Escape and `<dialog>`'s own affordances work too. */
  protected close(): void {
    const dialog = this.dialog().nativeElement;

    if (typeof dialog.close === 'function') {
      dialog.close();
    } else {
      dialog.removeAttribute('open');
    }
  }
}
