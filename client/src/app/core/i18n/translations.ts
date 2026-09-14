/**
 * Every string the application shows, in English and Ukrainian.
 *
 * A flat, dot-path key rather than a nested object: a nested tree needs a type-level "flatten" just
 * to let `TranslationService.t()` accept a checked key, and a flat `Record` gets the same safety —
 * `TranslationKey` below is `keyof typeof translations.en` — for a fraction of the type machinery.
 * English is the source of truth; a key missing from `uk` falls back to it rather than showing raw
 * text, so a half-finished translation degrades instead of breaking.
 *
 * Plural forms live under `<base>.one` / `.few` / `.many` / `.other` and are read through
 * `TranslationService.plural()`, never `t()` directly — English distinguishes one thing from
 * everything else, but Ukrainian has three grammatical plural forms (1 day → "1 день", 2–4 →
 * "2 дні", 5+ → "5 днів", with exceptions at 11–14), so a single `{{count}} day{{s}}` template that
 * works for English cannot be made to work for Ukrainian by interpolation alone.
 */
export const translations = {
  en: {
    // ── Shell ───────────────────────────────────────────────────────────────────────────────────
    'shell.skipLink': 'Skip to content',
    'shell.brand': 'Meeting Rooms',
    'shell.nav.ariaLabel': 'Main',
    'shell.nav.rooms': 'Rooms',
    'shell.nav.myBookings': 'My bookings',
    'shell.nav.admin': 'Admin',
    'shell.adminBadge': 'Admin',
    'shell.signOut': 'Sign out',
    'shell.locale.ariaLabel': 'Language',
    'shell.theme.ariaLabel': 'Theme',
    'shell.theme.auto': 'Auto',
    'shell.theme.light': 'Light',
    'shell.theme.dark': 'Dark',
    'shell.feedback.button': 'Feedback',
    'shell.feedback.ariaLabel': 'Open notes for reviewers',
    'shell.feedback.title': 'Notes for reviewers',
    'shell.feedback.close': 'Close',

    // ── Common ──────────────────────────────────────────────────────────────────────────────────
    'common.tryAgain': 'Try again',
    'common.retired': 'Retired',
    'common.working': 'Working…',
    'common.cancel': 'Cancel',
    'common.keep': 'Keep',

    // ── Client-side error fallbacks (a server's own `detail`/`title` is shown verbatim and is not
    //    translated here — only the two sentences the client itself puts up when there is none) ──
    'errors.offline': 'The server could not be reached. Check your connection and try again.',
    'errors.requestFailed': 'Request failed ({{status}}).',
    'errors.generic': 'Something went wrong. Please try again.',

    // ── Relative day naming, shared by the schedule, bookings list and admin panel ─────────────
    'date.today': 'Today',
    'date.tomorrow': 'Tomorrow',
    'date.yesterday': 'Yesterday',

    // ── Route titles (browser tab) ─────────────────────────────────────────────────────────────
    'routes.signIn': 'Sign in',
    'routes.rooms': 'Rooms',
    'routes.schedule': 'Schedule',
    'routes.myBookings': 'My bookings',
    'routes.admin': 'Administration',

    // ── Rooms ───────────────────────────────────────────────────────────────────────────────────
    'rooms.title': 'Rooms',
    'rooms.subtitle': 'Choose a room to see which slots are free.',
    'rooms.filter.label': 'Filter rooms',
    'rooms.filter.placeholder': 'Filter by name or location',
    'rooms.loading.ariaLabel': 'Loading rooms',
    'rooms.error.title': 'The rooms could not be loaded.',
    'rooms.empty.title': 'No rooms yet.',
    'rooms.empty.detail': 'An administrator can add one from the Admin page.',
    'rooms.noMatch.title': 'No room matches “{{query}}”.',
    'rooms.noMatch.detail': 'Try part of a room’s name, or the floor or building it is on.',
    'rooms.noMatch.clear': 'Clear the filter',
    'rooms.seats': 'Seats {{count}}',
    'rooms.slotsPerDay.one': '{{count}} slot/day',
    'rooms.slotsPerDay.other': '{{count}} slots/day',
    'rooms.slotsPerDay.few': '{{count}} slots/day',
    'rooms.slotsPerDay.many': '{{count}} slots/day',

    // ── Schedule ────────────────────────────────────────────────────────────────────────────────
    'schedule.fallbackTitle': 'Schedule',
    'schedule.backToRooms': '← All rooms',
    'schedule.live.connected': 'Live',
    'schedule.live.reconnecting': 'Reconnecting…',
    'schedule.live.offline': 'Offline',
    'schedule.offlineNotice':
      'Live updates have stopped, so other people’s changes will not appear.',
    'schedule.reconnect': 'Reconnect',
    'schedule.prevDay': 'Previous day',
    'schedule.nextDay': 'Next day',
    'schedule.dateInput.ariaLabel': 'Date',
    'schedule.today': 'Today',
    'schedule.pastNotice': 'This day has already passed; it is shown for reference.',
    'schedule.loading.ariaLabel': 'Loading schedule',
    'schedule.loadError.title': 'The schedule could not be loaded.',
    'schedule.loadError.detail': 'The server did not answer, or the session has expired.',
    'schedule.empty.title': 'This room has no bookable slots.',
    'schedule.empty.detail': 'An administrator sets a room’s daily slots when creating it.',
    'schedule.freeSummary': '{{free}} of {{total}} slots free',
    'schedule.bookedByYou': 'Booked by you',
    'schedule.booked': 'Booked',
    'schedule.free': 'Free',
    'schedule.book': 'Book',
    'schedule.hubUnavailable':
      'Live updates are unavailable; reload to see other people’s changes.',
    'schedule.bookedNotice': 'Booked {{start}}–{{end}}.',
    'schedule.conflictNotice':
      'Somebody else booked that slot a moment before you. The schedule has been updated.',
    'schedule.cancelledNotice': 'Booking cancelled; the slot is free again.',

    // ── My bookings ─────────────────────────────────────────────────────────────────────────────
    'myBookings.title': 'My bookings',
    'myBookings.subtitleDefault': 'Slots you are currently holding.',
    'myBookings.subtitle': '{{slots}} across {{days}}.',
    'myBookings.slotsPhrase.one': '{{count}} slot',
    'myBookings.slotsPhrase.other': '{{count}} slots',
    'myBookings.slotsPhrase.few': '{{count}} slots',
    'myBookings.slotsPhrase.many': '{{count}} slots',
    'myBookings.daysPhrase.one': '{{count}} day',
    'myBookings.daysPhrase.other': '{{count}} days',
    'myBookings.daysPhrase.few': '{{count}} days',
    'myBookings.daysPhrase.many': '{{count}} days',
    'myBookings.loading.ariaLabel': 'Loading your bookings',
    'myBookings.loadError.title': 'Your bookings could not be loaded.',
    'myBookings.loadError.detail': 'The server did not answer, or the session has expired.',
    'myBookings.empty.title': 'You have no bookings.',
    'myBookings.empty.detail': 'Pick a room to see which of its slots are still free today.',
    'myBookings.empty.findRoom': 'Find a room',
    'myBookings.cancel': 'Cancel',
    'myBookings.cancelling': 'Cancelling…',

    // ── Admin ───────────────────────────────────────────────────────────────────────────────────
    'admin.title': 'Administration',
    'admin.subtitle': 'Manage rooms and see every booking.',
    'admin.addRoom.heading': 'Add a room',
    'admin.field.name': 'Name',
    'admin.field.location': 'Location',
    'admin.field.seats': 'Seats',
    'admin.field.opens': 'Opens',
    'admin.field.closes': 'Closes',
    'admin.field.slotLength': 'Slot length (min)',
    'admin.create': 'Create room',
    'admin.creating': 'Creating…',
    'admin.preview.prefix': 'Creates',
    'admin.preview.unit.one': 'slot',
    'admin.preview.unit.other': 'slots',
    'admin.preview.unit.few': 'slots',
    'admin.preview.unit.many': 'slots',
    'admin.preview.range': '{{start}} to {{end}}.',
    'admin.preview.empty':
      'These hours produce no slots. The closing hour must be at least one slot length after the opening hour.',
    'admin.rooms.heading': 'Rooms',
    'admin.rooms.loading.ariaLabel': 'Loading rooms',
    'admin.rooms.error.title': 'Rooms could not be loaded.',
    'admin.rooms.empty.title': 'No rooms yet.',
    'admin.rooms.empty.detail': 'Add one with the form above.',
    'admin.rooms.summary': '{{slots}} slots · seats {{capacity}}',
    'admin.retire': 'Retire',
    'admin.restore': 'Restore',
    'admin.delete': 'Delete',
    'admin.confirmDelete': 'Confirm delete',
    'admin.bookings.heading': 'All bookings',
    'admin.bookings.loading.ariaLabel': 'Loading bookings',
    'admin.bookings.error.title': 'Bookings could not be loaded.',
    'admin.bookings.empty.title': 'Nothing is booked.',
    'admin.confirmCancel': 'Confirm cancel',
    'admin.createdNotice': 'Created {{name}}.',
    'admin.restoredNotice': 'Restored {{name}}.',
    'admin.retiredNotice': 'Retired {{name}}.',
    'admin.deletedRetiredNotice':
      'Retired {{name}}. It has bookings, so the room is kept for their history and simply accepts no new ones.',
    'admin.deletedRemovedNotice': 'Removed {{name}}.',
    'admin.cancelledBookingNotice': 'Cancelled the {{room}} booking.',

    // ── Login ───────────────────────────────────────────────────────────────────────────────────
    'login.signIn.title': 'Sign in',
    'login.register.title': 'Create an account',
    'login.signIn.subtitle': 'Book a meeting room.',
    'login.register.subtitle': 'New accounts can view rooms and book slots.',
    'login.field.displayName': 'Display name',
    'login.field.email': 'Email',
    'login.field.password': 'Password',
    'login.showPassword': 'Show',
    'login.hidePassword': 'Hide',
    'login.passwordHint': 'At least {{count}} characters.',
    'login.submit.signIn': 'Sign in',
    'login.submit.register': 'Create account',
    'login.submit.busy': 'Please wait…',
    'login.toggle.toSignIn': 'I already have an account',
    'login.toggle.toRegister': 'Create an account instead',
    'login.validate.displayName': 'Enter a display name of at least {{count}} characters.',
    'login.validate.email': 'Enter an email address.',
    'login.validate.password': 'Enter your password.',
    'login.validate.passwordLength': 'Choose a password of at least {{count}} characters.',
  },
  uk: {
    // ── Shell ───────────────────────────────────────────────────────────────────────────────────
    'shell.skipLink': 'Перейти до вмісту',
    'shell.brand': 'Кімнати для нарад',
    'shell.nav.ariaLabel': 'Основна навігація',
    'shell.nav.rooms': 'Кімнати',
    'shell.nav.myBookings': 'Мої бронювання',
    'shell.nav.admin': 'Адміністрування',
    'shell.adminBadge': 'Адміністратор',
    'shell.signOut': 'Вийти',
    'shell.locale.ariaLabel': 'Мова',
    'shell.theme.ariaLabel': 'Тема',
    'shell.theme.auto': 'Авто',
    'shell.theme.light': 'Світла',
    'shell.theme.dark': 'Темна',
    'shell.feedback.button': 'Відгук',
    'shell.feedback.ariaLabel': 'Відкрити нотатки для рецензентів',
    'shell.feedback.title': 'Нотатки для рецензентів',
    'shell.feedback.close': 'Закрити',

    // ── Common ──────────────────────────────────────────────────────────────────────────────────
    'common.tryAgain': 'Спробувати ще раз',
    'common.retired': 'Виведено з експлуатації',
    'common.working': 'Обробка…',
    'common.cancel': 'Скасувати',
    'common.keep': 'Залишити',

    // ── Client-side error fallbacks ────────────────────────────────────────────────────────────
    'errors.offline':
      'Не вдалося з’єднатися із сервером. Перевірте підключення та спробуйте ще раз.',
    'errors.requestFailed': 'Запит не виконано ({{status}}).',
    'errors.generic': 'Щось пішло не так. Спробуйте ще раз.',

    // ── Relative day naming ─────────────────────────────────────────────────────────────────────
    'date.today': 'Сьогодні',
    'date.tomorrow': 'Завтра',
    'date.yesterday': 'Вчора',

    // ── Route titles (browser tab) ─────────────────────────────────────────────────────────────
    'routes.signIn': 'Вхід',
    'routes.rooms': 'Кімнати',
    'routes.schedule': 'Розклад',
    'routes.myBookings': 'Мої бронювання',
    'routes.admin': 'Адміністрування',

    // ── Rooms ───────────────────────────────────────────────────────────────────────────────────
    'rooms.title': 'Кімнати',
    'rooms.subtitle': 'Оберіть кімнату, щоб побачити вільні слоти.',
    'rooms.filter.label': 'Фільтр кімнат',
    'rooms.filter.placeholder': 'Фільтр за назвою або розташуванням',
    'rooms.loading.ariaLabel': 'Завантаження кімнат',
    'rooms.error.title': 'Не вдалося завантажити кімнати.',
    'rooms.empty.title': 'Кімнат поки немає.',
    'rooms.empty.detail': 'Адміністратор може додати кімнату на сторінці адміністрування.',
    'rooms.noMatch.title': 'Жодна кімната не відповідає запиту «{{query}}».',
    'rooms.noMatch.detail':
      'Спробуйте частину назви кімнати, поверх або будівлю, де вона розташована.',
    'rooms.noMatch.clear': 'Очистити фільтр',
    'rooms.seats': 'Місць: {{count}}',
    'rooms.slotsPerDay.one': '{{count}} слот на день',
    'rooms.slotsPerDay.few': '{{count}} слоти на день',
    'rooms.slotsPerDay.many': '{{count}} слотів на день',
    'rooms.slotsPerDay.other': '{{count}} слоти на день',

    // ── Schedule ────────────────────────────────────────────────────────────────────────────────
    'schedule.fallbackTitle': 'Розклад',
    'schedule.backToRooms': '← Усі кімнати',
    'schedule.live.connected': 'Наживо',
    'schedule.live.reconnecting': 'Повторне підключення…',
    'schedule.live.offline': 'Немає з’єднання',
    'schedule.offlineNotice':
      'Оновлення в реальному часі зупинено, тому зміни інших користувачів не відображатимуться.',
    'schedule.reconnect': 'Перепідключитися',
    'schedule.prevDay': 'Попередній день',
    'schedule.nextDay': 'Наступний день',
    'schedule.dateInput.ariaLabel': 'Дата',
    'schedule.today': 'Сьогодні',
    'schedule.pastNotice': 'Цей день уже минув; показано лише для довідки.',
    'schedule.loading.ariaLabel': 'Завантаження розкладу',
    'schedule.loadError.title': 'Не вдалося завантажити розклад.',
    'schedule.loadError.detail': 'Сервер не відповів, або сеанс завершився.',
    'schedule.empty.title': 'У цій кімнаті немає слотів для бронювання.',
    'schedule.empty.detail': 'Адміністратор встановлює денні слоти кімнати під час її створення.',
    'schedule.freeSummary': '{{free}} із {{total}} слотів вільні',
    'schedule.bookedByYou': 'Заброньовано вами',
    'schedule.booked': 'Заброньовано',
    'schedule.free': 'Вільно',
    'schedule.book': 'Забронювати',
    'schedule.hubUnavailable':
      'Оновлення в реальному часі недоступні; перезавантажте сторінку, щоб побачити зміни інших користувачів.',
    'schedule.bookedNotice': 'Заброньовано {{start}}–{{end}}.',
    'schedule.conflictNotice': 'Хтось інший щойно забронював цей слот. Розклад оновлено.',
    'schedule.cancelledNotice': 'Бронювання скасовано; слот знову вільний.',

    // ── My bookings ─────────────────────────────────────────────────────────────────────────────
    'myBookings.title': 'Мої бронювання',
    'myBookings.subtitleDefault': 'Слоти, які ви наразі утримуєте.',
    'myBookings.subtitle': '{{slots}} за {{days}}.',
    'myBookings.slotsPhrase.one': '{{count}} слот',
    'myBookings.slotsPhrase.few': '{{count}} слоти',
    'myBookings.slotsPhrase.many': '{{count}} слотів',
    'myBookings.slotsPhrase.other': '{{count}} слоти',
    'myBookings.daysPhrase.one': '{{count}} день',
    'myBookings.daysPhrase.few': '{{count}} дні',
    'myBookings.daysPhrase.many': '{{count}} днів',
    'myBookings.daysPhrase.other': '{{count}} дні',
    'myBookings.loading.ariaLabel': 'Завантаження ваших бронювань',
    'myBookings.loadError.title': 'Не вдалося завантажити ваші бронювання.',
    'myBookings.loadError.detail': 'Сервер не відповів, або сеанс завершився.',
    'myBookings.empty.title': 'У вас немає бронювань.',
    'myBookings.empty.detail': 'Оберіть кімнату, щоб побачити, які слоти ще вільні сьогодні.',
    'myBookings.empty.findRoom': 'Знайти кімнату',
    'myBookings.cancel': 'Скасувати',
    'myBookings.cancelling': 'Скасування…',

    // ── Admin ───────────────────────────────────────────────────────────────────────────────────
    'admin.title': 'Адміністрування',
    'admin.subtitle': 'Керуйте кімнатами та переглядайте всі бронювання.',
    'admin.addRoom.heading': 'Додати кімнату',
    'admin.field.name': 'Назва',
    'admin.field.location': 'Розташування',
    'admin.field.seats': 'Місць',
    'admin.field.opens': 'Відкривається',
    'admin.field.closes': 'Закривається',
    'admin.field.slotLength': 'Тривалість слоту (хв)',
    'admin.create': 'Створити кімнату',
    'admin.creating': 'Створення…',
    'admin.preview.prefix': 'Створює',
    'admin.preview.unit.one': 'слот',
    'admin.preview.unit.few': 'слоти',
    'admin.preview.unit.many': 'слотів',
    'admin.preview.unit.other': 'слоти',
    'admin.preview.range': 'з {{start}} до {{end}}.',
    'admin.preview.empty':
      'Ці години не створюють жодного слоту. Час закриття має бути щонайменше на один слот пізніше за час відкриття.',
    'admin.rooms.heading': 'Кімнати',
    'admin.rooms.loading.ariaLabel': 'Завантаження кімнат',
    'admin.rooms.error.title': 'Не вдалося завантажити кімнати.',
    'admin.rooms.empty.title': 'Кімнат поки немає.',
    'admin.rooms.empty.detail': 'Додайте кімнату за допомогою форми вище.',
    'admin.rooms.summary': '{{slots}} слотів · місць {{capacity}}',
    'admin.retire': 'Вивести з експлуатації',
    'admin.restore': 'Відновити',
    'admin.delete': 'Видалити',
    'admin.confirmDelete': 'Підтвердити видалення',
    'admin.bookings.heading': 'Усі бронювання',
    'admin.bookings.loading.ariaLabel': 'Завантаження бронювань',
    'admin.bookings.error.title': 'Не вдалося завантажити бронювання.',
    'admin.bookings.empty.title': 'Нічого не заброньовано.',
    'admin.confirmCancel': 'Підтвердити скасування',
    'admin.createdNotice': 'Створено кімнату «{{name}}».',
    'admin.restoredNotice': 'Відновлено кімнату «{{name}}».',
    'admin.retiredNotice': 'Кімнату «{{name}}» виведено з експлуатації.',
    'admin.deletedRetiredNotice':
      'Кімнату «{{name}}» виведено з експлуатації. У неї є бронювання, тому вона зберігається заради історії та більше не приймає нових.',
    'admin.deletedRemovedNotice': 'Видалено кімнату «{{name}}».',
    'admin.cancelledBookingNotice': 'Скасовано бронювання кімнати «{{room}}».',

    // ── Login ───────────────────────────────────────────────────────────────────────────────────
    'login.signIn.title': 'Увійти',
    'login.register.title': 'Створити обліковий запис',
    'login.signIn.subtitle': 'Забронюйте кімнату для нарад.',
    'login.register.subtitle':
      'Новий обліковий запис дозволяє переглядати кімнати та бронювати слоти.',
    'login.field.displayName': 'Ім’я для відображення',
    'login.field.email': 'Електронна пошта',
    'login.field.password': 'Пароль',
    'login.showPassword': 'Показати',
    'login.hidePassword': 'Приховати',
    'login.passwordHint': 'Щонайменше {{count}} символів.',
    'login.submit.signIn': 'Увійти',
    'login.submit.register': 'Створити обліковий запис',
    'login.submit.busy': 'Зачекайте…',
    'login.toggle.toSignIn': 'У мене вже є обліковий запис',
    'login.toggle.toRegister': 'Створити обліковий запис натомість',
    'login.validate.displayName':
      'Введіть ім’я для відображення, що містить щонайменше {{count}} символів.',
    'login.validate.email': 'Введіть електронну адресу.',
    'login.validate.password': 'Введіть пароль.',
    'login.validate.passwordLength': 'Оберіть пароль, що містить щонайменше {{count}} символів.',
  },
} as const;

/** A locale the application can be shown in. */
export type Locale = keyof typeof translations;

/** Every key `TranslationService.t()` and `.plural()` will accept. */
export type TranslationKey = keyof typeof translations.en;
