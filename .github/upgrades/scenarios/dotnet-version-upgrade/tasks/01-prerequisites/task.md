# 01-prerequisites: Verify .NET 10 SDK and convert solution to .slnx

Verify the .NET 10 SDK is installed and that any global.json is compatible with .NET 10. Convert the existing `GolfStatsApp.sln` to the new XML-based `GolfStatsApp.slnx` format (e.g. via `dotnet sln migrate`) and remove the old `.sln` once the `.slnx` is verified to contain all 3 projects and solution items.

**Done when**: .NET 10 SDK confirmed, global.json (if any) compatible, `GolfStatsApp.slnx` exists with all projects, old `.sln` removed, and solution restores.
