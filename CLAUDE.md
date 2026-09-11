# CLAUDE.md — Meeting Room Booking System

Guidance for Claude Code when working in this repository.

## What this is

A booking system for meeting rooms where several users may compete for the same time slot. Two
things must hold: a slot is **never** double-booked, and every viewer of a schedule sees changes
**immediately**. Everything else is in service of those two.

ASP.NET Core 10 API + Angular 21 client + SQL Server, orchestrated locally by .NET Aspire and
deployed as two Azure Web Apps.

## Layout

| Path | What lives there |
| --- | --- |
| `src/MeetingRooms.AppHost` | Aspire orchestration. Development only — deployment goes through GitHub Actions. |
| `src/MeetingRooms.Core` | Entities, booking rules, validation. **No web dependency, and no SQL Server dependency.** |
| `src/MeetingRooms.Infrastructure.SQL` | `AppDbContext`, the active-slot index, migrations, repositories. |
| `src/MeetingRooms.Api` | Controllers, the SignalR hub, auth wiring, DI. |
| `src/MeetingRooms.Contracts` | DTOs shared by the API, the tests and (mirrored in) the client. |
| `tests/MeetingRooms.Tests` | xUnit tests against real SQL Server. |
| `client` | Angular workspace. |
| `docs/concurrency.md` | **Read this before touching booking logic.** |
| `docs/deployment.md` | Azure resources, app settings, the cross-site cookie caveat. |

## Commands

```bash
# Run everything (API, Angular). Connects to local SQL Server (LocalDB by default -- see
# src/MeetingRooms.AppHost/appsettings.json), never a Docker container; see the comment on the
# database resource in AppHost.cs for why.
dotnet run --project src/MeetingRooms.AppHost

dotnet build MeetingRoomBooking.slnx      # warnings are errors
dotnet test                               # needs Docker (for Testcontainers), or set MEETINGROOMS_TEST_SQL

cd client && npm start                    # client alone, proxying /api and /hubs
cd client && npm run build

# Migrations. Infrastructure.SQL is both the project and the startup project, because it owns the
# design-time factory and nothing else needs to be configured for the tools to run.
dotnet ef migrations add <Name> -p src/MeetingRooms.Infrastructure.SQL -s src/MeetingRooms.Infrastructure.SQL -o Database/Migrations
```

## The rule that matters most

**Never introduce a "check if the slot is free, then insert" pair.** The no-double-booking guarantee
is the filtered unique index `UX_Bookings_ActiveSlot`; booking is a single `INSERT` whose failure is
translated to `409`. Reading first to produce a nicer error message re-opens exactly the race the
design removes, while looking like an improvement. `docs/concurrency.md` explains why, and what was
rejected.

`BookAsync` does read the slot before inserting, but only to check that it **exists** and its room is
active. That read must never grow into an availability check.

Related invariants:

- `BookingStatus.Active` **must** stay `0`, and `Booking.Status` must stay mapped with
  `HasConversion<int>()`. The index filter is `[Status] = 0`. Changing either without a migration
  that rebuilds the index silently disables the guarantee rather than failing loudly.
- A lost race is `409`, never `500`. Services return `OperationResult`, they do not throw;
  `OperationResultExtensions` is the only place outcomes become status codes.
- Cancel by setting `Status = Cancelled`, never by deleting the row. The index filter is what
  releases the slot, and the row is history.
- Broadcast **after** the write commits, and through `IBookingNotifier` rather than `IHubContext`,
  so flows stay testable.

## Conventions

- Warnings are errors (`Directory.Build.props`), and public members carry XML documentation.
- Package versions live in `Directory.Packages.props`. Do not add a `Version` to a `PackageReference`.
- Core must not gain a SQL Server dependency. Provider-specific knowledge goes behind an abstraction
  in `Core/Abstractions` and is implemented in `Infrastructure.SQL`.
- Controllers do not touch `AppDbContext`; writes go through a service in `Core`.
- Entities never cross the HTTP boundary; map to a contract in `MeetingRooms.Contracts`.
- Simple shape validation is data annotations on the contract. FluentValidation is only for rules
  spanning several fields, so no rule is maintained in two places. On a positional record, write
  `[Required] string Name` — **not** `[property: Required]`, which ASP.NET rejects at runtime.
- Controller actions are named `...Async`, but ASP.NET strips that suffix from the route's action
  name. Use `CreatedAtRoute` with a named route rather than `CreatedAtAction(nameof(...Async))`.
- Comments explain **why**, not what. If a line is surprising, say what it is defending against.
- No secrets in the repository. Development defaults are obviously-fake placeholders; real values go
  in AppHost user secrets locally and Web App settings in Azure.
- Client: standalone components, signals, `OnPush`, lazy routes. TypeScript models mirror server
  contracts exactly. The access token stays in memory; never put it in `localStorage`.

## Commits

Atomic, one concern each, and the message says **what changed and why** — the "why" is the part a
reviewer cannot reconstruct from the diff. Prefix with `feat` / `fix` / `chore` / `test` / `docs`.

## Working on this repository

- Read `docs/concurrency.md` before changing anything under `Services`, `Bookings`, or the schema.
- After a schema change, add a migration and **check the generated SQL** — especially that the index
  filter survived:
  `dotnet ef migrations script -p src/MeetingRooms.Infrastructure.SQL -s src/MeetingRooms.Infrastructure.SQL | grep ActiveSlot`
- `dotnet test` needs a real SQL Server. Docker is the default; `MEETINGROOMS_TEST_SQL` points the
  run at an existing instance instead.
- A single green concurrency run is weak evidence about a race. Repeat it.
- Custom skills in `.claude/skills/` wrap the migration and concurrency-test workflows.
