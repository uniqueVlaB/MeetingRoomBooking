import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { formatDayLong, isPastDay, relativeDay } from '../../core/format/date';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { TranslationService } from '../../core/i18n/translation.service';
import { TranslationKey } from '../../core/i18n/translations';
import { Booking } from '../../core/models';

/** The translation key for each day-relative code `relativeDay()` can return. */
const RELATIVE_DAY_LABELS: Record<'today' | 'tomorrow' | 'yesterday', TranslationKey> = {
  today: 'date.today',
  tomorrow: 'date.tomorrow',
  yesterday: 'date.yesterday',
};

/** One day's worth of the user's bookings, as the list is rendered. */
interface BookingDay {
  date: string;
  /** The date written out, e.g. "Sunday, 20 September 2026". */
  label: string;
  /** The translation key for "Today"/"Tomorrow"/"Yesterday" where one of those applies. */
  relative: TranslationKey | null;
  /** Whether "today" is the relative label, so the chip can be highlighted like elsewhere in the app. */
  isToday: boolean;
  /** Whether the day has gone, so it can be shown as a record rather than as something upcoming. */
  past: boolean;
  bookings: Booking[];
}

/** The signed-in user's own bookings, with the option to release one. */
@Component({
  selector: 'app-my-bookings',
  imports: [RouterLink],
  templateUrl: './my-bookings.html',
  styleUrl: './my-bookings.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyBookingsComponent {
  private readonly bookingsApi = inject(BookingsService);

  protected readonly i18n = inject(TranslationService);

  protected readonly bookings = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly pendingBookingId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  /**
   * Whether the last load failed.
   *
   * An empty list and a failed request are the same value here, and they mean opposite things.
   * Without this the screen answers a network failure with "You have no bookings", which is both
   * wrong and alarming to somebody who knows they have several.
   */
  protected readonly loadFailed = signal(false);

  /** Placeholder rows drawn while the list loads. */
  protected readonly skeletonRows = [0, 1, 2];

  /**
   * The bookings grouped under the day they fall on.
   *
   * A flat list repeated the date on every row in the API's own format -- "2026-09-20" eight times
   * down the page -- which is both the least readable way to write a date and the least useful
   * place to put it. The server already returns them in date order, so grouping only has to respect
   * the order it was given.
   */
  protected readonly days = computed<BookingDay[]>(() => {
    const grouped = new Map<string, Booking[]>();

    for (const booking of this.bookings()) {
      const day = grouped.get(booking.slotDate);

      if (day) {
        day.push(booking);
      } else {
        grouped.set(booking.slotDate, [booking]);
      }
    }

    const locale = this.i18n.dateLocale();

    return [...grouped].map(([date, bookings]) => {
      const code = relativeDay(date);

      return {
        date,
        label: formatDayLong(date, locale),
        relative: code ? RELATIVE_DAY_LABELS[code] : null,
        isToday: code === 'today',
        past: isPastDay(date),
        bookings,
      };
    });
  });

  /**
   * The subtitle summarising how many slots the user holds, across how many days.
   *
   * Composed from two independently-pluralised phrases rather than one template, because "slots"
   * agrees with the slot count and "days" agrees with the day count -- two different numbers, each
   * needing its own grammatical form (Ukrainian more visibly than English, with three forms rather
   * than two).
   */
  protected readonly subtitle = computed(() => {
    if (this.loading() || this.loadFailed() || this.bookings().length === 0) {
      return this.i18n.t('myBookings.subtitleDefault');
    }

    return this.i18n.t('myBookings.subtitle', {
      slots: this.i18n.plural('myBookings.slotsPhrase', this.bookings().length),
      days: this.i18n.plural('myBookings.daysPhrase', this.days().length),
    });
  });

  constructor() {
    void this.load();
  }

  /** Reloads after a failed load. */
  protected async retry(): Promise<void> {
    this.error.set(null);
    await this.load();
  }

  /** Formats "09:00:00" as "09:00". */
  protected formatTime(time: string): string {
    return formatSlotTime(time);
  }

  /** Cancels a booking and drops it from the list. */
  protected async cancel(booking: Booking): Promise<void> {
    this.pendingBookingId.set(booking.id);
    this.error.set(null);

    try {
      await this.bookingsApi.cancel(booking.id);
      this.bookings.update((current) => current.filter((candidate) => candidate.id !== booking.id));
    } catch (error) {
      // The server's own explanation: "that booking has already been cancelled" and "you can only
      // cancel your own bookings" are different problems with different answers.
      this.error.set(describeError(error, (key, params) => this.i18n.t(key, params)));
      await this.load();
    } finally {
      this.pendingBookingId.set(null);
    }
  }

  /** Loads the user's bookings. */
  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.bookings.set(await this.bookingsApi.listMine());
      this.loadFailed.set(false);
    } catch (error) {
      this.error.set(describeError(error, (key, params) => this.i18n.t(key, params)));
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
