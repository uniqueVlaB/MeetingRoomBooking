import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RoomsService } from '../../core/api/rooms.service';
import { describeError } from '../../core/http/describe-error';
import { Room } from '../../core/models';

/** The room catalogue: pick a room to see its schedule. */
@Component({
  selector: 'app-rooms',
  imports: [RouterLink],
  templateUrl: './rooms.html',
  styleUrl: './rooms.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoomsComponent {
  private readonly roomsApi = inject(RoomsService);

  protected readonly rooms = signal<Room[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  /** Loads the catalogue. */
  private async load(): Promise<void> {
    try {
      this.rooms.set(await this.roomsApi.listRooms());
    } catch (error) {
      // The server's own explanation, not a canned sentence: "your session has expired" and "the
      // server could not be reached" call for different reactions from the user.
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
