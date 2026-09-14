import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { Booking, Room } from '../../core/models';
import { AdminComponent } from './admin';

/** Two rooms, so a test can prove that acting on one leaves the other alone. */
function rooms(): Room[] {
  return [
    {
      id: 'room-1',
      name: 'Kyiv — Focus Room',
      location: 'Third floor',
      capacity: 4,
      isActive: true,
      timeSlots: [],
    },
    {
      id: 'room-2',
      name: 'Lviv — Board Room',
      location: null,
      capacity: 12,
      isActive: true,
      timeSlots: [],
    },
  ];
}

function bookings(): Booking[] {
  return [
    {
      id: 'booking-1',
      roomId: 'room-1',
      roomName: 'Kyiv — Focus Room',
      timeSlotId: 'slot-1',
      slotDate: '2026-09-20',
      startTime: '09:00:00',
      endTime: '10:00:00',
      userId: 'user-2',
      userDisplayName: 'Uma User',
      createdUtc: '2026-09-14T09:00:00Z',
    },
  ];
}

/** A rooms client that records what was asked of it, and holds deletes open until released. */
class FakeRoomsService {
  readonly deleted: string[] = [];

  /** Resolves the delete that is currently in flight. */
  releaseDelete: (() => void) | null = null;

  listRooms(): Promise<Room[]> {
    return Promise.resolve(rooms());
  }

  deleteRoom(roomId: string): Promise<void> {
    this.deleted.push(roomId);

    return new Promise<void>((resolve) => {
      this.releaseDelete = resolve;
    });
  }
}

class FakeBookingsService {
  readonly cancelled: string[] = [];

  listAll(): Promise<Booking[]> {
    return Promise.resolve(bookings());
  }

  cancel(bookingId: string): Promise<void> {
    this.cancelled.push(bookingId);

    return Promise.resolve();
  }
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  openHour: { set(value: number): void };
  closeHour: { set(value: number): void };
  slotMinutes: { set(value: number): void };
  name: { set(value: string): void };
  slotPreview: () => { startTime: string; endTime: string }[];
  canCreate: () => boolean;
}

/**
 * The administration screen, where every action is taken on somebody else's behalf.
 *
 * The properties pinned here are the ones whose loss is invisible in the interface: a destructive
 * action that stopped asking, a page that freezes wholesale while one row works, and a form that
 * submits values the server is certain to reject.
 */
describe('AdminComponent', () => {
  let fixture: ComponentFixture<AdminComponent>;
  let component: Internals;
  let roomsApi: FakeRoomsService;
  let bookingsApi: FakeBookingsService;

  /** Every button on the page, in document order. */
  function buttons(): HTMLButtonElement[] {
    return [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
  }

  /** The first button whose visible label is exactly this text. */
  function button(label: string): HTMLButtonElement {
    const found = buttons().find((candidate) => candidate.textContent?.trim() === label);

    if (!found) {
      throw new Error(
        `No button labelled "${label}". Present: ${buttons()
          .map((candidate) => `"${candidate.textContent?.trim()}"`)
          .join(', ')}`,
      );
    }

    return found;
  }

  beforeEach(async () => {
    roomsApi = new FakeRoomsService();
    bookingsApi = new FakeBookingsService();

    await TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideZonelessChangeDetection(),

        // The template links each room to its schedule.
        provideRouter([]),
        { provide: RoomsService, useValue: roomsApi },
        { provide: BookingsService, useValue: bookingsApi },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminComponent);
    component = fixture.componentInstance as unknown as Internals;

    await fixture.whenStable();
  });

  describe('destructive actions', () => {
    it('does not delete a room on the first click', async () => {
      button('Delete').click();
      await fixture.whenStable();

      // The whole point: Delete sat next to Retire, looked the same, cost the same one click, and
      // could not be undone. Wiring it straight back to deleteRoom() would leave the interface
      // looking identical while restoring exactly that.
      expect(roomsApi.deleted).toEqual([]);
      expect(button('Confirm delete')).toBeTruthy();
    });

    it('deletes the room on the second click', async () => {
      button('Delete').click();
      await fixture.whenStable();

      button('Confirm delete').click();
      await fixture.whenStable();

      expect(roomsApi.deleted).toEqual(['room-1']);
    });

    it('lets the user back out of a delete it has asked about', async () => {
      button('Delete').click();
      await fixture.whenStable();

      button('Keep').click();
      await fixture.whenStable();

      expect(roomsApi.deleted).toEqual([]);
      expect(button('Delete')).toBeTruthy();
    });

    it('asks again before cancelling a booking that belongs to somebody else', async () => {
      button('Cancel').click();
      await fixture.whenStable();

      expect(bookingsApi.cancelled).toEqual([]);

      button('Confirm cancel').click();
      await fixture.whenStable();

      expect(bookingsApi.cancelled).toEqual(['booking-1']);
    });

    it('arms only the row that was asked about', async () => {
      button('Delete').click();
      await fixture.whenStable();

      // Two rooms are listed, so exactly one Delete must have turned into a question and the other
      // must still be its ordinary self.
      const labels = buttons().map((candidate) => candidate.textContent?.trim());

      expect(labels.filter((label) => label === 'Confirm delete')).toHaveLength(1);
      expect(labels.filter((label) => label === 'Delete')).toHaveLength(1);
    });
  });

  it('freezes only the row that is working', async () => {
    button('Delete').click();
    await fixture.whenStable();

    button('Confirm delete').click();
    await fixture.whenStable();

    // The delete is still in flight -- the fake has not been released.
    expect(roomsApi.releaseDelete).not.toBeNull();

    const enabled = buttons().filter((candidate) => !candidate.disabled);
    const labels = enabled.map((candidate) => candidate.textContent?.trim());

    // A single page-wide "busy" flag used to disable every button here, so acting on one room read
    // as the whole screen having stopped responding.
    expect(labels).toContain('Retire');
    expect(labels).toContain('Delete');
  });

  describe('the slot preview', () => {
    it('counts the slots the form would create', async () => {
      // 09:00 to 17:00 in hours: the default, and the one an administrator will check against.
      expect(component.slotPreview()).toHaveLength(8);
      expect(component.slotPreview()[0].startTime).toBe('09:00:00');
      expect(component.slotPreview()[7].endTime).toBe('17:00:00');
    });

    it('follows the slot length', async () => {
      component.slotMinutes.set(30);
      await fixture.whenStable();

      expect(component.slotPreview()).toHaveLength(16);
    });

    it('drops a trailing part-slot rather than overrunning the closing hour', async () => {
      component.openHour.set(9);
      component.closeHour.set(12);
      component.slotMinutes.set(120);
      await fixture.whenStable();

      // 09:00-11:00 fits; 11:00-13:00 would run past closing, and a room that is shut is not a room
      // that can be booked.
      expect(component.slotPreview()).toHaveLength(1);
      expect(component.slotPreview()[0].endTime).toBe('11:00:00');
    });

    it('refuses a form whose hours produce nothing', async () => {
      component.name.set('Somewhere');
      await fixture.whenStable();

      expect(component.canCreate()).toBe(true);

      component.closeHour.set(9);
      await fixture.whenStable();

      // Previously this was submitted and came back as a validation error from the server. The
      // preview says so in advance, and the button will not send it.
      expect(component.slotPreview()).toHaveLength(0);
      expect(component.canCreate()).toBe(false);
    });

    it('refuses a form with no name', async () => {
      expect(component.canCreate()).toBe(false);

      component.name.set('   ');
      await fixture.whenStable();

      // Whitespace is not a name, and the server would reject it after a round trip.
      expect(component.canCreate()).toBe(false);
    });
  });
});
