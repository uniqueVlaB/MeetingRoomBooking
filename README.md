# Meeting Room Booking System

A booking system for meeting rooms where several users may compete for the same time slot. Two
things must hold, and everything else is in service of them:

- **A slot is never double-booked.** Of any number of simultaneous requests for one slot, exactly
  one succeeds and the rest receive a clear `409` — never a silent overwrite, never a server error.
- **Everyone watching a schedule sees changes immediately**, without refreshing.

The first is guaranteed by a filtered unique index in the database, not by application logic, and is
proven by an automated test that fires twenty simultaneous requests at one slot. **The design and its
rejected alternatives are written up in [`docs/concurrency.md`](docs/concurrency.md)** — that is the
document to read first.

```
created=1 conflict=19 serverError=0 other=0
```

## Architecture

ASP.NET Core 10 API + Angular 21 client + SQL Server, orchestrated locally by .NET Aspire and
deployed to two Azure Web Apps.

| Project | Role |
| --- | --- |
| `src/MeetingRooms.AppHost` | .NET Aspire AppHost — starts SQL Server, the API and the client together. Development only. |
| `src/MeetingRooms.ServiceDefaults` | Shared telemetry, health checks, service discovery and HTTP resilience. |
| `src/MeetingRooms.Contracts` | Request and response shapes shared by the API, the tests and the client. |
| `src/MeetingRooms.Core` | Entities, booking rules, validation. No web and **no SQL Server dependency**. |
| `src/MeetingRooms.Infrastructure.SQL` | `AppDbContext`, the active-slot index, migrations, repositories, conflict detection. |
| `src/MeetingRooms.Api` | Controllers, the SignalR hub, authentication. |
| `tests/MeetingRooms.Tests` | xUnit integration tests against real SQL Server. |
| `client` | Angular workspace — standalone components, signals, lazy routes. |

Dependencies flow one way: `Api → Infrastructure.SQL → Core`. Core defines
`IDatabaseConflictDetector`; Infrastructure implements it with the SQL Server error numbers. That is
what lets the domain say "somebody else got the slot" without knowing what database it is running on.

| Concern | Choice |
| --- | --- |
| Concurrency | Filtered unique index; booking is one INSERT, a violation becomes `409` |
| Database | A local SQL Server instance (LocalDB by default) in development, Azure SQL in production |
| Real-time | SignalR, backed by Azure SignalR Service when a connection string is present |
| Auth | ASP.NET Identity, roles `User` / `Admin`, JWT access token + rotating refresh cookie |
| API docs | OpenAPI with Scalar at `/scalar/v1` in Development |
| Telemetry | OpenTelemetry through Aspire ServiceDefaults |

## Getting started

Prerequisites: **.NET 10 SDK**, **Node.js 20+**, and a local **SQL Server** instance — LocalDB, which
ships with Visual Studio and the SQL Server tooling, is used by default and needs no setup.

```bash
git clone <repository-url>
cd MeetingRoomBooking
dotnet run --project src/MeetingRooms.AppHost
```

That is the whole setup. Development defaults are built in for every secret, so there is nothing to
configure before the first run. The Aspire dashboard prints a login URL; from there the client is at
<http://localhost:4300>.

Seeded accounts (Development only):

| Account | Email | Password |
| --- | --- | --- |
| Administrator | `admin@meetingrooms.local` | `Admin!23456` |
| User | `user@meetingrooms.local` | `User!23456` |

### Using a different local SQL Server instance

The AppHost always connects to a local SQL Server instance rather than starting one in a Docker
container — a container's data volume can outlive the container, and a stale volume paired with a
regenerated `sa` password produces a confusing "login failed" error on every subsequent run for no
reason a developer caused. LocalDB is the default; to point at a named instance instead (SQL Server
Express, for example):

```powershell
cd src/MeetingRooms.AppHost
dotnet user-secrets set "ConnectionStrings:meetingrooms-db" "Server=.\SQLEXPRESS;Database=MeetingRooms;Trusted_Connection=True;TrustServerCertificate=True"
cd ../..
dotnet run --project src/MeetingRooms.AppHost
```

### Overriding the development secrets

Real values belong in the AppHost's user secrets, never in the repository:

```powershell
cd src/MeetingRooms.AppHost
dotnet user-secrets set "Parameters:JwtSigningKey"     "<at least 32 characters>"
dotnet user-secrets set "Parameters:SeedAdminPassword" "<password>"
dotnet user-secrets set "Parameters:SeedUserPassword"  "<password>"
```

### Where things are

| | |
| --- | --- |
| Angular client | <http://localhost:4300> |
| API | <https://localhost:7188> |
| API documentation (Scalar) | <https://localhost:7188/scalar/v1> |
| Health | <https://localhost:7188/health> |
| Aspire dashboard | printed at start-up, with a login token |

The API resource in the Aspire dashboard also carries a **Scalar API Docs** command — a one-click
way to open the docs once the health check goes green, instead of hunting down whatever port Aspire
assigned this run.

## Tests

```bash
dotnet test
```

The tests need a **real SQL Server**, because the behaviour under test is a filtered unique index
and the specific errors it raises — an in-memory provider would pass while proving nothing. This is
separate from the AppHost's local instance above: by default the test suite starts its own throwaway
SQL Server in a Docker container via Testcontainers, torn down after the run.

Without Docker, point the tests at an existing server. The fixture still creates and drops its own
uniquely named database, so it never touches existing data:

```powershell
$env:MEETINGROOMS_TEST_SQL = 'Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test
```

Just the concurrency tests:

```bash
dotnet test --filter "FullyQualifiedName~Concurrency|FullyQualifiedName~ActiveSlotIndex"
```

## API

| Endpoint | Who |
| --- | --- |
| `POST /api/auth/{register,login,refresh,logout}` | anyone |
| `GET /api/rooms`, `GET /api/rooms/{id}` | signed in |
| `GET /api/rooms/{id}/schedule?date=yyyy-MM-dd` | signed in |
| `POST/PUT/DELETE /api/rooms` | `Admin` |
| `POST /api/bookings` → `201` or **`409`** | signed in |
| `DELETE /api/bookings/{id}` | owner or `Admin` |
| `GET /api/bookings/mine` | signed in |
| `GET /api/bookings` | `Admin` |
| `/hubs/bookings` | signed in (SignalR) |

Live updates are scoped per room **and** date, so a browser showing Monday is not woken by a change
to Tuesday.

## Deployment

Pushing to `main` runs the full test suite and then deploys both apps to Azure. A change that
reintroduces double-booking cannot reach production, because the gate fails first.

Setting up the Azure resources, app settings and GitHub secrets is covered step by step in
[`docs/deployment.md`](docs/deployment.md) — including the cross-site refresh-cookie caveat, which
is the thing most likely to work locally and fail once the two halves are on separate origins.

## Working on this repository

[`CLAUDE.md`](CLAUDE.md) records the invariants that are not obvious from reading a single file —
above all, that a "check if free, then insert" pair must never be reintroduced. `.claude/skills/`
wraps the migration and concurrency-test workflows.
