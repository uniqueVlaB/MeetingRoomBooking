import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslationService } from '../i18n/translation.service';

/** Where the note is kept. Never sent anywhere -- this is the user's own scratchpad, not a report. */
const STORAGE_KEY = 'mrb-feedback-notes';

/**
 * A personal notes pad, reachable from the topbar, for the person building this project to jot
 * down their own running thoughts on how it is going.
 *
 * It is intentionally not a feedback form: nothing here is sent to a server, there is no submit
 * button and no recipient. It autosaves to this browser's storage as the user types, the same way a
 * text editor's buffer would, so the one and only cost of writing something down is closing the
 * dialog.
 */
@Component({
  selector: 'app-feedback-modal',
  imports: [FormsModule],
  templateUrl: './feedback-modal.html',
  styleUrl: './feedback-modal.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeedbackModalComponent implements AfterViewInit {
  protected readonly i18n = inject(TranslationService);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  /** The note's text, kept in memory and mirrored to storage on every change. */
  protected readonly text = signal(restoreText());

  constructor() {
    effect(() => {
      const text = this.text();

      try {
        localStorage.setItem(STORAGE_KEY, text);
      } catch {
        // Private browsing, or storage disabled outright. The note just does not outlive the tab.
      }
    });
  }

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

/** Restores a note left on a previous visit, or an empty pad on a first one. */
function restoreText(): string {
  try {
    return localStorage.getItem(STORAGE_KEY) ?? '';
  } catch {
    return '';
  }
}
