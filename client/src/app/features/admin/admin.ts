import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { formatDayShort, relativeDay } from '../../core/format/date';
import { formatSlotTime } from '../../core/format/time';
import { describeError } from '../../core/http/describe-error';
import { TranslationService } from '../../core/i18n/translation.service';
import { TranslationKey } from '../../core/i18n/translations';
import { Booking, Room, TimeSlotRequest } from '../../core/models';
import { Notice, failure, info } from '../../core/ui/notice';

/** The translation key for each day-relative code `relativeDay()` can return. */
const RELATIVE_DAY_LABELS: Record<'today' | 'tomorrow' | 'yesterday', TranslationKey> = {
  today: 'date.today',
  tomorrow: 'date.tomorrow',
  yesterday: 'date.yesterday',
};

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

  protected readonly i18n = inject(TranslationService);

  protected readonly rooms = signal<Room[]>([]);
  protected readonly bookings = signal<Booking[]>([]);
  protected readonly loading = signal(true);
  protected readonly notice = signal<Notice | null>(null);

  /** Whether the create-room form is submitting. */
  protected readonly creating = signal(false);

  /**
   * The room or booking whose action is in flight.
   *
   * Replaces a single page-wide "busy" flag. That flag disabled every button on the screen for the
   * duration of any one of them, so retiring one room greyed out the other twenty rooms and every
   * booking underneath, and nothing said which row was actually working.
   */
  protected readonly pendingRowId = signal<string | null>(null);

  /**
   * The row whose destructive action is waiting for a second click.
   *
   * Deleting a room and cancelling another user's booking were each a single click with no way
   * back, sitting beside a button that only retires. Asking again in place -- rather than through
   * `confirm()` -- keeps the question next to the row it is about, and keeps the user's focus on
   * the button they pressed instead of handing it to a dialog and taking it away again.
   */
  protected readonly confirmingId = signal<string | null>(null);

  /**
   * Whether the last load failed.
   *
   * Both panels render from lists that are empty before a load and empty after a failed one. Told
   * apart here so a failure does not present itself as "No rooms yet." to an administrator whose
   * catalogue is full.
   */
  protected readonly loadFailed = signal(false);

  /** Placeholder rows drawn while a panel loads. */
  protected readonly skeletonRows = [0, 1, 2];

  protected readonly name = signal('');
  protected readonly location = signal('');
  protected readonly capacity = signal(6);
  protected readonly openHour = signal(9);
  protected readonly closeHour = signal(17);
  protected readonly slotMinutes = signal(60);

  /**
   * The slots the current form values would create.
   *
   * Shown before submitting, because the three numbers that produce them do not obviously say how
   * many slots come out -- and a closing hour at or before the opening one silently produces none,
   * which the server then rejects as a validation error after a round trip.
   */
  protected readonly slotPreview = computed(() => this.buildSlots());

  /** Whether the form is complete enough to submit. */
  protected readonly canCreate = computed(
    () => this.name().trim().length > 0 && this.slotPreview().length > 0,
  );

  constructor() {
    void this.load();
  }

  /** Formats "09:00:00" as "09:00". */
  protected formatTime(time: string): string {
    return formatSlotTime(time);
  }

  /** Formats "2026-09-20" as "Today" where that applies, and "Sun 20 Sept" otherwise. */
  protected formatDate(date: string): string {
    const code = relativeDay(date);

    return code
      ? this.i18n.t(RELATIVE_DAY_LABELS[code])
      : formatDayShort(date, this.i18n.dateLocale());
  }

  /** Whether a row has a destructive action awaiting confirmation. */
  protected isConfirming(id: string): boolean {
    return this.confirmingId() === id;
  }

  /** Whether a row's action is in flight. */
  protected isPending(id: string): boolean {
    return this.pendingRowId() === id;
  }

  /** Asks again before a destructive action, or abandons the question. */
  protected toggleConfirm(id: string): void {
    this.confirmingId.update((current) => (current === id ? null : id));
  }

  /** Creates a room from the form. */
  protected async createRoom(): Promise<void> {
    if (!this.canCreate()) {
      return;
    }

    this.creating.set(true);
    this.notice.set(null);

    try {
      const room = await this.roomsApi.createRoom({
        name: this.name(),
        location: this.location() || null,
        capacity: this.capacity(),
        timeSlots: this.slotPreview(),
      });

      this.rooms.update((current) =>
        [...current, room].sort((a, b) => a.name.localeCompare(b.name)),
      );
      this.notice.set(info(this.i18n.t('admin.createdNotice', { name: room.name })));
      this.name.set('');
      this.location.set('');
    } catch (error) {
      this.notice.set(failure(this.describeError(error)));
    } finally {
      this.creating.set(false);
    }
  }

  /** Retires or restores a room. */
  protected async toggleActive(room: Room): Promise<void> {
    this.pendingRowId.set(room.id);
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

      const key: TranslationKey = updated.isActive ? 'admin.restoredNotice' : 'admin.retiredNotice';
      this.notice.set(info(this.i18n.t(key, { name: updated.name })));
    } catch (error) {
      this.notice.set(failure(this.describeError(error)));
    } finally {
      this.pendingRowId.set(null);
    }
  }

  /** Deletes a room, or retires it if it carries booking history. */
  protected async deleteRoom(room: Room): Promise<void> {
    this.confirmingId.set(null);
    this.pendingRowId.set(room.id);
    this.notice.set(null);

    try {
      await this.roomsApi.deleteRoom(room.id);
      await this.refresh();

      // The server decides between deleting and retiring, and answers 204 either way, so the
      // reloaded list is what says which happened.
      const retired = this.rooms().some((candidate) => candidate.id === room.id);

      this.notice.set(
        info(
          this.i18n.t(retired ? 'admin.deletedRetiredNotice' : 'admin.deletedRemovedNotice', {
            name: room.name,
          }),
        ),
      );
    } catch (error) {
      this.notice.set(failure(this.describeError(error)));
    } finally {
      this.pendingRowId.set(null);
    }
  }

  /** Cancels another user's booking. */
  protected async cancelBooking(booking: Booking): Promise<void> {
    this.confirmingId.set(null);
    this.pendingRowId.set(booking.id);
    this.notice.set(null);

    try {
      await this.bookingsApi.cancel(booking.id);
      this.bookings.update((current) => current.filter((candidate) => candidate.id !== booking.id));
      this.notice.set(
        info(this.i18n.t('admin.cancelledBookingNotice', { room: booking.roomName })),
      );
    } catch (error) {
      this.notice.set(failure(this.describeError(error)));
    } finally {
      this.pendingRowId.set(null);
    }
  }

  /** Reloads rooms and bookings after a failed load. */
  protected async retry(): Promise<void> {
    this.notice.set(null);
    await this.load();
  }

  /** Turns a failed request into the user's current language. */
  private describeError(error: unknown): string {
    return describeError(error, (key, params) => this.i18n.t(key, params));
  }

  /** Expands the opening hours into a list of slots. */
  private buildSlots(): TimeSlotRequest[] {
    const slots: TimeSlotRequest[] = [];
    const step = this.slotMinutes();

    // Guards a pathological form value rather than a plausible one: a step of zero would never
    // advance the loop, so the page would hang rather than show an empty preview.
    if (step <= 0) {
      return slots;
    }

    for (
      let minutes = this.openHour() * 60;
      minutes + step <= this.closeHour() * 60;
      minutes += step
    ) {
      slots.push({ startTime: toTime(minutes), endTime: toTime(minutes + step) });
    }

    return slots;
  }

  /** Loads rooms and bookings, showing the loading state while it happens. */
  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      await this.refresh();
      this.loadFailed.set(false);
    } catch (error) {
      this.notice.set(failure(this.describeError(error)));
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Re-reads both lists without blanking the panels.
   *
   * Used after an action the user has just taken, where replacing the page with placeholder rows
   * would be a flash of nothing in answer to a click that succeeded. Its error is left to escape to
   * the caller, which already has a message to report.
   */
  private async refresh(): Promise<void> {
    const [rooms, bookings] = await Promise.all([
      this.roomsApi.listRooms(),
      this.bookingsApi.listAll(),
    ]);

    this.rooms.set(rooms);
    this.bookings.set(bookings);
  }
}

/** Formats minutes-since-midnight as the "HH:mm:ss" the API expects for a TimeOnly. */
function toTime(minutes: number): string {
  const hours = `${Math.floor(minutes / 60)}`.padStart(2, '0');
  const remainder = `${minutes % 60}`.padStart(2, '0');

  return `${hours}:${remainder}:00`;
}
