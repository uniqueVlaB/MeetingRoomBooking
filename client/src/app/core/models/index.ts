/**
 * TypeScript mirrors of the server contracts in `MeetingRooms.Contracts`.
 *
 * Property names match the JSON exactly, which is camel-cased by System.Text.Json. `TimeOnly`
 * arrives as "09:00:00" and `DateOnly` as "2026-09-10"; both are kept as strings, because the only
 * things done with them here are display and equality, and converting to `Date` would drag in a
 * time zone that neither value has.
 */

/** The administrator role name, as issued in the token's role claims. */
export const ROLE_ADMIN = 'Admin';

/** A signed-in session. The refresh token is not here — it lives in an HttpOnly cookie. */
export interface AuthSession {
  accessToken: string;
  expiresUtc: string;
  userId: string;
  email: string;
  displayName: string;
  roles: string[];
}

/** Credentials for signing in. */
export interface LoginRequest {
  email: string;
  password: string;
}

/** Details for creating an account. */
export interface RegisterRequest {
  email: string;
  displayName: string;
  password: string;
}

/** One entry in a room's fixed daily schedule. */
export interface TimeSlot {
  id: string;
  startTime: string;
  endTime: string;
  ordinal: number;
}

/** A bookable room and its slot template. */
export interface Room {
  id: string;
  name: string;
  location: string | null;
  capacity: number;
  isActive: boolean;
  timeSlots: TimeSlot[];
}

/** A slot to create as part of a room's template. */
export interface TimeSlotRequest {
  startTime: string;
  endTime: string;
}

/** Request to create a room together with its daily slots. */
export interface CreateRoomRequest {
  name: string;
  location: string | null;
  capacity: number;
  timeSlots: TimeSlotRequest[];
}

/** Request to update a room's details. */
export interface UpdateRoomRequest {
  name: string;
  location: string | null;
  capacity: number;
  isActive: boolean;
}

/** One slot on one date, with its current booking state. */
export interface ScheduleSlot {
  timeSlotId: string;
  startTime: string;
  endTime: string;
  ordinal: number;
  isBooked: boolean;
  bookingId: string | null;
  bookedByUserId: string | null;
  bookedByDisplayName: string | null;
}

/** A room's schedule for a single date. */
export interface Schedule {
  roomId: string;
  roomName: string;
  date: string;
  slots: ScheduleSlot[];
}

/** A booking, as returned by the API and broadcast over SignalR. */
export interface Booking {
  id: string;
  roomId: string;
  roomName: string;
  timeSlotId: string;
  slotDate: string;
  startTime: string;
  endTime: string;
  userId: string;
  userDisplayName: string;
  createdUtc: string;
}

/** Request to book one slot on one date. */
export interface CreateBookingRequest {
  timeSlotId: string;
  slotDate: string;
}
