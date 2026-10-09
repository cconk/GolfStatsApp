# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0
- **Solution Format**: Convert GolfStatsApp.sln to GolfStatsApp.slnx (user request)
- **Scope**: Modernize entire application (all projects, packages)

## Upgrade Options
- **Upgrade Strategy**: All-at-Once (user confirmed)

## Strategy
**Selected**: All-at-Once
**Rationale**: 3 SDK-style net8.0 projects, shallow dependency graph, no incompatible packages.

### Execution Constraints
- Single atomic upgrade — all projects updated together
- Update TFMs, then packages, restore, then build and fix all errors in one pass
- Validate full solution build (0 errors, 0 warnings) after upgrade; testing afterwards

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: Single Commit at End
- **Branch Sync**: Auto (Merge)
