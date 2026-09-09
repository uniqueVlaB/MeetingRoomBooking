import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RoomsService } from '../../core/api/rooms.service';
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
  private readonly rooms = inject(RoomsService);

  protected readonly list = signal<Room[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  /** Loads the catalogue. */
  private async load(): Promise<void> {
    try {
      this.list.set(await this.rooms.listRooms());
    } catch {
      this.error.set('Rooms could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
