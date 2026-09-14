/**
 * Dates on the wire and dates on screen.
 *
 * The API speaks `DateOnly` — "2026-09-20" — and the client keeps it in that form, because it is
 * the format the schedule endpoint takes back, the format the SignalR group name is built from, and
 * a value with no time zone attached. None of that makes it readable: a user scanning their
 * bookings should see "Sun 20 Sep", not an identifier.
 *
 * The locale used to format that is a parameter, not a constant: the application has its own
 * language switch (`core/i18n`), independent of the browser's, and a booking made in English should
 * read in Ukrainian the moment the user switches — including the weekday and month names, not just
 * the surrounding chrome. Every caller defaults to `'en-GB'` so a call site that has no
 * `TranslationService` in scope (a spec, say) still gets the fixed, deterministic formatting this
 * module always had.
 */
const DEFAULT_LOCALE = 'en-GB';

/** `Intl.DateTimeFormat` instances are not cheap to build, so one is kept per locale actually used. */
const longFormatters = new Map<string, Intl.DateTimeFormat>();
const shortFormatters = new Map<string, Intl.DateTimeFormat>();

function longFormatter(locale: string): Intl.DateTimeFormat {
  let formatter = longFormatters.get(locale);

  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
    longFormatters.set(locale, formatter);
  }

  return formatter;
}

function shortFormatter(locale: string): Intl.DateTimeFormat {
  let formatter = shortFormatters.get(locale);

  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
    });
    shortFormatters.set(locale, formatter);
  }

  return formatter;
}

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
export function formatDayLong(value: string, locale: string = DEFAULT_LOCALE): string {
  return longFormatter(locale).format(parseIsoDate(value));
}

/** Formats "2026-09-20" as "Sun 20 Sept", for a list where the year is rarely in doubt. */
export function formatDayShort(value: string, locale: string = DEFAULT_LOCALE): string {
  return shortFormatter(locale).format(parseIsoDate(value));
}

/** A date named relative to today, for the three days a booking system is mostly about. */
export type RelativeDay = 'today' | 'tomorrow' | 'yesterday';

/**
 * Names a date relative to today — `'today'`, `'tomorrow'`, `'yesterday'` — or null if it is
 * further off than that.
 *
 * Returns a code rather than display text on purpose: what a reader sees has to be in whichever
 * language is currently chosen ("Сьогодні", not "Today"), so translating it is the caller's job —
 * `TranslationService.t('date.' + code)` — and this function only has to say *which* of the three
 * days, the same way in every language.
 */
export function relativeDay(value: string, today = todayIso()): RelativeDay | null {
  if (value === today) {
    return 'today';
  }

  if (value === shiftIsoDate(today, 1)) {
    return 'tomorrow';
  }

  if (value === shiftIsoDate(today, -1)) {
    return 'yesterday';
  }

  return null;
}

/** Whether a date is in the past, so a screen can show it as settled rather than as upcoming. */
export function isPastDay(value: string, today = todayIso()): boolean {
  return value < today;
}
