# GolfStatsApp
Fantasy and other golf stats and visualization

## Features
- SQLite-backed team stats with an admin UI for editing results and player points
- Accounts, private leagues (join codes), snake drafts and a leaderboard
- Installable PWA, dark mode, mobile-friendly tables
- CSV / Excel export of stats tables and league leaderboards

## Development
- Requires the .NET 10 SDK.
- Run: `dotnet run --project Server/GolfStatsApp.Server.csproj`
- Test: `dotnet test Tests/GolfStatsApp.Tests.csproj`
- Database: `Server/App_Data/golfstats.db` by default; override with `ConnectionStrings:Default`.
- Admin: the first registered user, or any email listed under `Admin:Emails`, is an admin.
- CI: `.github/workflows/ci.yml` builds and tests on PRs and branch pushes.
