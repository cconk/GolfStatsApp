# Database scripts (SQL Server)

The app uses SQL Server via EF Core; it no longer creates the schema on startup.
Run the scripts in SSMS against `(localdb)\MSSQLLocalDB` in order:

1. `01_create_database.sql` – creates the `GolfStatsApp` database (idempotent).
2. `02_create_tables.sql` – creates all tables, keys and indexes (run once on an empty database).

The default connection string lives in `Server/appsettings.json` (`ConnectionStrings:Default`). Override it
per machine with user secrets, e.g.:

```
dotnet user-secrets set "ConnectionStrings:Default" "Data Source=...;Initial Catalog=GolfStatsApp;..." --project Server
```

On first start the app seeds the `Teams` table from `Server/Data/teamstats.seed.json`.

## Regenerating `02_create_tables.sql`
When entities change, regenerate the script from the model:

```
dotnet ef dbcontext script --project Server/GolfStatsApp.Server.csproj -o database/tmp.sql
```

then prepend the `USE [GolfStatsApp]` header. For an existing database, write an incremental
`03_...sql` script with the `ALTER`/`CREATE` statements instead of re-running 02.

## Tests
The test suite still runs on throw-away SQLite files (`DatabaseProvider=Sqlite`), so no SQL Server is needed for `dotnet test`.
