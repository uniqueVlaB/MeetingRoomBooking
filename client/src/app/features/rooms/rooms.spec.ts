import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { RoomsService } from '../../core/api/rooms.service';
import { Room } from '../../core/models';
import { RoomsComponent } from './rooms';

/** Builds a room with just the fields the catalogue renders. */
function room(id: string, name: string, location: string | null): Room {
  return { id, name, location, capacity: 6, isActive: true, timeSlots: [] };
}

/**
 * Eight rooms: enough to be above the threshold at which the filter is offered, and varied enough
 * that a filter has something to narrow.
 */
function catalogue(): Room[] {
  return [
    room('r1', 'Kyiv — Focus Room', 'Third floor'),
    room('r2', 'Lviv — Board Room', 'Third floor'),
    room('r3', 'Odesa — Huddle', 'Ground floor'),
    room('r4', 'Kharkiv — Studio', 'Ground floor'),
    room('r5', 'Dnipro — Quiet Room', null),
    room('r6', 'Poltava — Meeting Room', 'Second floor'),
    room('r7', 'Rivne — Interview Room', 'Second floor'),
    room('r8', 'Sumy — Workshop', 'Annexe'),
  ];
}

/** A rooms client the test points at a fixed answer, or at a failure. */
class FakeRoomsService {
  constructor(private readonly answer: () => Promise<Room[]>) {}

  /** How many times the catalogue has been requested, so a retry is visible. */
  calls = 0;

  listRooms(): Promise<Room[]> {
    this.calls += 1;

    return this.answer();
  }
}

/** Reaches the component's protected members, which are protected for the template's sake. */
interface Internals {
  rooms: () => Room[];
  visibleRooms: () => Room[];
  filterable: () => boolean;
  query: { set(value: string): void };
  error: () => string | null;
  retry: () => Promise<void>;
}

/**
 * The room catalogue.
 *
 * The filter is the part worth pinning: narrowing a list and failing to load one both end with
 * nothing on screen, and a catalogue that is genuinely empty is a third thing again. Each has a
 * different way out, so telling a user the wrong one leaves them stuck.
 */
describe('RoomsComponent', () => {
  let fixture: ComponentFixture<RoomsComponent>;
  let component: Internals;

  /** Builds the component against a rooms client with the given answer. */
  async function render(answer: () => Promise<Room[]>): Promise<FakeRoomsService> {
    const roomsApi = new FakeRoomsService(answer);

    await TestBed.configureTestingModule({
      imports: [RoomsComponent],
      providers: [
        provideZonelessChangeDetection(),

        // Each card links to that room's schedule.
        provideRouter([]),
        { provide: RoomsService, useValue: roomsApi },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RoomsComponent);
    component = fixture.componentInstance as unknown as Internals;

    await fixture.whenStable();

    return roomsApi;
  }

  /** The page's visible text, collapsed to single spaces. */
  function text(): string {
    return (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  describe('filtering', () => {
    beforeEach(async () => {
      await render(() => Promise.resolve(catalogue()));
    });

    it('matches part of a room name, ignoring case', async () => {
      component.query.set('board');
      await fixture.whenStable();

      expect(component.visibleRooms().map((match) => match.id)).toEqual(['r2']);
    });

    it('matches a location as well as a name', async () => {
      component.query.set('ground floor');
      await fixture.whenStable();

      // "Which rooms are downstairs" is the other way people look for one, and the location is the
      // only other thing a card says in words.
      expect(component.visibleRooms().map((match) => match.id)).toEqual(['r3', 'r4']);
    });

    it('survives a room with no location', async () => {
      // The one room here with a null location must not throw when the filter reads it.
      component.query.set('quiet');
      await fixture.whenStable();

      expect(component.visibleRooms().map((match) => match.id)).toEqual(['r5']);
    });

    it('ignores surrounding whitespace', async () => {
      component.query.set('   huddle  ');
      await fixture.whenStable();

      expect(component.visibleRooms()).toHaveLength(1);
    });

    it('shows the whole catalogue when the box is empty', async () => {
      component.query.set('');
      await fixture.whenStable();

      expect(component.visibleRooms()).toHaveLength(8);
    });

    it('says a filter matched nothing, rather than that there are no rooms', async () => {
      component.query.set('zzz');
      await fixture.whenStable();

      // The distinction is the point. "No rooms yet" tells an administrator to go and create one;
      // the actual problem is three characters in a box they can clear.
      expect(text()).toContain('No room matches');
      expect(text()).not.toContain('No rooms yet');
    });
  });

  describe('when there are few rooms', () => {
    it('does not offer a filter', async () => {
      await render(() => Promise.resolve(catalogue().slice(0, 3)));

      // A search box over three rooms is furniture: another control to tab through, answering a
      // question nobody has.
      expect(component.filterable()).toBe(false);
      expect(fixture.nativeElement.querySelector('input[type="search"]')).toBeNull();
    });
  });

  describe('when the catalogue cannot be loaded', () => {
    it('reports the failure instead of an empty catalogue, and offers a way back', async () => {
      let fail = true;
      const roomsApi = await render(() =>
        fail ? Promise.reject(new Error('unreachable')) : Promise.resolve(catalogue()),
      );

      expect(component.error()).not.toBeNull();
      expect(text()).not.toContain('No rooms yet');

      // Before this the only retry was reloading the browser: the load ran once, from the
      // constructor, with nothing able to call it again.
      fail = false;
      await component.retry();
      await fixture.whenStable();

      expect(roomsApi.calls).toBe(2);
      expect(component.error()).toBeNull();
      expect(component.rooms()).toHaveLength(8);
    });
  });
});
