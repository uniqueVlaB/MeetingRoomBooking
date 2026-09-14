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

/** One schedule request the test has not answered yet. */
interface Pending {
  date: string;
  resolve: (schedule: Schedule) => void;
  reject: (error: unknown) => void;
}

/** A rooms client whose schedule responses the test resolves by hand. */
class FakeRoomsService {
  readonly pending: Pending[] = [];

  getSchedule(_roomId: string, date: string): Promise<Schedule> {
    return new Promise<Schedule>((resolve, reject) => {
      this.pending.push({ date, resolve, reject });
    });
  }

  /** Resolves the request that asked for a date. */
  settle(date: string): void {
    this.take(date).resolve(scheduleFor(date));
  }

  /** Fails the request that asked for a date, as a server or network error would. */
  fail(date: string): void {
    this.take(date).reject(new Error('unreachable'));
  }

  /** Removes and returns the pending request for a date. */
  private take(date: string): Pending {
    const index = this.pending.findIndex((request) => request.date === date);
    const [request] = this.pending.splice(index, 1);

    return request;
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

  /** Fires when the hub has dropped and rejoined its group; the component must re-read. */
  readonly rejoined$ = new Subject<void>();

  readonly connectionState = signal<'connected' | 'reconnecting' | 'offline'>('offline');
  readonly isConnected = signal(false);

  /** Groups left, so the test can prove the component stops watching when it goes away. */
  unwatched = 0;

  watch(): Promise<void> {
    return Promise.resolve();
  }

  unwatch(): Promise<void> {
    this.unwatched += 1;
    return Promise.resolve();
  }
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  date: { set(value: string): void };
  schedule: () => Schedule | null;
  slots: () => Schedule['slots'];
  loading: () => boolean;
  loadFailed: () => boolean;
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
  let hub: SilentHubService;

  beforeEach(async () => {
    rooms = new FakeRoomsService();
    hub = new SilentHubService();

    await TestBed.configureTestingModule({
      imports: [ScheduleComponent],
      providers: [
        provideZonelessChangeDetection(),

        // The template carries routerLink back to the room list.
        provideRouter([]),
        { provide: RoomsService, useValue: rooms },
        { provide: BookingsService, useValue: new FakeBookingsService() },
        { provide: BookingHubService, useValue: hub },
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

  it('re-reads the schedule after the hub rejoins its group', async () => {
    component.date.set('2026-09-20');
    await fixture.whenStable();

    rooms.settle('2026-09-20');
    await fixture.whenStable();

    // Somebody else books this slot while the connection is down, so the broadcast never arrives.
    expect(component.slots()[0].isBooked).toBe(false);

    // Ignore the load for today that the component fires on arrival, so what is left is only what
    // the rejoin causes.
    rooms.pending.length = 0;

    hub.rejoined$.next();
    await fixture.whenStable();

    // Rejoining restores the flow of updates but recovers nothing missed, so the only honest move
    // is to ask the server again.
    expect(rooms.pending).toHaveLength(1);
    expect(rooms.pending[0].date).toBe('2026-09-20');
  });

  it('reports a failed load as a failure rather than as an empty room', async () => {
    component.date.set('2026-09-20');
    await fixture.whenStable();

    rooms.fail('2026-09-20');
    await fixture.whenStable();

    // Both leave the component holding no slots. Telling them apart is what stops the screen
    // answering a dead network with "This room has no bookable slots."
    expect(component.loadFailed()).toBe(true);
    expect(component.schedule()).toBeNull();
    expect(component.loading()).toBe(false);
  });

  it('stops showing the previous day while the next one loads', async () => {
    component.date.set('2026-09-20');
    await fixture.whenStable();

    rooms.settle('2026-09-20');
    await fixture.whenStable();

    expect(component.schedule()?.date).toBe('2026-09-20');

    component.date.set('2026-09-21');
    await fixture.whenStable();

    // The template hides the grid whenever a load is in flight. Leaving the previous day visible
    // would offer a Cancel button carrying that day's booking id under a picker showing the next.
    expect(component.loading()).toBe(true);
  });

  it('leaves the watched group when the view goes away', async () => {
    expect(hub.unwatched).toBe(0);

    fixture.destroy();

    // The connection is shared and stays open; staying in the group would have the server fan
    // messages out to a page with nothing left to render them.
    expect(hub.unwatched).toBe(1);
  });
});
