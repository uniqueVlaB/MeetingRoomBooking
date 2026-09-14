# Concurrency control: how a slot is never double-booked

This is the deliberate design decision the task asks to be explained rather than left to ORM or
database defaults.

## The requirement

When several users request the same slot at effectively the same moment, exactly one must succeed.
Everyone else must get a clear conflict response — never a silent overwrite, never a server error.

## The mechanism: a filtered unique index

The guarantee lives in the database, not in application code:

```sql
CREATE UNIQUE INDEX UX_Bookings_ActiveSlot
    ON Bookings (TimeSlotId, SlotDate)
    WHERE [Status] = 0;   -- 0 = BookingStatus.Active
```

Declared in EF Core in
[`BookingConfiguration`](../src/MeetingRooms.Infrastructure.SQL/Database/Configuration/BookingConfiguration.cs):

```csharp
builder.HasIndex(b => new { b.TimeSlotId, b.SlotDate })
    .IsUnique()
    .HasFilter("[Status] = 0")
    .HasDatabaseName(AppDbContext.ActiveSlotIndexName);
```

Because the constraint is in the schema, booking is a **single INSERT**:

1. Add a `Booking` row and call `SaveChangesAsync`.
2. If it commits, the caller owns the slot. Return `201 Created`.
3. If SQL Server raises error **2601** or **2627** naming `UX_Bookings_ActiveSlot`, another request
   committed first. Return `409 Conflict`.

There is no "is this slot free?" query before the insert, so there is no window between the check
and the write for a competing transaction to slip through. Whichever request reaches the index
first wins; SQL Server serialises them on the index itself. The losers do not corrupt anything —
their insert simply never happens.

`BookAsync` does read the slot first, but only to establish that it **exists** and that its room is
still active. That read asks nothing about availability, so it is not the "check" half of a
check-then-insert. A comment says so at the call site, because that is the line a future change is
most likely to get wrong.

## Why not the alternatives

**Check-then-insert.** Explicitly ruled out by the task, and rightly: two requests can both read
"free" before either writes. Wrapping the pair in a transaction does not fix it at the default
`READ COMMITTED` isolation level, because the shared read lock is released before the insert.

**`SERIALIZABLE` transactions or `UPDLOCK, HOLDLOCK` hints.** These do work — the range lock
prevents the phantom — but they make correctness depend on every future caller remembering to take
the right lock in the right order, and they invite deadlocks under load. The index cannot be
forgotten: it applies to every writer, including a manual `INSERT` typed into a query window.

**Optimistic concurrency on a pre-materialised slot row.** Workable, but it requires a row to exist
for every room, for every day, forever, just so there is something to version. The unique index gets
the same guarantee without inventing rows for days nobody books.

**Application-level locks** (`SemaphoreSlim`, distributed cache locks). These fail as soon as the
Web App scales past one instance, which is exactly the deployment target here.

## Why the filter matters

Restricting the index to `Status = 0` means cancelled bookings do not occupy the slot. A user can
cancel and the slot becomes bookable again, while the cancelled row is preserved for history and
auditing. Without the filter, a cancelled booking would block its slot permanently.

The literal `0` couples the index to `BookingStatus.Active`. That coupling is documented on the
[enum](../src/MeetingRooms.Core/Entities/BookingStatus.cs): renumbering the members without a
migration that rebuilds the index would silently disable the guarantee. The same is true of the
`HasConversion<int>()` on `Booking.Status` — storing the status as a string would make the filter
`[Status] = 0` match nothing.

## Where `rowversion` fits

`Booking.RowVersion`, `Room.RowVersion` and `RefreshToken.RowVersion` are SQL Server `rowversion`
columns, giving EF Core optimistic concurrency on **updates**. That covers a different race — two
clients changing the same existing row — and is complementary to, not a substitute for, the unique
index, which is what protects **creation**.

## The same discipline elsewhere: refresh-token rotation

Redeeming a refresh token is a read-then-write: check the token is live, then revoke it and issue a
replacement. That is the shape this document spends its length arguing against, and it was present
in `RefreshTokenService.RotateAsync` with nothing guarding it. Two requests presenting the same
cookie — two tabs resuming at once, or a retried request — could both pass the check and both mint
a replacement. One captured cookie then becomes two independent live sessions, and the replay
detection the design promises never fires, because nothing ever sees a revoked token presented
twice.

There is no natural unique index to lean on here, so the guard is `rowversion` on `RefreshToken`:
the second writer's save fails with `DbUpdateConcurrencyException`, and the service **fails closed**,
returning the same "session expired" answer an unknown token gets. Failing closed matters — a
rotation that cannot prove it was the only one must not hand out a session.

`ConcurrentRefreshTests.SimultaneousRefreshes_RedeemTheTokenOnlyOnce` fires eight refreshes at one
cookie and asserts one `200`, seven `401`s, no `5xx`, and exactly one live token left for that
account.

## Broadcasting after the write

The second half of the requirement — every viewer sees the change immediately — has a failure of
its own worth naming. `BookingsController` broadcasts through `IBookingNotifier` **after** the write
commits, and passes `CancellationToken.None` rather than the request's token. The booking is already
committed by that point, so the send is no longer the caller's to cancel: with the request token, a
client that closed its tab in the window between commit and send would take the broadcast with it,
leaving every other viewer on a stale schedule with no error anywhere to show for it.

## How the conflict reaches the client

[`SqlServerConflictDetector`](../src/MeetingRooms.Infrastructure.SQL/Concurrency/SqlServerConflictDetector.cs)
recognises errors 2601 and 2627 and matches on the index name, so a slot conflict is never confused
with an unrelated duplicate such as a repeated room name. It is the only place in the solution that
knows SQL Server error numbers; the domain sees only
[`IDatabaseConflictDetector`](../src/MeetingRooms.Core/Abstractions/IDatabaseConflictDetector.cs),
which is why `MeetingRooms.Core` has no SQL Server dependency.

`BookingService` returns `OperationOutcome.Conflict` rather than throwing, and
[`OperationResultExtensions`](../src/MeetingRooms.Api/Infrastructure/OperationResultExtensions.cs)
maps it to `409` with an RFC 9457 problem-details body. Centralising that mapping is what keeps the
contract honest: a lost race always leaves through the same door and cannot drift into a 500 in one
controller and a 200 in another.

## How it is proven

| Test | What it establishes |
| --- | --- |
| `ActiveSlotIndexTests` | The database rejects a second active booking, allows the same slot on another date, frees the slot after a cancellation, and does not mistake a duplicate room name for a slot conflict. |
| `ConcurrentBookingTests.TwentySimultaneousRequests_CreateExactlyOneBooking` | 20 authenticated clients held at a barrier and released together produce exactly one `201`, nineteen `409`s, no `5xx`, and one active row. |
| `ConcurrentBookingTests.RepeatedRaces_NeverProduceASecondBooking` | Five further rounds, because one green run is weak evidence about a race. |
| `ConcurrentRefreshTests.SimultaneousRefreshes_RedeemTheTokenOnlyOnce` | Eight refreshes carrying one cookie produce one session, not two. |
| `BookingEndpointsTests.CancellingABooking_LetsAnotherUserBookTheSlot` | The end-to-end version: held, blocked with 409, cancelled, then bookable again. |

Observed output:

```
created=1 conflict=19 serverError=0 other=0
```

Both run against real SQL Server. The in-memory and SQLite providers would prove nothing here,
because the behaviour under test is a SQL Server index and the specific errors it raises.

Reading the result:

- Any `serverError` means a lost race escaped as an exception instead of being translated to `409`.
- `created` greater than 1 means the guarantee is gone — check that the migration still creates
  `UX_Bookings_ActiveSlot` with its `[Status] = 0` filter.
- `created` of 0 means every request failed; look at `other` and the response bodies.
