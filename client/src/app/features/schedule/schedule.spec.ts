import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { BookingsService } from '../../core/api/bookings.service';
import { RoomsService } from '../../core/api/rooms.service';
import { AuthService } from '../../core/auth/auth.service';
import { Booking, Schedule } from '../../core/models';
import { BookingHubService } from '../../core/realtime/booking-hub.service';
import { ScheduleComponent } from './schedule';

const ROOM_ID = 'room-1';

/** A schedule with two free slots on a date. */
function scheduleFor(date: string): Schedule {
  return {
    roomId: ROOM_ID,
    roomName: 'Kyiv — Focus Room',
    date,
    slots: [
      {
        timeSlotId: 'slot-1',
        startTime: '09:00:00',
        endTime: '10:00:00',
        ordinal: 0,
        isBooked: false,
        bookingId: null,
        bookedByUserId: null,
        bookedByDisplayName: null,
      },
      {
        timeSlotId: 'slot-2',
        startTime: '10:00:00',
        endTime: '11:00:00',
        ordinal: 1,
        isBooked: false,
        bookingId: null,
        bookedByUserId: null,
        bookedByDisplayName: null,
      },
    ],
  };
}

/** A booking of the first slot on a date. */
function bookingFor(date: string): Booking {
  return {
    id: 'booking-1',
    roomId: ROOM_ID,
    roomName: 'Kyiv — Focus Room',
    timeSlotId: 'slot-1',
    slotDate: date,
    startTime: '09:00:00',
    endTime: '10:00:00',
    userId: 'user-1',
    userDisplayName: 'Uma User',
    createdUtc: '2026-09-14T09:00:00Z',
  };
}

/** A rooms client whose schedule responses the test resolves by hand. */
class FakeRoomsService {
  readonly pending: { date: string; resolve: (schedule: Schedule) => void }[] = [];

  getSchedule(_roomId: string, date: string): Promise<Schedule> {
    return new Promise<Schedule>((resolve) => {
      this.pending.push({ date, resolve });
    });
  }

  /** Resolves the request that asked for a date. */
  settle(date: string): void {
    const index = this.pending.findIndex((request) => request.date === date);
    const [request] = this.pending.splice(index, 1);

    request.resolve(scheduleFor(date));
  }
}

/** A bookings client that answers immediately. */
class FakeBookingsService {
  book(request: { timeSlotId: string; slotDate: string }): Promise<Booking> {
    return Promise.resolve(bookingFor(request.slotDate));
  }

  cancel(): Promise<void> {
    return Promise.resolve();
  }
}

/** A hub that never connects, so the component must stand on its own. */
class SilentHubService {
  readonly slotBooked$ = new Subject<Booking>();
  readonly slotReleased$ = new Subject<Booking>();
  readonly isConnected = signal(false);

  watch(): Promise<void> {
    return Promise.resolve();
  }
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  date: { set(value: string): void };
  schedule: () => Schedule | null;
  slots: () => Schedule['slots'];
  loading: () => boolean;
  book: (slot: Schedule['slots'][number]) => Promise<void>;
  cancel: (slot: Schedule['slots'][number]) => Promise<void>;
}

/**
 * The screen the whole system exists for.
 *
 * Two behaviours are pinned here because both fail silently and both leave a user looking at a
 * schedule that is not true: a slower response for a day already left overwriting the day on
 * screen, and a successful booking that never appears because the broadcast carrying it did not
 * arrive.
 */
describe('ScheduleComponent', () => {
  let fixture: ComponentFixture<ScheduleComponent>;
  let component: Internals;
  let rooms: FakeRoomsService;

  beforeEach(async () => {
    rooms = new FakeRoomsService();

    await TestBed.configureTestingModule({
      imports: [ScheduleComponent],
      providers: [
        provideZonelessChangeDetection(),

        // The template carries routerLink back to the room list.
        provideRouter([]),
        { provide: RoomsService, useValue: rooms },
        { provide: BookingsService, useValue: new FakeBookingsService() },
        { provide: BookingHubService, useValue: new SilentHubService() },
        {
          provide: AuthService,
          useValue: { session: signal({ userId: 'user-1' }), isAdmin: () => false },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ScheduleComponent);
    fixture.componentRef.setInput('roomId', ROOM_ID);
    component = fixture.componentInstance as unknown as Internals;

    await fixture.whenStable();
  });

  it('ignores a response for a day the user has already left', async () => {
    // Two days in flight at once, as happens when the date arrows are clicked quickly.
    component.date.set('2026-09-20');
    await fixture.whenStable();

    component.date.set('2026-09-21');
    await fixture.whenStable();

    // The older request comes back last — the case that used to win.
    rooms.settle('2026-09-21');
    await fixture.whenStable();

    rooms.settle('2026-09-20');
    await fixture.whenStable();

    // The day on screen must be the day the user asked for last, not the one whose response
    // happened to arrive last.
    expect(component.schedule()?.date).toBe('2026-09-21');
    expect(component.loading()).toBe(false);
  });

  it('marks a slot taken from the booking response, without waiting for a broadcast', async () => {
    component.date.set('2026-09-20');
    await fixture.whenStable();

    rooms.settle('2026-09-20');
    await fixture.whenStable();

    const free = component.slots()[0];

    expect(free.isBooked).toBe(false);

    await component.book(free);
    await fixture.whenStable();

    // The hub in this test never delivers anything, which is exactly the state the component
    // treats as survivable. The grid still has to tell the truth.
    const booked = component.slots()[0];

    expect(booked.isBooked).toBe(true);
    expect(booked.bookingId).toBe('booking-1');
    expect(booked.bookedByDisplayName).toBe('Uma User');
  });

  it('frees a slot from the cancellation, without waiting for a broadcast', async () => {
    component.date.set('2026-09-20');
    await fixture.whenStable();

    rooms.settle('2026-09-20');
    await fixture.whenStable();

    await component.book(component.slots()[0]);
    await fixture.whenStable();

    await component.cancel(component.slots()[0]);
    await fixture.whenStable();

    const released = component.slots()[0];

    expect(released.isBooked).toBe(false);
    expect(released.bookingId).toBeNull();
  });
});
