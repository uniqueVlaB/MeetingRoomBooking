/**
 * Formats a `TimeOnly` from the API — "09:00:00" — as "09:00".
 *
 * Seconds are always zero on a slot template, and showing them makes a schedule harder to scan.
 */
export function formatSlotTime(time: string): string {
  return time.slice(0, 5);
}
