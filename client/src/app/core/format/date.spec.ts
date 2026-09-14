import { describe, expect, it } from 'vitest';
import {
  formatDayLong,
  formatDayShort,
  isPastDay,
  parseIsoDate,
  relativeDay,
  shiftIsoDate,
  toIsoDate,
  todayIso,
} from './date';

/**
 * Date handling, which is where a booking system quietly goes wrong.
 *
 * Every case here is one that produces a plausible-looking but wrong day rather than an error: an
 * off-by-one from a UTC parse, a month or a year that fails to roll over, or a "Today" label on a
 * date that is not today.
 */
describe('date formatting', () => {
  it('reads an ISO date as local midnight, not as UTC', () => {
    const parsed = parseIsoDate('2026-09-20');

    // The bug this guards: `new Date("2026-09-20")` is UTC midnight, which is 19 September in every
    // time zone west of Greenwich -- so a user in Chicago would see yesterday's name on a booking.
    expect(parsed.getFullYear()).toBe(2026);
    expect(parsed.getMonth()).toBe(8);
    expect(parsed.getDate()).toBe(20);
    expect(parsed.getHours()).toBe(0);
  });

  it('round-trips a date through its ISO form', () => {
    expect(toIsoDate(parseIsoDate('2026-09-20'))).toBe('2026-09-20');
  });

  it('pads single-digit months and days', () => {
    // "2026-1-5" is not a DateOnly the API will accept, and the failure would be a 400 from a value
    // that looks right in the URL.
    expect(toIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('names today as today', () => {
    expect(relativeDay(todayIso())).toBe('today');
  });

  describe('shifting by days', () => {
    it('moves within a month', () => {
      expect(shiftIsoDate('2026-09-20', 1)).toBe('2026-09-21');
      expect(shiftIsoDate('2026-09-20', -1)).toBe('2026-09-19');
    });

    it('rolls over a month boundary', () => {
      expect(shiftIsoDate('2026-09-30', 1)).toBe('2026-10-01');
      expect(shiftIsoDate('2026-10-01', -1)).toBe('2026-09-30');
    });

    it('rolls over a year boundary', () => {
      expect(shiftIsoDate('2026-12-31', 1)).toBe('2027-01-01');
    });

    it('handles a leap day', () => {
      expect(shiftIsoDate('2028-02-28', 1)).toBe('2028-02-29');
      expect(shiftIsoDate('2028-02-29', 1)).toBe('2028-03-01');
    });

    it('skips 29 February in a common year', () => {
      expect(shiftIsoDate('2027-02-28', 1)).toBe('2027-03-01');
    });
  });

  describe('display', () => {
    it('spells a date out in full for a heading', () => {
      expect(formatDayLong('2026-09-20')).toBe('Sunday, 20 September 2026');
    });

    it('abbreviates a date for a list', () => {
      // The month abbreviation is CLDR's ("Sep" or "Sept" depending on the data the runtime ships),
      // so the shape is pinned rather than the exact spelling.
      expect(formatDayShort('2026-09-20')).toMatch(/^Sun 20 Sept?$/);
    });

    it('puts the day before the month, matching the default English formatting', () => {
      // A fixed default locale is the point: left to the host this would read "9/20/2026" on a US
      // machine while every neighbouring string stayed British English.
      expect(formatDayLong('2026-01-02')).toBe('Friday, 2 January 2026');
    });

    it('formats in another language when asked, weekday and month included', () => {
      // The point of taking a locale rather than hardcoding one: switching the application's
      // language must translate the date too, not just the labels around it.
      expect(formatDayLong('2026-09-20', 'uk-UA')).toBe('неділя, 20 вересня 2026 р.');
    });
  });

  describe('relative naming', () => {
    it('names the three days a booking system is mostly about', () => {
      expect(relativeDay('2026-09-20', '2026-09-20')).toBe('today');
      expect(relativeDay('2026-09-21', '2026-09-20')).toBe('tomorrow');
      expect(relativeDay('2026-09-19', '2026-09-20')).toBe('yesterday');
    });

    it('declines to name a date further off', () => {
      // Anything else is shown by its own name; inventing "in 2 days" for an arbitrary distance
      // would be less precise than the date it replaced.
      expect(relativeDay('2026-09-22', '2026-09-20')).toBeNull();
      expect(relativeDay('2026-09-18', '2026-09-20')).toBeNull();
    });

    it('crosses a month boundary', () => {
      expect(relativeDay('2026-10-01', '2026-09-30')).toBe('tomorrow');
      expect(relativeDay('2026-09-30', '2026-10-01')).toBe('yesterday');
    });

    it('returns a code, not display text, so the caller translates it', () => {
      // Guards the contract the shell relies on: relativeDay used to return the English word
      // itself, which meant it could only ever be shown in English. A component that forgot to
      // translate the code and rendered it directly would show "today", not "Today" or "Сьогодні".
      expect(relativeDay('2026-09-20', '2026-09-20')).not.toBe('Today');
    });
  });

  describe('past days', () => {
    it('treats today as not yet past', () => {
      // Today's later slots are still bookable, so a screen must not present today as settled.
      expect(isPastDay('2026-09-20', '2026-09-20')).toBe(false);
    });

    it('recognises an earlier day', () => {
      expect(isPastDay('2026-09-19', '2026-09-20')).toBe(true);
      expect(isPastDay('2025-12-31', '2026-09-20')).toBe(true);
    });

    it('recognises a later day', () => {
      expect(isPastDay('2026-09-21', '2026-09-20')).toBe(false);
    });
  });
});
