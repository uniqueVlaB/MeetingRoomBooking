import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { Booking } from '../../core/models';

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

  constructor() {
    void this.load();
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
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
