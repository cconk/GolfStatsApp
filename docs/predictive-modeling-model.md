# Predictive Modeling — Model Reference (v1)

All outputs are **estimates** from a statistical Monte Carlo simulation. Constants live in
`PredictiveModelingOptions` (`appsettings.json` → `PredictiveModeling`).

## 1. Skill estimate μ (strokes better than field per round)

**Stat component** (`ModelInputService.StatToStrokes`)
- SG stats (SG: Total, OTT, APP, ARG, Putting) are used directly as strokes gained per round.
- Non-SG stats (Birdie Average, Bogey Avoidance, Scoring Average) are converted to a z-score across the
  tour population, sign-flipped when lower is better, then multiplied by the SD of SG: Total
  (calibration: 1 SD of the stat ≈ 1 SD of SG: Total). Falls back to 1.0 stroke/SD with a warning.
- Weighted by the user's stat weights, normalized to 100%.
- Missing values use the field mean (0) and add a per-golfer warning.

**Performance component**
- Each round sample = field average to par for that round − golfer's to par. Field averages come from
  synced `GolferRound` data (≥ 30 rounds); otherwise `DefaultFieldToPar` (0.7).
- Sources: current tournament, recent form (last N events) and historical (tournaments/seasons), with
  weights default 40/40/20, renormalized over enabled sources.
- Recency decay within an event list: weight = 0.5^(index / HalfLifeEvents), default half-life 8 events.
- Explicit per-golfer events (FR-10b) replace recent/historical for that golfer and use their combined weight.
  Unplayed rounds are never imputed.

**Blend & shrinkage**
- μ = s·stat + (1−s)·perf where s = `StatsWeight`/100 (only one component if the other is absent).
- Performance shrinkage toward field mean: μ' = n·μ / (n + k), k = `ShrinkageRounds` (8).

## 2. Variance
σ = SD of the golfer's performance samples if ≥ 10 rounds, else `FieldSigma` (2.8); clamped to [1.5, 4.5].

## 3. Simulation (`MonteCarloEngine`)
- Round score = round(fieldToPar − μ' + σ·Z + shock_r), shock_r ~ N(0, 0.8) shared by all golfers per round.
- Rounds already played in the target tournament are fixed to actual results.
- Cut after R2: top N and ties (`CutRules`); missed-cut golfers rank below all survivors.
- Finish ranking by 72-hole total with a uniform random tiebreak.
- When only part of the field is modeled, the cut N is scaled by modeled/field size (warning issued) and
  finish positions are relative to the modeled group.

## 4. Determinism
Simulations are split into fixed 500-sim chunks; each chunk's RNG seed is derived from the master seed via
SplitMix64 and chunk results are merged in chunk order, so a seed yields identical output on any machine.

## 5. Cut rules (§6.1)
Defaults: pga 65, major 70 (Masters 50, U.S. Open 60), Players 65, signature & playoffs no cut,
additional 65; team events (Zurich) unsupported. Override in `PredictiveModeling:CutRules` /
`CutOverrides`. **Verify against the official PGA TOUR format each season.**

## 6. Data sources
- Stats: memory cache → PGA Tour API (written to `StatSnapshots`) → DB snapshots → CSV (birdie/bogey only).
- Performances: DB (`PlayerResultSnapshots`, synced `GolferRound`) first, then PGA Tour API with write-back.
- Fallback data carries a "data as of" warning; if everything fails the API returns 503.
