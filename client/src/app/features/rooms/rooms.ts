import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RoomsService } from '../../core/api/rooms.service';
import { describeError } from '../../core/http/describe-error';
import { TranslationService } from '../../core/i18n/translation.service';
import { Room } from '../../core/models';

/** Above this many rooms, scanning the grid by eye stops being the quickest way to find one. */
const FILTER_THRESHOLD = 6;

/** The room catalogue: pick a room to see its schedule. */
@Component({
  selector: 'app-rooms',
  imports: [FormsModule, RouterLink],
  templateUrl: './rooms.html',
  styleUrl: './rooms.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoomsComponent {
  private readonly roomsApi = inject(RoomsService);

  protected readonly i18n = inject(TranslationService);

  protected readonly rooms = signal<Room[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** What the user has typed into the filter box. */
  protected readonly query = signal('');

  /** Placeholder cards drawn while the catalogue loads. */
  protected readonly skeletonCards = [0, 1, 2, 3];

  /**
   * Whether to offer the filter at all.
   *
   * A search box over three rooms is furniture: it takes vertical space, adds a control to tab
   * through, and answers a question nobody has. It earns its place once the grid no longer fits on
   * screen at a glance.
   */
  protected readonly filterable = computed(() => this.rooms().length > FILTER_THRESHOLD);

  /** The rooms to show, narrowed by the filter. */
  protected readonly visibleRooms = computed(() => {
    const query = this.query().trim().toLowerCase();

    if (!query) {
      return this.rooms();
    }

    // Location as well as name: "which rooms are on the third floor" is the other way people look
    // for a room, and it is the only other thing a card shows in words.
    return this.rooms().filter(
      (room) =>
        room.name.toLowerCase().includes(query) ||
        (room.location?.toLowerCase().includes(query) ?? false),
    );
  });

  constructor() {
    void this.load();
  }

  /** Reloads the catalogue after a failure. */
  protected async retry(): Promise<void> {
    await this.load();
  }

  /** Clears the filter, so a search that matched nothing is one click from the full list. */
  protected clearQuery(): void {
    this.query.set('');
  }

  /** Loads the catalogue. */
  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.rooms.set(await this.roomsApi.listRooms());
    } catch (error) {
      // The server's own explanation, not a canned sentence: "your session has expired" and "the
      // server could not be reached" call for different reactions from the user.
      this.error.set(describeError(error, (key, params) => this.i18n.t(key, params)));
    } finally {
      this.loading.set(false);
    }
  }
}
