---
name: concurrency-test
description: Run the double-booking concurrency tests, with or without Docker. Use when verifying the no-double-booking guarantee, after changing booking logic or the Bookings schema, or when the tests fail to start because no SQL Server is reachable.
---

# Running the concurrency tests

## Why this skill exists

These tests need a **real SQL Server**: the behaviour under test is a filtered unique index and the
specific errors (2601 / 2627) it raises. The in-memory and SQLite providers would pass while proving
nothing. The fixture has two ways to get a server, and picking the wrong one is the usual reason a
run fails before any test executes.

## Option A — Docker (default)

Testcontainers starts a throwaway SQL Server. Requires Docker to be running.

```bash
dotnet test
```

## Option B — an existing SQL Server (no Docker)

Set `MEETINGROOMS_TEST_SQL` to a **server** connection string. The fixture creates its own uniquely
named database on that server and drops it afterwards, so it never touches existing data.

```powershell
# PowerShell
$env:MEETINGROOMS_TEST_SQL = 'Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test
```

```bash
# bash
export MEETINGROOMS_TEST_SQL='Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test
```

## Just the concurrency tests

```bash
dotnet test --filter "FullyQualifiedName~Concurrency|FullyQualifiedName~ActiveSlotIndex"
```

## Reading the result

- `ActiveSlotIndexTests` — must always pass. It proves the database itself rejects a duplicate active
  booking. If it fails, the schema guarantee is broken; fix that before anything else.
- `ConcurrentBookingTests.TwentySimultaneousRequests_CreateExactlyOneBooking` — the test the task
  requires.

Expected output:

```
created=1 conflict=19 serverError=0 other=0
```

- Any `serverError` means a lost race escaped as an exception instead of being translated to `409`.
  Check the `DbUpdateException` handling in `BookingService` against `docs/concurrency.md`.
- `created` greater than 1 means the guarantee is gone — check that the migration still creates
  `UX_Bookings_ActiveSlot` with its `[Status] = 0` filter, and that `Booking.Status` is still stored
  as an int.
- `created` of 0 means every request failed; look at `other` and the response bodies.

## Before concluding "it works"

A single green run is weak evidence about a race condition. Repeat it:

```bash
for i in 1 2 3 4 5; do dotnet test --filter "FullyQualifiedName~Concurrency" || break; done
```

`RepeatedRaces_NeverProduceASecondBooking` already runs five rounds inside one test, but repeating
the whole run varies the timing further.
