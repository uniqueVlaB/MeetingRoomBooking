import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config.service';
import { Booking, CreateBookingRequest } from '../models';

/** Creates and cancels bookings, and lists them. */
@Injectable({ providedIn: 'root' })
export class BookingsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  /**
   * Books a slot.
   *
   * Rejects with a 409 when another user won the slot. Callers are expected to surface that as a
   * plain "somebody just took it" message and reload the schedule — it is an ordinary outcome of
   * two people clicking at once, not a failure.
   */
  book(request: CreateBookingRequest): Promise<Booking> {
    return firstValueFrom(this.http.post<Booking>(this.url(), request));
  }

  /** Cancels a booking, releasing the slot. */
  cancel(bookingId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url(bookingId)));
  }

  /** Lists the signed-in user's own bookings. */
  listMine(): Promise<Booking[]> {
    return firstValueFrom(this.http.get<Booking[]>(this.url('mine')));
  }

  /** Lists every user's bookings. Administrators only. */
  listAll(): Promise<Booking[]> {
    return firstValueFrom(this.http.get<Booking[]>(this.url()));
  }

  /** Builds an API URL from path segments. */
  private url(...segments: string[]): string {
    return [`${this.config.apiBaseUrl}/api/bookings`, ...segments].join('/');
  }
}
