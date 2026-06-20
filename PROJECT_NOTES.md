# Data Center Tycoon — Project Notes

> Single source of truth for continuing this project across sessions. Read this first before making changes.

---

## What the game is now

A top-down, **turn-based economic tycoon**. You run a compute company on a procedurally
generated map. **Demand lives at the towns**: each town emits compute demand (proportional to
its size) that grows every **day**, and you can only serve a town with **data centers inside its
service catchment** (the ring of tiles within `ServiceRadius` of it). The tension is per-town:

- **Serving a town's demand makes money** — but only data centers in *that town's* catchment count.
  Build out in open land with no town nearby and you serve nobody.
- **Crowding a town makes it unhappy** — NIMBY. The penalty is *convex* in how much compute you've
  packed into a town's catchment, normalised by a size-based "comfortable capacity". Pack a town
  past comfort and its anger accelerates.
- **Failing to meet demand also hurts** — brownouts (unmet demand, summed across towns) bite happiness.

**Terrain has mechanical roles too:**
- **River = cooling.** Tiles near a river (within `CoolingRadius`) run at half operating cost, so you
  want your data centers hugging the water. (Towns are anchored near rivers, so each catchment has
  contested cool frontage.) You **cannot build on river or town tiles**.
- **Forest = buffer.** Forest left standing inside a town's catchment **reduces that town's NIMBY**
  (it screens the eyesore). A town's catchment grass is its comfortable cap; once that runs out you
  can **clear forest** to keep serving — but it costs `FOREST_BUILD_MULT`× and removes the buffer.

Demand grows ~5.5%/day forever, so eventually each town demands more than its surroundings can
*comfortably* host. To keep every town fully served you must over-pack them, and they revolt.
Skilled play serves each town up to ~its comfortable capacity, builds on cheap cooled grass, keeps
forest standing, accepts the growing brownout, and banks the cash goal first. **Difficulty comes
from the map**: a town boxed in by river/forest/other towns has a small grass catchment (low hard
cap), so it's the bottleneck.

**You win** by reaching the cash goal; **you lose** if happiness collapses or you go bankrupt.

This replaces two earlier designs: the original "drag red rectangles, watch a happiness number" toy,
and the interim **global-footprint** model (happiness = convex penalty on *total* tiles covered,
weighted by land type). If you read either of those in an old note or commit, they are obsolete —
happiness is now driven by *per-town crowding*, not total coverage.

---

## Quick Start

Primary dev environment is **Windows 11** (Arial font present, MonoGame templates installed):

```powershell
cd <repo>
dotnet run
```

Rebuild after edits (kill the running game first — the `.exe` locks):

```powershell
Get-Process -Name "DataCenterTycoon" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
```

### Building on Linux / CI (gotcha)

The content pipeline builds `Content/Font.spritefont`, which references **Arial**. Arial is a
Windows font and is **not** present on a stock Linux box, so the content build fails there with
`Could not find "Arial" font file` (installing `fonts-liberation` does **not** fix it — MonoGame's
`FontDescriptionProcessor` resolves the font by file/family, not via fontconfig aliases).

To **compile-check the C# only** on Linux (skips the content build):

```bash
dotnet build -p:EnableMGCBItems=false
```

If you ever need a full Linux build, swap the spritefont's `<FontName>` to a font installed on the
machine (e.g. `Liberation Sans`) — but do **not** commit that change, since it would alter the
look on the user's Windows build.

---

## Controls

| Input | Action |
|---|---|
| Left-drag | Build a block of data centers (tile-snapped) — build inside a town's tinted catchment to serve it; blue tiles are cooled (cheap upkeep). Preview shows cost; red = invalid (overlaps / on river or town / unaffordable). |
| Left-click | Build a single tile. |
| Right-click | Remove the data center under the cursor (50% refund). |

You cannot build on **river** or **town** tiles. Building on **forest** is allowed but costs
`FOREST_BUILD_MULT`× and removes that town's buffer.
| Space | Advance to the next day (earn profit, update happiness, demand grows). |
| R | Retry the **same** map (reset money/day/demand/happiness, clear data centers). |
| N | New map (new seed) and reset. |
| Esc | Quit. |

You cannot build on top of an existing data center (placement is rejected if it overlaps).

---

## Win / Lose

Checked at the end of each **Advance Day**:

- **Win** — cash `>= WIN_CASH_GOAL` ($6,000).
- **Lose (revolt)** — happiness `< HAPPINESS_LOSE` (35).
- **Lose (bankrupt)** — money `< 0`.

End screen shows the outcome and `R` (retry map) / `N` (new map). Happiness `< HAPPINESS_WARN` (50)
shows a warning banner while playing.

---

## The economic model (the heart of the game)

All constants live at the top of `Game1.cs`. The model is mirrored in `tools/balance_sim.py`
so balance can be re-tuned without launching the game — **keep the two in sync.**

**Per-town quantities.** Town `t` has a `Size` (its tile count). The map assigns every cell to a
nearest town within `ServiceRadius` (`MapGenerator.CatchmentTown[x,y]`, `-1` = no service).
- `demand_t  = (Size_t / Σ Size) * totalDemand`            — bigger town, bigger share
- `supply_t  = #(your data-center tiles in town t's catchment)`
- `served_t  = min(demand_t, supply_t)`
- `comfort_t = COMFORT_PER_SIZE * Size_t`                  — low-anger capacity (3.85 * size)

Each day, when you press Space (`AdvanceDay`):

```
served   = Σ served_t
revenue  = REVENUE_PER_SERVED * served                              # 2.7 per served unit
opCost   = OP_COST_PER_TILE * (tileCount - cooledTiles*(1-COOLED_OP_FACTOR))   # cooled tiles at 0.5x
money   += revenue - opCost

# happiness eases toward a target (fast down, slow up)
relief_t = BUFFER_RELIEF * min(1, preservedForest_t / (BUFFER_PER_SIZE*Size_t)) # forest screen, <=0.20
nimby    = Σ  NIMBY_WEIGHT * (supply_t / comfort_t)^NIMBY_EXP * (1 - relief_t)   # 14 * (packing)^2.2
brownout = BROWNOUT_WEIGHT * (1 - served / Σ demand_t)             # 50 * overall shortfall
target   = 100 - nimby - brownout
rate     = (target < happiness) ? HAPPINESS_FALL : HAPPINESS_RISE  # 0.55 down / 0.16 up
happiness += rate * (target - happiness)

totalDemand *= 1 + DAILY_DEMAND_GROWTH          # +5.5%/day
```

`cooledTiles` = your tiles on cooled cells (`MapGenerator.Cooled`, within `CoolingRadius` of a river).
`preservedForest_t` = forest tiles in town t's catchment you have **not** built over (clearing forest
to serve removes the buffer and raises that town's anger).

**Why this creates per-town pressure (and kills "serve everyone"):**
- NIMBY is **convex (^2.2)** in how much you've packed a town's catchment relative to its comfortable
  capacity. Up to ~comfort it's cheap; past it, anger accelerates hard.
- `demand_t` grows ~5.5%/day, so each town's demand eventually exceeds its comfortable capacity. To
  fully serve it you must pack past comfort → the town reddens and happiness falls.
- A town's **hard serving cap = its catchment capacity** (buildable cells within `ServiceRadius`),
  which is set by the map. A town boxed in by river/forest/other towns can't be fully served at all
  → permanent brownout. This is the map-driven difficulty.
- So "meet every town's full demand" **loses**. Serve each town up to ~comfort, accept the brownout,
  bank the cash goal before happiness erodes.

Happiness is **stateful** (a field that eases toward the target each day), so a bad day lingers and
recovery is slow (fall 0.55, rise 0.16).

Tiles placed **outside any catchment** serve nobody and generate no NIMBY, but still cost upkeep —
pure waste. The faint per-town tint on the map shows each town's catchment.

### Validated balance (see `tools/balance_sim.py`, and verified on real maps)

| Strategy | Outcome |
|---|---|
| Meet every town's full demand (naive) | **REVOLT** ~day 18–20 (packs towns past comfort) |
| Serve each town to ~0.9–1.0× comfort (skilled) | **WIN** ~day 26–28 |
| Over-pack to 1.1× comfort | mostly **REVOLT** |

The C# economy (cooling + forest buffer included) was run against **actual generated maps** (real
catchments): naive loses on 16/16 seeds, skilled comfort-0.9 wins on 16/16, comfort-1.0 on 15/16,
over-pack comfort-1.1 on only 2/16.

---

## Tunable constants (single-line changes in `Game1.cs`)

| Constant | Value | Effect |
|---|---|---|
| `START_MONEY` | 650 | Early-game tightness |
| `WIN_CASH_GOAL` | 6000 | How long/hard the run is (raise = longer) |
| `COST_PER_TILE` | 12 | Build cost (one-time) |
| `REFUND_FRACTION` | 0.5 | Refund on removal |
| `OP_COST_PER_TILE` | 1.0 | Daily upkeep per tile |
| `COOLED_OP_FACTOR` | 0.5 | Upkeep multiplier for cooled (near-river) tiles |
| `REVENUE_PER_SERVED` | 2.7 | Income per served compute unit |
| `FOREST_BUILD_MULT` | 2.5 | Build-cost multiplier to clear forest |
| `BASE_DEMAND` | 100 | Day-1 total demand (split across towns by size) |
| `DAILY_DEMAND_GROWTH` | 0.055 | The doom clock — higher = shorter game |
| `NIMBY_WEIGHT` | 14 | How much crowding a town hurts |
| `NIMBY_EXP` | 2.2 | Convexity of the crowding penalty (>1 accelerates) |
| `COMFORT_PER_SIZE` | 3.85 | Comfortable compute capacity per town tile |
| `BROWNOUT_WEIGHT` | 50 | How much overall under-serving hurts |
| `BUFFER_RELIEF` | 0.20 | Max NIMBY reduction from preserved forest |
| `BUFFER_PER_SIZE` | 1.0 | Forest tiles per town tile needed for full relief |
| `HAPPINESS_FALL` / `HAPPINESS_RISE` | 0.55 / 0.16 | Resentment fast, trust slow |
| `HAPPINESS_LOSE` / `HAPPINESS_WARN` | 35 / 50 | Lose threshold / warning |
| `MapGenerator.ServiceRadius` | 6 | Catchment size → each town's hard serving cap |
| `MapGenerator.CoolingRadius` | 2 | How far river cooling reaches |

To re-tune: edit the constants in **both** `Game1.cs` and `tools/balance_sim.py`, run
`python3 tools/balance_sim.py`, and confirm naive play still loses and skilled play still wins.
`tools/balance_sim.py` abstracts geometry via representative catchment capacities (the measured
`126/100/81` for `ServiceRadius=6`); to check against real maps, run the economy over actual
`MapGenerator` instances (a throwaway harness referencing `MapGenerator.cs` + `Noise.cs`).

---

## Architecture

```
Program.cs        entry point -> new Game1().Run()

Game1 : Game      everything: input, economy, happiness, rendering, save/load
  state:          _money, _computeDemand (total), _townSupply[] (per town),
                  _townSizeDenom, _tileCount, _happiness, _day, _state (Playing/Won/Lost)
  _dataCenters:   List<DataCenter> (placed blocks; never overlap)
  Update          input -> placement / removal / Space-advance / R / N / Esc
  AdvanceDay      profit, StepHappiness, demand growth, win/lose check, save
  RecalculateCoverage   recomputes tileCount + per-town supply (via CatchmentTown)
  DemandServed / NimbyTotal / HappinessTargetNow   the per-town economy + happiness
  ProjectedHappiness / StepHappiness               ease happiness toward target
  Draw            map -> catchment tint -> towns(anger) -> data centers ->
                  active drag -> per-town gauges -> HUD -> end screen
  SaveGame / LoadGame    full-state persistence (see below)

DataCenter        Bounds (tile-snapped Rectangle) + Cost. (The old unused `Value` field is gone.)

Town              Id, Tiles, Size, Center, CatchmentCapacity. (in MapGenerator.cs)

MapGenerator      Perlin map: river, 2 forests, 3 towns. 48x27 grid, 40px tiles.
  Towns            List<Town> (tracked components; Size, Center, CatchmentCapacity, ForestInCatchment)
  CatchmentTown    int[GW,GH]  nearest town within ServiceRadius, else -1 (multi-source BFS)
  Cooled           bool[GW,GH] empty/forest cells within CoolingRadius of a river
  PlaceTownsConsiderate   retries town layout (~140x), keeps the first/best layout where every
                          town clears MinCapacity = 3*size (wider spacing, prefers open ground)
Noise             hand-rolled 2D Perlin noise.
```

**Considerate generation.** River and forests are generated as before. Towns are the part that
matters for the economy, so `PlaceTownsConsiderate` tries up to 140 layouts and **breaks as soon as
every town clears `MinCapacity = 3 * size`** (empty cells within `ServiceRadius`, assigned to nearest
town), else keeps the best-scoring. It uses wider town spacing (≥4) and prefers anchors with open
ground (`CountEmptyNear`). **~97% of seeds** are fully fair (up from ~85%); the rest have one tighter
town and are rerollable with `N`/`R`. Measured average capacities at `ServiceRadius=6`: ~`123 / 109 /
109` for the `22 / 16 / 10` towns; the big town runs proportionally short (it's the bottleneck), which
the balance accounts for. `ComputeCooling` then marks near-river cells, and each town records its
`ForestInCatchment` (for the buffer).

---

## Persistence

- **File**: `save.dat` in the working directory.
- **Saved**: on every placement/removal, every Advance Day, and on new/reset.
- **Format** (`v2`, line-based, invariant culture):
  ```
  v2
  seed=<long>
  day=<int>
  money=<float>
  demand=<float>
  happiness=<float>
  state=Playing|Won|Lost
  dc=<x>,<y>,<w>,<h>,<cost>     (one per data center)
  ```
- **Legacy**: a file that is just a number is treated as a seed-only save (old format) → starts a
  fresh game on that map.
- On startup the full game (map, money, day, demand, happiness, placed data centers, win/lose
  state) is restored. Parsing is defensive: any failure falls back to a new game.

---

## Conventions

- **No comments in code** (user preference — even on tricky bits).
- Namespace `DataCenterTycoon` for everything; PascalCase public, `_camelCase` private fields.
- Colors are `static readonly Color` at the top of `Game1.cs`.
- Tile coords `(x, y)`, `(0,0)` top-left.
- Don't add features the user didn't ask for. When they say "consider X", they want discussion,
  not silent implementation.

---

## Known issues / future work

- **HUD overlaps the top-left play area.** The translucent HUD panel sits over the map; you can
  still build under it, but it's visually obscured. A dedicated sidebar or map offset would be
  cleaner.
- **No audio, no animation/tweening.** Purely functional rendering.
- **~3% of seeds have one tighter town.** Considerate generation guarantees a fair catchment on ~97%
  of seeds; the rest are harder (one boxed-in town) and rerollable with `N`/`R`. A hard guarantee
  (shrink/relocate a boxed town, or scale its demand to capacity) is possible if needed.
- **`DataCenter` is still just area.** A future "multiple variables + formula" (power, throughput,
  tiers) would add fields here feeding supply/cost/NIMBY.
- **Terrain mechanics are simple.** River = flat cooling discount near water; forest = NIMBY buffer +
  clear-at-cost. No power grid, elevation, or coastline (those were considered and deferred).
- **No difficulty settings / no endless mode past the win.** The run ends at the cash goal.

---

## Decision log (terrain-roles iteration — latest)

| Decision | Why |
|---|---|
| River = cooling: near-river tiles run at half upkeep; river/town tiles unbuildable | User asked to give river/forest real roles. Cooling is classic data-center logic; towns are river-anchored so each catchment has contested cool frontage. |
| Forest = buffer: preserved catchment forest cuts NIMBY (≤20%); clearing it costs 2.5× and removes the buffer | Makes forest a strategic reserve (keep it calm, or clear it for late-game capacity), tying a second terrain type to the economy. |
| Bumped `NIMBY_WEIGHT` 11→14 | Offsets the ~20% forest relief so the baseline tension is unchanged; cooling shortened the arc to ~26–28 days. |
| Tightened generation: break on all-towns-fair, 140 tries, spacing ≥4, prefer open ground | User asked to tighten the guarantee. Fair-map rate ~85% → ~97%. |
| Re-verified the full economy (cooling + buffer + clearing) on real maps | Terrain changes shift balance; checked naive loses 16/16, skilled wins 16/16. |

## Decision log (town-centric iteration)

| Decision | Why |
|---|---|
| Demand lives at towns; serve only within a town's catchment | User chose "Town = demand & NIMBY" to make the map meaningful. Local serving is what makes *where* you build matter (vs. the global-footprint model). |
| Local, convex NIMBY normalised by size-based comfort (`11 * (supply/(3.85*size))^2.2`) | Crowding a town angers *that* town, accelerating past comfort. Size-based comfort keeps balance stable across seeds; geometry enters via the hard cap. |
| Hard serving cap = catchment capacity (geometry) | This is the map-driven difficulty: a boxed-in town can't be fully served. |
| Considerate town generation (best of ~70 layouts, scored by catchment fairness) | User chose "considerate / fair" — guarantees most seeds give every town a workable catchment instead of rolling the dice. |
| Replaced global footprint model with per-town | The footprint model (convex penalty on total tiles by type) made coverage matter but not *location*; towns now tie generation ↔ mechanics. |
| Retuned: growth 5.5%/day, revenue 2.7, goal $6,000 | New model's per-town caps are smaller than the whole map, so the arc was retuned (sim + verified on real maps) to keep naive-loses / skilled-wins, ~30-day arc. |
| Kept 3 towns (22/16/10) | Matches the Aquidneck realism basis and the validated balance. Count is a one-line change (`MapGenerator.TownSizes`) + a re-tune. |

### Earlier iteration (global-footprint — superseded by the above for happiness)

| Decision | Why |
|---|---|
| Convex, uncapped footprint penalty (`130 * x^1.8`) | Old `min(18, log)` cap made sprawl free → "middle of the road" was safe. |
| Stateful happiness (fall 0.55 / rise 0.16) | Bad days linger, recovery slow; implements the recovery the dead `happinessChange` code only gestured at. (Kept.) |
| Win = cash goal; lose = revolt or bankruptcy | Gives the treadmill a goal and two failure modes. (Kept.) |
| Tuned via simulation before coding | Balance is a numbers problem; `tools/balance_sim.py`, verified against a C# port. (Kept.) |
| Full-state save (`v2`) | A tycoon run needs to survive restart. (Kept.) |
| No overlapping placement; 50% refund | Removes a supply double-count exploit; expansion is a commitment. (Kept.) |
| Removed `DataCenter.Value` | Unused vestige. (Kept.) |
