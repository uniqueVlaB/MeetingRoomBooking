import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { Booking, Room, TimeSlotRequest } from '../../core/models';
import { Notice, failure, info } from '../../core/ui/notice';

/**
 * Administration: manage the room catalogue and see every user's bookings.
 *
 * Slots are generated from an opening hour, a closing hour and a length, rather than entered one by
 * one. It is the shape a meeting room actually has, and it makes the overlapping-slot case the
 * server rejects hard to produce by accident.
 */
@Component({
  selector: 'app-admin',
  imports: [FormsModule, RouterLink],
  templateUrl: './admin.html',
  styleUrl: './admin.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminComponent {
  private readonly roomsApi = inject(RoomsService);
  private readonly bookingsApi = inject(BookingsService);

  protected readonly rooms = signal<Room[]>([]);
  protected readonly bookings = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  /**
   * Whether the last load failed.
   *
   * Both panels render from lists that are empty before a load and empty after a failed one. Told
   * apart here so a failure does not present itself as "No rooms yet." to an administrator whose
   * catalogue is full.
   */
  protected readonly loadFailed = signal(false);

  protected readonly name = signal('');
  protected readonly location = signal('');
  protected readonly capacity = signal(6);
  protected readonly openHour = signal(9);
  protected readonly closeHour = signal(17);
  protected readonly slotMinutes = signal(60);

  constructor() {
    void this.load();
  }

  /** Formats "09:00:00" as "09:00". */
  protected formatTime(time: string): string {
    return formatSlotTime(time);
  }

  /** Creates a room from the form. */
  protected async createRoom(): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      const room = await this.roomsApi.createRoom({
        name: this.name(),
        location: this.location() || null,
        capacity: this.capacity(),
        timeSlots: this.buildSlots(),
      });

      this.rooms.update((current) =>
        [...current, room].sort((a, b) => a.name.localeCompare(b.name)),
      );
      this.notice.set(info(`Created ${room.name}.`));
      this.name.set('');
      this.location.set('');
    } catch (error) {
      this.notice.set(failure(describeError(error)));
    } finally {
      this.busy.set(false);
    }
  }

  /** Retires or restores a room. */
  protected async toggleActive(room: Room): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      const updated = await this.roomsApi.updateRoom(room.id, {
        name: room.name,
        location: room.location,
        capacity: room.capacity,
        isActive: !room.isActive,
      });

      this.rooms.update((current) =>
        current.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
      );
    } catch (error) {
      this.notice.set(failure(describeError(error)));
    } finally {
      this.busy.set(false);
    }
  }

  /** Deletes a room, or retires it if it carries booking history. */
  protected async deleteRoom(room: Room): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      await this.roomsApi.deleteRoom(room.id);
      await this.load();

      // The server decides between deleting and retiring, and answers 204 either way, so the
      // reloaded list is what says which happened.
      const retired = this.rooms().some((candidate) => candidate.id === room.id);

      this.notice.set(
        info(
          retired
            ? `Retired ${room.name}. It has bookings, so the room is kept for their history and simply accepts no new ones.`
            : `Removed ${room.name}.`,
        ),
      );
    } catch (error) {
      this.notice.set(failure(describeError(error)));
    } finally {
      this.busy.set(false);
    }
  }

  /** Cancels somebody else's booking. */
  protected async cancelBooking(booking: Booking): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      await this.bookingsApi.cancel(booking.id);
      this.bookings.update((current) => current.filter((candidate) => candidate.id !== booking.id));
    } catch (error) {
      this.notice.set(failure(describeError(error)));
    } finally {
      this.busy.set(false);
    }
  }

  /** Expands the opening hours into a list of slots. */
  private buildSlots(): TimeSlotRequest[] {
    const slots: TimeSlotRequest[] = [];
    const step = this.slotMinutes();

    for (
      let minutes = this.openHour() * 60;
      minutes + step <= this.closeHour() * 60;
      minutes += step
    ) {
      slots.push({ startTime: toTime(minutes), endTime: toTime(minutes + step) });
    }

    return slots;
  }

  /** Loads rooms and bookings. */
  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const [rooms, bookings] = await Promise.all([
        this.roomsApi.listRooms(),
        this.bookingsApi.listAll(),
      ]);

      this.rooms.set(rooms);
      this.bookings.set(bookings);
      this.loadFailed.set(false);
    } catch (error) {
      this.notice.set(failure(describeError(error)));
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /** Reloads rooms and bookings after a failed load. */
  protected async retry(): Promise<void> {
    this.notice.set(null);
    await this.load();
  }
}

/** Formats minutes-since-midnight as the "HH:mm:ss" the API expects for a TimeOnly. */
function toTime(minutes: number): string {
  const hours = `${Math.floor(minutes / 60)}`.padStart(2, '0');
  const remainder = `${minutes % 60}`.padStart(2, '0');

  return `${hours}:${remainder}:00`;
}
