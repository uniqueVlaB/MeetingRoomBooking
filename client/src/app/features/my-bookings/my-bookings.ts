import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
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
  private readonly bookings = inject(BookingsService);

  protected readonly list = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly pendingId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  /** Formats "09:00:00" as "09:00". */
  protected formatTime(time: string): string {
    return time.slice(0, 5);
  }

  /** Cancels a booking and drops it from the list. */
  protected async cancel(booking: Booking): Promise<void> {
    this.pendingId.set(booking.id);
    this.error.set(null);

    try {
      await this.bookings.cancel(booking.id);
      this.list.update((current) => current.filter((candidate) => candidate.id !== booking.id));
    } catch {
      this.error.set('That booking could not be cancelled. It may already be gone.');
      await this.load();
    } finally {
      this.pendingId.set(null);
    }
  }

  /** Loads the user's bookings. */
  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.list.set(await this.bookings.listMine());
    } catch {
      this.error.set('Your bookings could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
