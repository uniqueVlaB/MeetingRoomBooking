import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { BookingsService } from '../../core/api/bookings.service';
import { shiftIsoDate, todayIso } from '../../core/format/date';
import { Booking } from '../../core/models';
import { MyBookingsComponent } from './my-bookings';

/** Builds a booking on a date, at an hour. */
function booking(
  id: string,
  slotDate: string,
  hour: number,
  roomName = 'Kyiv — Focus Room',
): Booking {
  const start = `${`${hour}`.padStart(2, '0')}:00:00`;
  const end = `${`${hour + 1}`.padStart(2, '0')}:00:00`;

  return {
    id,
    roomId: 'room-1',
    roomName,
    timeSlotId: `slot-${hour}`,
    slotDate,
    startTime: start,
    endTime: end,
    userId: 'user-1',
    userDisplayName: 'Uma User',
    createdUtc: '2026-09-14T09:00:00Z',
  };
}

/** A bookings client the test points at a fixed answer. */
class FakeBookingsService {
  constructor(private readonly answer: () => Promise<Booking[]>) {}

  listMine(): Promise<Booking[]> {
    return this.answer();
  }

  cancel(): Promise<void> {
    return Promise.resolve();
  }
}

/** One rendered day group, as the component exposes it. */
interface Day {
  date: string;
  label: string;
  relative: string | null;
  past: boolean;
  bookings: Booking[];
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  days: () => Day[];
  bookings: () => Booking[];
  loadFailed: () => boolean;
}

/**
 * The user's own bookings.
 *
 * The list used to repeat "2026-09-20" on every row -- the API's format, in the least readable
 * place. It is now grouped under one heading per day, which is worth pinning because grouping is
 * the kind of code that fails by quietly losing a row or reordering the days.
 */
describe('MyBookingsComponent', () => {
  let fixture: ComponentFixture<MyBookingsComponent>;
  let component: Internals;

  /** Builds the component against a bookings client with the given answer. */
  async function render(answer: () => Promise<Booking[]>): Promise<void> {
    TestBed.resetTestingModule();

    await TestBed.configureTestingModule({
      imports: [MyBookingsComponent],
      providers: [
        provideZonelessChangeDetection(),

        // Each row links back to the room's schedule.
        provideRouter([]),
        { provide: BookingsService, useValue: new FakeBookingsService(answer) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyBookingsComponent);
    component = fixture.componentInstance as unknown as Internals;

    await fixture.whenStable();
  }

  describe('grouping by day', () => {
    it('puts every booking under exactly one day, losing none', async () => {
      await render(() =>
        Promise.resolve([
          booking('b1', '2026-09-20', 9),
          booking('b2', '2026-09-20', 11),
          booking('b3', '2026-09-21', 14),
        ]),
      );

      expect(component.days()).toHaveLength(2);
      expect(component.days().flatMap((day) => day.bookings)).toHaveLength(3);
      expect(component.days()[0].bookings.map((held) => held.id)).toEqual(['b1', 'b2']);
      expect(component.days()[1].bookings.map((held) => held.id)).toEqual(['b3']);
    });

    it('keeps the order the server sent', async () => {
      await render(() =>
        Promise.resolve([
          booking('b1', '2026-09-20', 9),
          booking('b2', '2026-09-21', 9),
          booking('b3', '2026-09-22', 9),
        ]),
      );

      // The server already returns these in date order, so grouping only has to avoid disturbing
      // it. A Map preserves insertion order; sorting again here would be a second opinion about
      // something already decided.
      expect(component.days().map((day) => day.date)).toEqual([
        '2026-09-20',
        '2026-09-21',
        '2026-09-22',
      ]);
    });

    it('regroups a day whose rows are not adjacent', async () => {
      await render(() =>
        Promise.resolve([
          booking('b1', '2026-09-20', 9),
          booking('b2', '2026-09-21', 9),
          booking('b3', '2026-09-20', 15),
        ]),
      );

      // Not the order the API produces today, but the grouping must not depend on that: silently
      // creating a second "Sunday" heading would be the failure.
      expect(component.days()).toHaveLength(2);
      expect(component.days()[0].bookings.map((held) => held.id)).toEqual(['b1', 'b3']);
    });

    it('names today and tomorrow, and marks yesterday as gone', async () => {
      const today = todayIso();

      await render(() =>
        Promise.resolve([
          booking('b1', shiftIsoDate(today, -1), 9),
          booking('b2', today, 9),
          booking('b3', shiftIsoDate(today, 1), 9),
        ]),
      );

      const [yesterday, now, tomorrow] = component.days();

      expect(yesterday.relative).toBe('Yesterday');
      expect(yesterday.past).toBe(true);
      expect(now.relative).toBe('Today');
      expect(now.past).toBe(false);
      expect(tomorrow.relative).toBe('Tomorrow');
      expect(tomorrow.past).toBe(false);
    });

    it('writes the date out in words', async () => {
      await render(() => Promise.resolve([booking('b1', '2026-09-20', 9)]));

      // The heading is the only place the date appears now, so it has to be the readable form.
      expect(component.days()[0].label).toBe('Sunday, 20 September 2026');
      expect(fixture.nativeElement.textContent).toContain('Sunday, 20 September 2026');
    });
  });

  describe('when nothing comes back', () => {
    it('distinguishes an empty list from a failed request', async () => {
      await render(() => Promise.resolve([]));

      expect(component.loadFailed()).toBe(false);
      expect(fixture.nativeElement.textContent).toContain('You have no bookings');

      await render(() => Promise.reject(new Error('unreachable')));

      // Both leave the component holding nothing. Answering a dead network with "You have no
      // bookings" is wrong, and alarming to somebody who knows they have several.
      expect(component.loadFailed()).toBe(true);
      expect(fixture.nativeElement.textContent).not.toContain('You have no bookings');
    });
  });
});
