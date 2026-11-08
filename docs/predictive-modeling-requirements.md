# Predictive Modeling (Monte Carlo) — Requirements & Development Plan

| | |
|---|---|
| **Status** | Draft v1.0 |
| **Branch** | `featurePredictiveModeling` |
| **Stack** | .NET 10, Blazor WebAssembly client, ASP.NET Core server, EF Core + SQLite, xUnit tests |

## 1. Purpose

Let users run Monte Carlo simulations that predict golfer outcomes from **season stats** and **current/historical tournament performances**. Users pick golfers (by ID or name), choose which stats and performance data feed the model, choose the number of simulations (1–10,000), and review probabilistic results.

### 1.1 Goals
- Search/select golfers by PGA Tour player ID or name.
- Select which stats and performance sources drive the model.
- Configure simulation count (max 10,000) and optional seed.
- Present predicted distributions (score, finish, cut, win/top-N probabilities) clearly and exportably.

### 1.2 Non-Goals (v1)
- Betting/odds recommendations or live odds ingestion.
- Course-specific/weather modeling (see §13).
- ML training pipelines; v1 is statistical simulation only.
- Changing existing fantasy scoring or the existing `/fantasy-golf` simulator behavior.

## 2. Current-State Summary

| Area | Existing asset | Relevance |
|---|---|---|
| Simulation | `Server/Services/GolfSimulationService.cs` (fantasy points from birdies/bogeys/cut probability; `FantasyGolfController` `POST api/fantasygolf/simulate` hard-codes 10,000 runs) | Reuse patterns (seedable `Random`, `Task.Run`, percentiles). **Do not change its contract**; build a new service. |
| Player data | `PlayerDataService` (PGA Tour stats 156 + 02414, CSV fallback `Data/merged_golf_stats.csv`, 6h `IMemoryCache`), `GolfPlayerData` | Player IDs/names and baseline stats. |
| PGA Tour client | `Server/PgaTour/*`, `PgaTourController` (`players`, `players/{id}/results`, `players/{id}/stats`, `stats/catalog`, `tournaments/{id}/field\|leaderboard`, `schedule/{year}`), `stat_ids.json` | Stat catalog, historical results, current field/leaderboard. |
| Persisted tournament data | `SyncedTournament`, `TournamentGolfer`, `GolferRound` (`ToPar`, `HolePoints`, `BogeyFree`) via `TournamentSyncService` | Current/historical round-level performance. |
| Export | `ExportService` (CSV/XLSX, formula-injection safe) | Reuse for results export. |
| UI | `FantasyGolf.razor`, `PgaExplorer.razor`, `ApiClient`, `NavMenu.razor` | Patterns for new page and nav link. |
| Tests | `Tests/GolfSimulationServiceTests.cs`, `ApiTests.cs` | Pattern for seeded deterministic tests. |

> Agent note: verify these assumptions by reading the files before coding; adjust names if they have drifted.

## 3. Personas & User Stories

- **Fantasy player** — compare candidate golfers' likely outcomes this week.
- **Stats enthusiast** — choose exactly which stats drive the prediction and see how results change.

| ID | Story | Priority |
|---|---|---|
| US-1 | Search golfers by name (partial, case-insensitive) or exact player ID and add them to a selection list. | Must |
| US-2 | Choose which season stats (e.g., SG: Total, SG: Off-the-Tee, birdie avg, bogey avoidance) the model uses. | Must |
| US-3 | Choose which performances are used: current tournament rounds, last N completed tournaments, or specific historical tournaments/seasons. | Must |
| US-3a | For each selected golfer, see the list of historical events that golfer **participated in** and pick exactly which events (and optionally rounds) feed that golfer's simulation. | Must |
| US-4 | Choose number of simulations from 1 to 10,000 (default 10,000). | Must |
| US-5 | Choose the target tournament (current or upcoming) so field size and cut rules apply. | Must |
| US-6 | View per-golfer results: expected score, finish distribution, P(make cut), P(win), P(top 5/10/20), percentiles. | Must |
| US-7 | See which inputs were used and any data-quality warnings (missing stats, small samples). | Must |
| US-8 | Set an optional random seed for reproducible runs. | Should |
| US-9 | Adjust input weights (e.g., recent form vs. season). | Should |
| US-10 | Export results to CSV/XLSX. | Should |
| US-11 | Save and re-open previous runs (authenticated users). | Could |

## 4. Functional Requirements

### 4.1 Golfer selection
- **FR-1** Typeahead search by **name** (substring, case- and diacritics-insensitive) and by **player ID** (exact match); ID shown in every result row.
- **FR-2** Selected golfers appear in a list (ID, name, rank); each removable; duplicates prevented.
- **FR-3** Minimum 1, maximum **156** golfers. Violations return HTTP 400.
- **FR-4** "Load tournament field" bulk-adds golfers from the chosen tournament field.
- **FR-5** Unknown IDs/names in an API request yield per-golfer validation errors, not a 500.

### 4.2 Stat selection
- **FR-6** List available stats from the existing catalog (`stats/catalog` / `stat_ids.json`), grouped by category (e.g., Strokes Gained) with search.
- **FR-7** User selects 1..N stats (cap N = 15), each with optional **weight** (default equal, normalized to 100%).
- **FR-8** Each stat maps to a model role (§6). Unsupported stats are shown disabled with a tooltip. v1 minimum: SG: Total, SG: Off-the-Tee, SG: Approach, SG: Around-the-Green, SG: Putting, Birdie Average, Bogey Avoidance, Scoring Average.
- **FR-9** Missing stat values for a golfer fall back to the field mean and are flagged in warnings.

### 4.3 Performance selection
- **FR-10** Sources (any combination): (1) **Current tournament** rounds (`GolferRound` / live leaderboard); (2) **Recent form** — last *N* completed tournaments (N 1–20, default 5); (3) **Historical** — chosen tournaments and/or seasons (up to last 5 seasons).
- **FR-10b** **Per-golfer event participation selection.** "Performances" means the historical events a golfer actually played. For every selected golfer the UI shall list that golfer's participated events (from `GolferRound`/`TournamentGolfer`/`PlayerResultSnapshot`, then `players/{id}/results`), showing event name, season, date, event type, finish position (or MC/WD/DQ), total to par, and rounds played, with filters by season, event type, and text search.
  - The user can check/uncheck individual events per golfer, and use bulk actions: select all, clear, last N, by season, by event type, and **"apply selection to all golfers"** (matches events by `tournamentId`; golfers who did not play a given event simply have none to contribute).
  - Optional round-level toggles per event (R1–R4); default is all rounds played. MC/WD/DQ rounds that were played are included; unplayed rounds are never imputed.
  - A golfer's explicit event list **overrides** the generic Recent form/Historical sources for that golfer; golfers with no explicit selection use the generic sources (FR-10). The inputs summary shows, per golfer, the events and round counts used.
  - Selecting an event the golfer did not participate in is rejected (400); golfers with no participated events available get a warning and fall back to stats only.
  - Default selection when the user has not chosen: the golfer's last N events (FR-10 default N = 5), pre-checked and editable.
- **FR-10a** Source resolution order for performances is **database first** (`GolferRound`, `SyncedTournament`, and persisted result snapshots), then the PGA Tour API for any tournament/golfer not found in the DB; API results are written back to the DB. The inputs summary reports how many events came from each tier.
- **FR-11** Each source has an optional weight (default current 40 / recent 40 / historical 20; renormalized when sources are disabled).
- **FR-12** At least one stat **or** one performance source must be selected.
- **FR-13** Recency decay (exponential, default half-life 8 events) applies within recent/historical sources.

### 4.4 Simulation configuration
- **FR-14** `simulations` is an integer in **[1, 10,000]**, default 10,000. Server enforces the bound (400 on violation; never silently clamp).
- **FR-15** Optional `seed` (int). Same inputs + seed ⇒ identical output.
- **FR-16** Target tournament selectable (current/upcoming via `schedule/{year}`); supplies event type (`pga`/`major`/`signature`/`additional`), 4 rounds, field size, and cut rule from the **Cut Rules table (§6.1)**.
- **FR-17** Runs have a timeout (default 60 s) and honor client cancellation via `CancellationToken`.

### 4.5 Results
- **FR-18** Per-golfer: mean/median/P10/P90 strokes-to-par, mean finish, finish buckets (1, 2–5, 6–10, 11–20, 21–40, 41+, MC), P(make cut), P(win), P(top 5), P(top 10), P(top 20).
- **FR-19** Field-level: simulations run, golfers modeled, elapsed ms, seed used.
- **FR-20** Sortable results table; charts: win/top-10 probability bars, finish-position histogram, and score distribution ranges. Use a **free/open-source (MIT/Apache) Blazor-compatible charting library** (see §7.1); simple bars may use CSS.
- **FR-21** **Inputs summary** panel: stats, performance sources, weights, golfers, data as-of timestamps, warnings.
- **FR-22** Export results + inputs summary to CSV/XLSX via `ExportService`.
- **FR-23** (Could) Authenticated users can save, list, re-open, and delete their own runs.

### 4.6 Navigation
- **FR-24** New nav entry "Predictive Modeling" (route `/predictive-modeling`) in `NavMenu.razor`. Saving (FR-23) requires login.

## 5. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-1 | **Performance:** 10,000 sims × 156 golfers × 4 rounds ≤ 10 s; 10,000 × 20 golfers ≤ 2 s on a typical dev machine. Avoid per-iteration allocations/LINQ in hot loops. |
| NFR-2 | **Determinism:** Seeded runs are reproducible. RNG is per-run (no shared static); parallel chunks use RNGs derived from the master seed with deterministic merge. |
| NFR-3 | **Validation/Security:** Server-side validation of counts, ID format, stat IDs (allowlist from catalog), weight ranges 0–100. Exports reuse `ExportService.Escape`. No stack traces in error responses (unlike existing `PlayerDataController`). |
| NFR-4 | **Availability / fallback chain:** `IMemoryCache` → PGA Tour API → **database** (persisted snapshots, §9) → CSV (Birdie/Bogey only, with warning). Every successful PGA API fetch is written through to the DB so the DB is a usable fallback. Results served from DB/CSV carry a "data as of" timestamp and a warning. If every tier fails, return 503 with a clear message. Performance data resolves **DB first, then API** (see FR-10a). |
| NFR-5 | **Abuse control:** Max concurrent runs per server (default 4) and per user/IP; excess ⇒ 429. |
| NFR-6 | **UX/Accessibility:** Mobile-friendly, keyboard-operable selectors, dark-mode compatible, loading/error states. |
| NFR-7 | **Testability:** Engine is pure and RNG-injectable; ≥ 85% line coverage on new service code. |
| NFR-8 | **Observability:** Structured logs for run start/end (golfer count, sims, elapsed); no PII. |
| NFR-9 | **Compatibility:** Existing endpoints/tests keep passing; schema changes additive only and compatible with `DbInitializer.UpgradeSchemaAsync`. |

## 6. Modeling Approach (v1)

For golfer *g*, simulated round *r*:

1. **Skill estimate** `μ_g` (expected strokes vs. field per round) = weighted blend of:
   - *Stat component*: weighted z-scores of selected stats. SG stats map directly to strokes-gained/round; non-SG stats convert via a linear mapping calibrated against SG: Total on the current field (documented in code).
   - *Performance component*: recency-decayed mean strokes-to-par vs. field from selected sources.
   - Shrink toward field mean by sample size: `μ' = (n·μ + k·μ_field)/(n+k)`, default k = 8 rounds.
2. **Variance** `σ_g` = golfer's round-to-round SD (field SD if < 10 rounds), clamped to [1.5, 4.5].
3. **Round score** ~ `Normal(par_offset − μ'_g, σ_g)` plus a shared per-round "conditions" shock ~ `Normal(0, 0.8)` applied to all golfers.
4. **Tournament:** simulate R1–R2, apply cut, simulate R3–R4 for survivors, rank by total with random tiebreak.
5. **Aggregation:** accumulate finish counts, score sums and bounded score samples → FR-18 metrics.
6. **In-progress events:** already-played rounds are fixed to actual results; only remaining rounds are simulated.

Formulas and constants are documented in `docs/predictive-modeling-model.md` (created in Phase 2). Non-SG stat→strokes mappings are modeling assumptions: they must be documented, calibrated against SG: Total, user-weightable, and all output labeled **"estimate"** in the UI and exports.

### 6.1 Cut Rules (configuration-driven)

Implemented as a data table in code/`appsettings` (`PredictiveModeling:CutRules`), **not** hard-coded in the engine, with per-tournament overrides because PGA TOUR formats change year to year. **The list below is the v1 default and must be verified against the current season's official format before release and re-checked each season.**

| Event class | Cut after R2 | Notes |
|---|---|---|
| `pga` (regular full-field events) | Top 65 and ties | Default. |
| `major` — Masters | Top 50 and ties | Override by tournament. |
| `major` — PGA Championship, The Open | Top 70 and ties | Override by tournament. |
| `major` — U.S. Open | Top 60 and ties | Override by tournament. |
| The Players Championship | Top 65 and ties | Override by tournament. |
| `signature` (limited-field, e.g., Sentry, Genesis, RBC Heritage, Truist) | **No cut** (default) | Some signature events (e.g., AT&T Pebble Beach, Arnold Palmer, Memorial) have historically used a reduced cut (top 50 and ties); mark as overrides per tournament. |
| FedEx Cup Playoffs (St. Jude, BMW, Tour Championship) | **No cut** | Tour Championship field = 30. |
| `additional` (opposite-field / alternate events) | Top 65 and ties | Default; override if no-cut. |
| Team formats (e.g., Zurich Classic) | Out of scope for v1 | Reject or warn: engine models individual stroke play only. |

Rules: when a tournament has no cut, `pMakeCut` = 1.0 and the UI hides MC from finish buckets. Ties at the cut line are all retained (field may exceed N); ties for finish rank use random tiebreak per §6 step 4.

## 7. Architecture & Design

```
Client (Blazor)                      Server (ASP.NET Core)
PredictiveModeling.razor  --HTTP-->  PredictiveModelingController
  GolferPicker                         |- GolferSearchService   (IDs/names)
  StatPicker                           |- ModelInputService     (stats + performances -> GolferInputs)
  PerformancePicker + GolferEventPicker |- MonteCarloEngine      (pure, seedable, no I/O)
  SimConfigPanel                       '- export via ExportService
  ResultsView                        Reuses: PlayerDataService, PgaTourApiClient,
ApiClient (new methods)                       GolferRound/TournamentGolfer, IMemoryCache
```

| Layer | Proposed path |
|---|---|
| Shared DTOs | `Shared/Models/PredictiveModeling.cs` |
| Engine | `Server/Services/Prediction/MonteCarloEngine.cs` |
| Inputs | `Server/Services/Prediction/ModelInputService.cs` |
| Search | `Server/Services/Prediction/GolferSearchService.cs` |
| Controller | `Server/Controllers/PredictiveModelingController.cs` |
| UI | `Client/Pages/PredictiveModeling.razor` + components in `Client/Shared/Prediction/` |
| Tests | `Tests/MonteCarloEngineTests.cs`, `Tests/PredictiveModelingApiTests.cs`, `Tests/ModelInputServiceTests.cs` |
| Docs | `docs/predictive-modeling-model.md`; update `README.md` features |

Register services in `Server/Program.cs` (engine singleton/stateless; others consistent with existing services).

### 7.1 Charting library (free, .NET stack)

Approved: a free/open-source library usable from Blazor WebAssembly. Candidates, in order of preference:

| Library | License | Notes |
|---|---|---|
| **Blazor-ApexCharts** (`Blazor-ApexCharts`) | MIT | Rich bar/box-plot/range charts; wraps ApexCharts.js (JS interop). Recommended. |
| **Radzen.Blazor** (`Radzen.Blazor`) | MIT | Native Blazor charts; larger component package. |
| **MudBlazor** (`MudBlazor`) | MIT | Only if the app adopts MudBlazor broadly; too heavy for charts alone. |

Selection criteria (Phase 5 spike, ≤ 0.5 day): MIT/Apache license, supports .NET 10 / WASM, works with dark mode, keeps the PWA service-worker asset manifest valid (JS assets cached/offline-safe), small payload. Verify the package version via NuGet and run a supply-chain/vulnerability check before adding. Avoid unmaintained options (e.g., `ChartJs.Blazor`). Wrap charts in app-owned components (`Client/Shared/Prediction/*Chart.razor`) so the library can be swapped.

## 8. API Specification

Base route: `api/predictive-modeling`.

- **`GET golfers?query={text}&limit=25`** — name substring (min 2 chars) or exact ID. Returns `[{ playerId, playerName, rank }]`; `limit` 1–100.
- **`GET stats`** — `[{ statId, name, category, supported, role }]`.
- **`GET tournaments?scope=current|upcoming|historical&year=YYYY`** — `[{ tournamentId, name, startDate, eventType, status }]`.
- **`GET golfers/{playerId}/events?fromSeason=&toSeason=&eventType=`** — events the golfer participated in (FR-10b): `[{ tournamentId, name, season, startDate, eventType, position, status (finished|MC|WD|DQ), totalToPar, roundsPlayed, rounds: [{ round, toPar }], source (db|api) }]`, newest first. 404 for an unknown golfer; resolution DB first, then API (FR-10a).
- **`POST simulate`**
- **`POST export?format=csv|xlsx`** — body is a `simulate` response; returns a file via `ExportService`.
- **(Could) `GET/POST/DELETE runs`** — `[Authorize]`, scoped to caller.

`POST simulate` request:
```jsonc
{
  "tournamentId": "R2026014",
  "golfers": [{
    "playerId": "46046", "playerName": "Scottie Scheffler",                    // id and/or name; id wins
    "events": [                                                                // optional per-golfer participated events (FR-10b)
      { "tournamentId": "R2025014", "rounds": [1, 2, 3, 4] },                  // "rounds" optional; default = all played
      { "tournamentId": "R2025007" }
    ]
  }],
  "stats": [{ "statId": "02675", "weight": 50 }, { "statId": "156", "weight": 50 }],
  "performance": {
    "currentTournament": { "enabled": true, "weight": 40 },
    "recentForm":        { "enabled": true, "weight": 40, "lastNEvents": 5 },
    "historical":        { "enabled": true, "weight": 20, "tournamentIds": ["R2025014"], "seasons": [2024, 2025] }
  },
  "simulations": 10000,   // 1..10000
  "seed": 12345           // optional
}
```
Response (200):
```jsonc
{
  "simulationsRun": 10000, "seed": 12345, "elapsedMs": 842, "generatedUtc": "...",
  "golfers": [{
    "playerId": "46046", "playerName": "Scottie Scheffler",
    "meanScoreToPar": -9.4, "p10": -14, "p50": -9, "p90": -4, "meanFinish": 6.2,
    "finishBuckets": { "1": 0.18, "2-5": 0.27, "6-10": 0.20, "11-20": 0.15, "21-40": 0.10, "41+": 0.04, "MC": 0.06 },
    "pMakeCut": 0.94, "pWin": 0.18, "pTop5": 0.45, "pTop10": 0.65, "pTop20": 0.80,
    "warnings": []
  }],
  "inputsSummary": { "stats": [], "performance": {}, "dataAsOfUtc": "...", "warnings": [] }
}
```
Errors (`ProblemDetails`): 400 validation, 404 tournament not found, 429 concurrency limit, 503 data unavailable, 504 timeout.

## 9. Data Model Changes

Required (database fallback, NFR-4 / FR-10a). Additive only; verified against `DbInitializer.UpgradeSchemaAsync` (`CREATE TABLE IF NOT EXISTS`):

- `StatSnapshot { Id, StatId, PlayerId, PlayerName, Season, Value, Rank, CapturedUtc }` — unique index `(StatId, PlayerId, Season)`; upserted on every successful PGA API stat fetch.
- `PlayerResultSnapshot { Id, PlayerId, TournamentId, TournamentName, Season, EndDate, EventType, Position, Status, ToPar, Rounds, CapturedUtc }` plus child `PlayerResultRound { Id, SnapshotId, Round, ToPar }` (needed for per-round selection, FR-10b) — unique index `(PlayerId, TournamentId)`; upserted from `players/{id}/results` and from completed synced tournaments. Used for recent/historical performance (DB first, then API).
- Existing `GolferRound` / `SyncedTournament` / `TournamentGolfer` remain the source for the current tournament.
- Add the new `DbSet`s and indexes in `AppDbContext`; write-through logic lives in `ModelInputService` (not controllers).

Optional (FR-23): `SavedPredictionRun { Id, UserId, Name, CreatedUtc, ConfigJson, ResultJson }`, index `(UserId, CreatedUtc)`; additive and verified against `DbInitializer.UpgradeSchemaAsync`.

Also cache per-player results in `IMemoryCache` (`pga:results:{playerId}`, TTL 6 h, matching `PlayerDataService`) in front of the DB/API tiers.

## 10. Development Plan

Each phase is one mergeable PR, keeps `dotnet test` green, and must not change existing endpoint behavior. Agents: read §2 and §5 first. Sizes: S/M/L.

### Phase 0 — Discovery & scaffolding (S)
- Confirm §2 assumptions (read `PgaTourApiClient`, `TournamentSyncService`, `GolferRound` usage, `ApiClient`).
- Add DTOs with validation attributes in `Shared/Models/PredictiveModeling.cs`.
- Add controller/service shells and DI registrations; endpoints return 501.
- **Done when:** solution builds; DTOs compile in Client and Server.

### Phase 1 — Search & catalog endpoints (M)
- `GolferSearchService` over `PlayerDataService` + PGA `players`; normalize names; exact ID match.
- Implement `GET golfers`, `GET stats`, `GET tournaments`, and `GET golfers/{playerId}/events` (participation list, DB first then API with write-back).
- Tests for events endpoint: only participated events returned, MC/WD/DQ statuses, season/type filters, DB-then-API fallback, unknown golfer 404.
- Tests: substring, ID exact, no match, limit bounds, diacritics, CSV fallback.
- **Done when:** endpoints work with PGA API mocked and with fallback.

### Phase 2 — Monte Carlo engine (L)
- Implement `MonteCarloEngine` per §6 (inputs: `GolferInputs[]`, config, seed, `CancellationToken`); chunked parallelism with derived seeds and deterministic merge.
- Write `docs/predictive-modeling-model.md`.
- Tests: seed determinism; probabilities sum to 1; stronger golfer wins more often; sims = 1 and 10,000; cut logic; fixed played rounds; cancellation; non-gating benchmark for NFR-1.
- **Done when:** engine tests pass and NFR-1 targets are met.

### Phase 3 — Model input assembly (L)
- Add `StatSnapshot` and `PlayerResultSnapshot` entities/indexes (§9) and schema-upgrade verification against an existing DB.
- `ModelInputService`: implement the fallback chain (cache → API → DB → CSV) for stats and DB-first-then-API for performances with write-through (NFR-4, FR-10a); fetch current-tournament `GolferRound`s; apply weights, decay, shrinkage, field-mean fallback, warnings (FR-9, FR-13).
- Honor per-golfer `events`/`rounds` (FR-10b): validate participation, override generic sources, apply recency decay by event date, never impute unplayed rounds; add per-golfer summary of events/rounds used.
- Tests: explicit events override generic sources; non-participated event ⇒ 400; round subset; empty selection ⇒ default last N; no events ⇒ stats-only + warning.
- Implement `CutRules` configuration (§6.1) and wire into engine config.
- Tests: API-down ⇒ DB fallback with "data as of" warning; DB-miss ⇒ API then write-back; weight normalization, missing-stat fallback + warning, small-sample shrinkage, source toggles, unknown stat ID rejection.
- **Done when:** fixtures produce expected `GolferInputs` and warnings.

### Phase 4 — Simulate API & hardening (M)
- Wire `POST simulate`; validation (FR-3, FR-12, FR-14); timeout; concurrency limiter (NFR-5); error mapping; logging.
- Tests (`WebApplicationFactory`, per `ApiTests.cs`): 400 for sims 0 / 10,001 / no golfers; 200 at 1 and 10,000; 404 bad tournament; 429 under load; end-to-end seed reproducibility.
- **Done when:** API tests green; no stack traces in error bodies.

### Phase 5 — UI (L)
- `PredictiveModeling.razor` with `GolferPicker`, `StatPicker`, `PerformancePicker` (source toggles and weights), `GolferEventPicker` (per-golfer participated-events table with checkboxes, filters, round toggles, bulk actions and "apply to all golfers"; FR-10b), `SimConfigPanel` (numeric input + slider 1–10,000, seed, tournament dropdown), `ResultsView` (sortable table, probability bars, distribution), inputs summary and warnings.
- Charting spike and library selection per §7.1; build chart wrapper components.
- Extend `ApiClient`; add nav link; loading/cancel/error states; dark mode and mobile layout.
- **Done when:** manual walkthrough of US-1…US-7 passes at desktop and mobile widths; client validation mirrors server bounds.

### Phase 6 — Export & polish (S)
- `POST export` (CSV/XLSX) + UI buttons; update `README.md`; finalize model docs.
- Tests for export content and formula-injection escaping.

### Phase 7 (optional) — Saved runs (M)
- `SavedPredictionRun` entity, endpoints, "My runs" UI, ownership tests.

### Sequencing
`0 → {1, 2 in parallel} → 3 → 4 → 5 → 6 → (7)`. Phases 1 and 2 touch disjoint files. Phase 5 can start against a stubbed API once Phase 0 DTOs exist.

## 11. Acceptance Criteria (release gate)

1. A golfer can be found by partial name **and** by player ID and added to a run.
2. ≥ 3 stats and any combination of the three performance sources can be selected with weights.
2a. For a selected golfer, only events that golfer participated in are listed; selected events/rounds (and only those) are used in that golfer's simulation and shown in the inputs summary.
3. `simulations` accepts 1–10,000; 0 and 10,001 are rejected by UI and API with clear messages.
4. Same request + seed returns identical results.
5. Results show all FR-18 metrics; win probabilities across a full-field run sum to 1.0 ± 0.001.
6. 10,000 sims × 20 golfers ≤ 2 s; × 156 golfers ≤ 10 s.
7. With PGA API unavailable, the feature degrades with a warning or 503 (no unhandled exception).
8. CSV/XLSX export matches on-screen results.
9. All existing tests pass; new code ≥ 85% line coverage; no new build warnings.

## 12. Risks & Open Questions

| # | Item | Decision |
|---|---|---|
| R1 | PGA Tour GraphQL API is unofficial and may change or rate-limit. | **Decided:** cache aggressively; keep behind `PgaTourApiClient`; **database is the fallback** (write-through snapshots, §9), CSV last resort (NFR-4). |
| R2 | Non-SG stat → strokes mapping is a modeling assumption. | **Accepted:** document, calibrate, expose weights, label output "estimate" (§6). |
| Q1 | Anonymous users allowed to run simulations? | **Decided: yes**, with rate limiting (NFR-5). |
| Q2 | "Performances" limited to synced DB events or any PGA API event? | **Decided:** DB first, then API (FR-10a). |
| Q3 | Cut rules for signature/no-cut events? | **Decided:** configuration-driven table in §6.1. List is a v1 default and **must be verified against the official current-season formats before release** (open action for the product owner). |
| Q4 | Is adding a charting library acceptable? | **Decided: yes**, free/open-source .NET-compatible only (§7.1). |

## 13. Future Work
Course-fit and weather features, matchup and fantasy-points projections reusing `GolfSimulationService` scoring, backtesting against historical results, calibration dashboards.

## 14. Definition of Done (per PR)
- `dotnet test Tests/GolfStatsApp.Tests.csproj` passes.
- New tests per phase; no changes to existing endpoint contracts.
- Server-side validation for every new input; no stack traces in responses.
- Docs updated (`README.md`, `docs/predictive-modeling-model.md`).
