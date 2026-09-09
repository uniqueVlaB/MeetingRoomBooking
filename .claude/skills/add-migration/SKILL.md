---
name: add-migration
description: Add an EF Core migration and verify the generated SQL, especially that the active-slot unique index and its filter survived. Use after changing an entity, an entity configuration, or anything under Infrastructure.SQL/Database.
---

# Adding a migration

## The commands

`MeetingRooms.Infrastructure.SQL` is **both** the project and the startup project. It owns the
design-time factory (`AppDbContextFactory`), so the tools need nothing else configured — the API does
not have to be startable, and no connection string has to be present.

```bash
dotnet ef migrations add <Name> \
  -p src/MeetingRooms.Infrastructure.SQL \
  -s src/MeetingRooms.Infrastructure.SQL \
  -o Database/Migrations
```

To undo one that has not been applied anywhere:

```bash
dotnet ef migrations remove -p src/MeetingRooms.Infrastructure.SQL -s src/MeetingRooms.Infrastructure.SQL
```

## Always verify the generated SQL

This is the point of the skill. The no-double-booking guarantee is a database object, so a migration
that quietly drops or alters it would remove the guarantee without breaking any build.

```bash
dotnet ef migrations script \
  -p src/MeetingRooms.Infrastructure.SQL \
  -s src/MeetingRooms.Infrastructure.SQL | grep -i ActiveSlot
```

Expected, and unchanged unless the change was deliberately about the index:

```sql
CREATE UNIQUE INDEX [UX_Bookings_ActiveSlot] ON [Bookings] ([TimeSlotId], [SlotDate]) WHERE [Status] = 0;
```

Check three things:

1. **`UNIQUE`** is present.
2. The **`WHERE [Status] = 0`** filter is present. Without it, cancelled bookings would block their
   slot forever; with it wrong, nothing would be enforced.
3. The columns are still `(TimeSlotId, SlotDate)`.

If the migration drops the index and recreates it, that is fine — as long as the recreated form
matches. If it drops it and does not recreate it, stop and work out why.

## Then run the tests

`ActiveSlotIndexTests` applies migrations to a fresh database and asserts the guarantee end to end,
so it is the real check that the schema still does its job:

```bash
dotnet test --filter "FullyQualifiedName~ActiveSlotIndex"
```

See the `concurrency-test` skill if the run cannot find a SQL Server.

## Applying it

- **Locally**: `Database:MigrateOnStartup` is `true` in Development, so simply running the AppHost
  applies pending migrations.
- **Azure**: the same setting is enabled on the Web App, so a deployment applies them at start-up.
  EF Core takes an exclusive migration lock, so two instances starting together cannot both apply
  the same migration. See `docs/deployment.md` for why the migration-bundle alternative was not used.
