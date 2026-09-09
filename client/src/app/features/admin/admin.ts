import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { Booking, Room, TimeSlotRequest } from '../../core/models';

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
  private readonly rooms = inject(RoomsService);
  private readonly bookings = inject(BookingsService);

  protected readonly roomList = signal<Room[]>([]);
  protected readonly allBookings = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<{ kind: 'info' | 'error'; text: string } | null>(null);

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
    return time.slice(0, 5);
  }

  /** Creates a room from the form. */
  protected async createRoom(): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      const room = await this.rooms.createRoom({
        name: this.name(),
        location: this.location() || null,
        capacity: this.capacity(),
        timeSlots: this.buildSlots(),
      });

      this.roomList.update((current) => [...current, room].sort((a, b) => a.name.localeCompare(b.name)));
      this.notice.set({ kind: 'info', text: `Created ${room.name}.` });
      this.name.set('');
      this.location.set('');
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.busy.set(false);
    }
  }

  /** Retires or restores a room. */
  protected async toggleActive(room: Room): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      const updated = await this.rooms.updateRoom(room.id, {
        name: room.name,
        location: room.location,
        capacity: room.capacity,
        isActive: !room.isActive,
      });

      this.roomList.update((current) =>
        current.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
      );
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.busy.set(false);
    }
  }

  /** Deletes a room, or retires it if it carries booking history. */
  protected async deleteRoom(room: Room): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);

    try {
      await this.rooms.deleteRoom(room.id);
      await this.load();
      this.notice.set({
        kind: 'info',
        text: `Removed ${room.name}. Rooms with bookings are retired rather than deleted, so their history survives.`,
      });
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.busy.set(false);
    }
  }

  /** Cancels somebody else's booking. */
  protected async cancelBooking(booking: Booking): Promise<void> {
    this.busy.set(true);

    try {
      await this.bookings.cancel(booking.id);
      this.allBookings.update((current) => current.filter((candidate) => candidate.id !== booking.id));
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.busy.set(false);
    }
  }

  /** Expands the opening hours into a list of slots. */
  private buildSlots(): TimeSlotRequest[] {
    const slots: TimeSlotRequest[] = [];
    const step = this.slotMinutes();

    for (let minutes = this.openHour() * 60; minutes + step <= this.closeHour() * 60; minutes += step) {
      slots.push({ startTime: toTime(minutes), endTime: toTime(minutes + step) });
    }

    return slots;
  }

  /** Loads rooms and bookings. */
  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const [rooms, bookings] = await Promise.all([
        this.rooms.listRooms(),
        this.bookings.listAll(),
      ]);

      this.roomList.set(rooms);
      this.allBookings.set(bookings);
    } catch (error) {
      this.notice.set({ kind: 'error', text: describeError(error) });
    } finally {
      this.loading.set(false);
    }
  }
}

/** Formats minutes-since-midnight as the "HH:mm:ss" the API expects for a TimeOnly. */
function toTime(minutes: number): string {
  const hours = `${Math.floor(minutes / 60)}`.padStart(2, '0');
  const remainder = `${minutes % 60}`.padStart(2, '0');

  return `${hours}:${remainder}:00`;
}

/** Turns an error into something worth showing a user. */
function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    return error.error?.detail ?? error.error?.title ?? `Request failed (${error.status}).`;
  }

  return 'Something went wrong. Please try again.';
}
