# 02-upgrade-all-projects: Upgrade Shared, Client, and Server to net10.0

Update `TargetFramework` to `net10.0` in GolfStatsApp.Shared, GolfStatsApp.Client and GolfStatsApp.Server. Bump packages flagged by the assessment to 10.0.x: Microsoft.AspNetCore.Components.WebAssembly, .WebAssembly.DevServer, .WebAssembly.Server, Microsoft.EntityFrameworkCore.Tools (and other EF Core packages for consistency), Microsoft.NET.ILLink.Tasks, System.Net.Http.Json (remove if now in-box/redundant).

Review behavioral-change notes: `HttpContent`, `Uri` constructor usage (Client), and `UseExceptionHandler(string)` (Server). Research starting points: Client Program.cs `HttpClient` BaseAddress setup, Server Program.cs middleware pipeline, EF Core 10 breaking changes in the DbContext.

**Done when**: All 3 projects target net10.0, recommended packages updated, solution builds with 0 errors and 0 warnings.
