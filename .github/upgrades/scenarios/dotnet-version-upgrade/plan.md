# .NET 10 Upgrade Plan

## Overview

**Target**: Upgrade GolfStatsApp (Blazor WebAssembly hosted: Server, Client, Shared) from net8.0 to net10.0 and convert GolfStatsApp.sln to GolfStatsApp.slnx
**Scope**: 3 SDK-style projects, ~900 LOC, 6 packages to upgrade, 6 behavioral-change API notes

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: 3 projects, all on .NET 8, shallow dependency graph (Server → Client → Shared), no incompatible packages.

## Tasks

### 01-prerequisites: Verify .NET 10 SDK and convert solution to .slnx

Verify the .NET 10 SDK is installed and that any global.json is compatible with .NET 10. Convert the existing `GolfStatsApp.sln` to the new XML-based `GolfStatsApp.slnx` format (e.g. via `dotnet sln migrate`) and remove the old `.sln` once the `.slnx` is verified to contain all 3 projects and solution items.

**Done when**: .NET 10 SDK confirmed, global.json (if any) compatible, `GolfStatsApp.slnx` exists with all projects, old `.sln` removed, and solution restores.

---

### 02-upgrade-all-projects: Upgrade Shared, Client, and Server to net10.0

Update `TargetFramework` to `net10.0` in GolfStatsApp.Shared, GolfStatsApp.Client and GolfStatsApp.Server. Bump packages flagged by the assessment to 10.0.x: Microsoft.AspNetCore.Components.WebAssembly, .WebAssembly.DevServer, .WebAssembly.Server, Microsoft.EntityFrameworkCore.Tools (and other EF Core packages for consistency), Microsoft.NET.ILLink.Tasks, System.Net.Http.Json (remove if now in-box/redundant).

Review behavioral-change notes: `HttpContent`, `Uri` constructor usage (Client), and `UseExceptionHandler(string)` (Server). Research starting points: Client Program.cs `HttpClient` BaseAddress setup, Server Program.cs middleware pipeline, EF Core 10 breaking changes in the DbContext.

**Done when**: All 3 projects target net10.0, recommended packages updated, solution builds with 0 errors and 0 warnings.

---

### 03-final-validation: Validate the full solution

Run a full solution build of `GolfStatsApp.slnx` and run any tests present. Document any deferred recommendations.

**Done when**: Full solution builds cleanly and any tests pass.
