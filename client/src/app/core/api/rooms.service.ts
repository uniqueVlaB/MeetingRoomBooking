import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config.service';
import { CreateRoomRequest, Room, Schedule, UpdateRoomRequest } from '../models';

/** Reads rooms and schedules, and performs the administrator-only catalogue operations. */
@Injectable({ providedIn: 'root' })
export class RoomsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  /** Lists rooms. Administrators also receive retired ones. */
  listRooms(): Promise<Room[]> {
    return firstValueFrom(this.http.get<Room[]>(this.url()));
  }

  /** Reads one room. */
  getRoom(roomId: string): Promise<Room> {
    return firstValueFrom(this.http.get<Room>(this.url(roomId)));
  }

  /**
   * Reads a room's schedule for a date.
   *
   * @param roomId The room.
   * @param date The date as `yyyy-MM-dd` — the same format the SignalR group name uses, so a
   * schedule view and its live subscription always refer to the same day.
   */
  getSchedule(roomId: string, date: string): Promise<Schedule> {
    return firstValueFrom(
      this.http.get<Schedule>(this.url(roomId, 'schedule'), { params: { date } }),
    );
  }

  /** Creates a room and its slot template. */
  createRoom(request: CreateRoomRequest): Promise<Room> {
    return firstValueFrom(this.http.post<Room>(this.url(), request));
  }

  /** Updates a room's details. */
  updateRoom(roomId: string, request: UpdateRoomRequest): Promise<Room> {
    return firstValueFrom(this.http.put<Room>(this.url(roomId), request));
  }

  /** Removes or retires a room. */
  deleteRoom(roomId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url(roomId)));
  }

  /** Builds an API URL from path segments. */
  private url(...segments: string[]): string {
    return [`${this.config.apiBaseUrl}/api/rooms`, ...segments].join('/');
  }
}
