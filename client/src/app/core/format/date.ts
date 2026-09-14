/**
 * Dates on the wire and dates on screen.
 *
 * The API speaks `DateOnly` — "2026-09-20" — and the client keeps it in that form, because it is
 * the format the schedule endpoint takes back, the format the SignalR group name is built from, and
 * a value with no time zone attached. None of that makes it readable: a user scanning their
 * bookings should see "Sun 20 Sep", not an identifier.
 *
 * The locale is pinned rather than taken from the browser. The application is English throughout
 * (`index.html` declares `lang="en"`), and leaving it to the host would make the same booking read
 * "9/20/2026" on one machine and "20.09.2026" on the next while every other string stayed English.
 */
const LOCALE = 'en-GB';

const LONG = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

const SHORT = new Intl.DateTimeFormat(LOCALE, { weekday: 'short', day: 'numeric', month: 'short' });

/**
 * Reads "2026-09-20" as local midnight on that day.
 *
 * `new Date("2026-09-20")` is parsed as UTC midnight, which is the previous day everywhere west of
 * Greenwich — so a booking made in the evening would display with yesterday's name. Building the
 * date from its parts keeps it in the same wall-clock day the server meant.
 */
export function parseIsoDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);

  return new Date(year, month - 1, day);
}

/** Formats a date as "2026-09-20", avoiding the UTC shift `toISOString` would introduce. */
export function toIsoDate(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');

  return `${value.getFullYear()}-${month}-${day}`;
}

/** Today as "2026-09-20" in the browser's own time zone. */
export function todayIso(): string {
  return toIsoDate(new Date());
}

/** Moves an ISO date by a number of days, staying on calendar days across a clock change. */
export function shiftIsoDate(value: string, days: number): string {
  const moved = parseIsoDate(value);
  moved.setDate(moved.getDate() + days);

  return toIsoDate(moved);
}

/** Formats "2026-09-20" as "Sunday, 20 September 2026", for a heading. */
export function formatDayLong(value: string): string {
  return LONG.format(parseIsoDate(value));
}

/** Formats "2026-09-20" as "Sun 20 Sept", for a list where the year is rarely in doubt. */
export function formatDayShort(value: string): string {
  return SHORT.format(parseIsoDate(value));
}

/**
 * Names a date relative to today — "Today", "Tomorrow", "Yesterday" — or null if it is further off.
 *
 * Worth its own label because these three are the days a booking system is mostly about, and
 * "Sun 20 Sep" does not tell a reader at a glance whether it has already happened.
 */
export function relativeDay(value: string, today = todayIso()): string | null {
  if (value === today) {
    return 'Today';
  }

  if (value === shiftIsoDate(today, 1)) {
    return 'Tomorrow';
  }

  if (value === shiftIsoDate(today, -1)) {
    return 'Yesterday';
  }

  return null;
}

/** Whether a date is in the past, so a screen can show it as settled rather than as upcoming. */
export function isPastDay(value: string, today = todayIso()): boolean {
  return value < today;
}
