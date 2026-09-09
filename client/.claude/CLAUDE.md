
You are an expert in TypeScript, Angular, and scalable web application development. You write functional, maintainable, performant, and accessible code following Angular and TypeScript best practices.

## TypeScript Best Practices

- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain

## Angular Best Practices

- Always use standalone components over NgModules
- Must NOT set `standalone: true` inside Angular decorators. It's the default in Angular v20+.
- Use signals for state management
- Implement lazy loading for feature routes
- Do NOT use the `@HostBinding` and `@HostListener` decorators. Put host bindings inside the `host` object of the `@Component` or `@Directive` decorator instead
- Use `NgOptimizedImage` for all static images.
  - `NgOptimizedImage` does not work for inline base64 images.

## Accessibility Requirements

- It MUST pass all AXE checks.
- It MUST follow all WCAG AA minimums, including focus management, color contrast, and ARIA attributes.

### Components

- Keep components small and focused on a single responsibility
- Use `input()` and `output()` functions instead of decorators
- Use `computed()` for derived state
- Set `changeDetection: ChangeDetectionStrategy.OnPush` in `@Component` decorator
- Prefer inline templates for small components
- Prefer Reactive forms instead of Template-driven ones
- Do NOT use `ngClass`, use `class` bindings instead
- Do NOT use `ngStyle`, use `style` bindings instead
- When using external templates/styles, use paths relative to the component TS file.

## State Management

- Use signals for local component state
- Use `computed()` for derived state
- Keep state transformations pure and predictable
- Do NOT use `mutate` on signals, use `update` or `set` instead

## Templates

- Keep templates simple and avoid complex logic
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`
- Use the async pipe to handle observables
- Do not assume globals like (`new Date()`) are available.
- Do not write arrow functions in templates (they are not supported).

## Services

- Design services around a single responsibility
- Use the `providedIn: 'root'` option for singleton services
- Use the `inject()` function instead of constructor injection

## This project specifically

See the repository root `CLAUDE.md` for the rules that span both halves of the system.

### Structure

- `core/config` — `AppConfig`, loaded from `public/config.json` at start-up. **All API URLs come
  from `AppConfig.apiBaseUrl`.** Never hardcode a host or import an `environment` file for it; the
  API lives on a different origin in Azure and the URL is configuration, not a build input.
- `core/auth` — session, HTTP interceptor, route guards.
- `core/api` — thin wrappers over the REST endpoints, returning promises.
- `core/realtime` — the shared SignalR connection.
- `core/models` — TypeScript mirrors of `MeetingRooms.Contracts`. They must match the server exactly;
  a drifting field name fails silently at runtime rather than at build time.
- `features/*` — one lazily loaded component per screen.

### Rules that are easy to break

- **The access token stays in memory.** Never write it to `localStorage` or `sessionStorage`. Surviving
  a reload is the HttpOnly refresh cookie's job, via `AuthService.restore()`.
- Requests to `/api/auth/*` need `withCredentials: true`, or the refresh cookie is neither stored nor
  sent. Everything else gets its `Authorization` header from `authInterceptor` — do not add it by hand.
- `AuthService.restore()` memoises its in-flight promise. Refresh tokens rotate on use, so concurrent
  refreshes would invalidate each other and sign the user out. Do not remove the memoisation.
- After a SignalR reconnect, **rejoin the group**. Membership does not survive a reconnect, and the
  symptom — updates silently stopping — looks like nothing is wrong.
- A `409` from `POST /api/bookings` is an expected outcome, not a failure: another user won the slot.
  Show a plain explanation and reload the schedule; never surface it as an error dump.
- Dates are `yyyy-MM-dd` strings and times are `HH:mm:ss` strings. Do not convert them to `Date`;
  neither value carries a time zone, and converting invents one.
