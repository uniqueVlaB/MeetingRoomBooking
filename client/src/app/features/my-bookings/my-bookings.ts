import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { formatDayLong, isPastDay, relativeDay } from '../../core/format/date';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { Booking } from '../../core/models';

/** One day's worth of the user's bookings, as the list is rendered. */
interface BookingDay {
  date: string;
  /** The date written out, e.g. "Sunday, 20 September 2026". */
  label: string;
  /** "Today", "Tomorrow" or "Yesterday" where one of those applies. */
  relative: string | null;
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

    return [...grouped].map(([date, bookings]) => ({
      date,
      label: formatDayLong(date),
      relative: relativeDay(date),
      past: isPastDay(date),
      bookings,
    }));
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
      this.error.set(describeError(error));
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
      this.error.set(describeError(error));
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
