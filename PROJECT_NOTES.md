# Data Center Tycoon — Project Notes

> Single source of truth for continuing this project across sessions. Read this first before making changes.

---

## What the game is now

A top-down, **turn-based economic tycoon**. You run a compute company on a procedurally
generated map. Each **day**, customer **compute demand** grows. You meet it by placing
**data centers** (red tiles) on the map. The tension is the whole game:

- **Meeting demand makes money** — you earn revenue on the compute you actually serve.
- **Covering land makes people unhappy** — and the unhappiness is *convex and uncapped*:
  the more of the map you pave, the faster happiness falls. Paving towns/rivers/forests is
  far worse than empty grass.
- **Failing to meet demand also makes people unhappy** — brownouts bite immediately.

Demand grows ~7%/day forever, so eventually you **cannot** both fully meet demand and keep
people happy. The skill is deciding, each day, how much to expand: enough to bank the cash
goal, not so much that the community revolts. **You win** by reaching a cash goal; **you lose**
if happiness collapses or you go bankrupt.

This replaces the old "drag red rectangles, watch a happiness number" toy described in earlier
versions of this file. If you are reading a description of "Happiness starts at 100 and drops as
the map is covered in red, Money increases as Happiness decreases" — that is obsolete.

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
| Left-drag | Build a block of data centers (tile-snapped). Preview shows cost; red = invalid (overlaps existing / unaffordable). |
| Left-click | Build a single tile. |
| Right-click | Remove the data center under the cursor (50% refund). |
| Space | Advance to the next day (earn profit, update happiness, demand grows). |
| R | Retry the **same** map (reset money/day/demand/happiness, clear data centers). |
| N | New map (new seed) and reset. |
| Esc | Quit. |

You cannot build on top of an existing data center (placement is rejected if it overlaps).

---

## Win / Lose

Checked at the end of each **Advance Day**:

- **Win** — cash `>= WIN_CASH_GOAL` ($7,000).
- **Lose (revolt)** — happiness `< HAPPINESS_LOSE` (35).
- **Lose (bankrupt)** — money `< 0`.

End screen shows the outcome and `R` (retry map) / `N` (new map). Happiness `< HAPPINESS_WARN` (50)
shows a warning banner while playing.

---

## The economic model (the heart of the game)

All constants live at the top of `Game1.cs`. The model is mirrored in `tools/balance_sim.py`
so balance can be re-tuned without launching the game — **keep the two in sync.**

Each day, when you press Space (`AdvanceDay`):

```
served      = min(supply, demand)              # supply = tiles * COMPUTE_PER_TILE (1/tile)
revenue     = REVENUE_PER_SERVED * served      # 2.4 per served compute unit
opCost      = OP_COST_PER_TILE  * tileCount    # 1.0 per tile per day
money      += revenue - opCost

# happiness eases toward a target (fast down, slow up)
servedFrac  = min(1, supply / demand)
footprint   = FOOTPRINT_WEIGHT * (weightedCovered / GridArea) ^ FOOTPRINT_EXP   # 130 * x^1.8
brownout    = BROWNOUT_WEIGHT  * (1 - servedFrac)                                # 70 * shortfall
target      = 100 - footprint - brownout
rate        = (target < happiness) ? HAPPINESS_FALL : HAPPINESS_RISE             # 0.55 down / 0.16 up
happiness  += rate * (target - happiness)

demand     *= 1 + DAILY_DEMAND_GROWTH          # +7%/day
```

`weightedCovered` is the sum of tile weights under your data centers:

| Tile | Weight | Why |
|---|---|---|
| Empty (grass) | 1.0 | Cheap to anger over |
| Forest | 1.6 | Moderate |
| River | 2.5 | Waterfront / ecological |
| Town | 4.0 | Paving where people live is the worst |

**Why this creates real pressure (and kills the "middle of the road"):**
- The footprint penalty is **convex (^1.8) and uncapped** — every additional tile hurts more than
  the last. (The old model capped the tile penalty at ~20 tiles, so sprawl was effectively free.)
- Empty land runs out (~690 tiles on an average map) right around when demand needs that many tiles
  (~day 28). Past that, fully meeting demand forces you onto town/river/forest (weight 2.5–4×),
  which spikes the footprint and triggers a revolt.
- So "just meet all demand every day" **loses**. You must cap your footprint, accept partial
  service (brownout), and bank the cash goal before happiness erodes.

Happiness is **stateful** (a field that eases toward the target each day), which is why a bad day
lingers and recovery is slow — this is the mechanic the previous version *intended* (there was dead
`happinessChange` code that computed a recovery value and never applied it) but never wired up.

### Validated balance (see `tools/balance_sim.py`)

| Strategy | Outcome |
|---|---|
| Meet all demand (naive) | **REVOLT** ~day 32 (forced onto bad land) |
| Cap footprint at usable land (skilled) | **WIN** ~day 33 |
| Too lean (serve 50–80%) | **REVOLT** (chronic brownout) |
| Over-build (serve 125%) | **REVOLT / BANKRUPT** (footprint + op cost) |

The C# implementation was checked against this model and reproduces it to the dollar.

---

## Tunable constants (single-line changes in `Game1.cs`)

| Constant | Value | Effect |
|---|---|---|
| `START_MONEY` | 650 | Early-game tightness |
| `WIN_CASH_GOAL` | 7000 | How long/hard the run is (raise = longer) |
| `COST_PER_TILE` | 12 | Build cost (one-time) |
| `REFUND_FRACTION` | 0.5 | Refund on removal |
| `OP_COST_PER_TILE` | 1.0 | Daily upkeep per tile |
| `REVENUE_PER_SERVED` | 2.4 | Income per served compute unit |
| `BASE_DEMAND` | 100 | Day-1 demand |
| `DAILY_DEMAND_GROWTH` | 0.07 | The doom clock — higher = shorter game |
| `FOOTPRINT_WEIGHT` | 130 | How much sprawl hurts |
| `FOOTPRINT_EXP` | 1.8 | Convexity of sprawl penalty (>1 accelerates) |
| `BROWNOUT_WEIGHT` | 70 | How much under-serving hurts |
| `HAPPINESS_FALL` / `HAPPINESS_RISE` | 0.55 / 0.16 | Resentment fast, trust slow |
| `HAPPINESS_LOSE` / `HAPPINESS_WARN` | 35 / 50 | Lose threshold / warning |
| `TileWeight(...)` | 1 / 1.6 / 2.5 / 4 | Empty / forest / river / town |

To re-tune: edit the constants in **both** `Game1.cs` and `tools/balance_sim.py`, run
`python3 tools/balance_sim.py`, and confirm naive play still loses and skilled play still wins.

---

## Architecture

```
Program.cs        entry point -> new Game1().Run()

Game1 : Game      everything: input, economy, happiness, rendering, save/load
  state:          _money, _computeDemand, _computeSupply, _tileCount,
                  _weightedCovered, _happiness, _day, _state (Playing/Won/Lost)
  _dataCenters:   List<DataCenter> (placed blocks; never overlap)
  Update          input -> placement / removal / Space-advance / R / N / Esc
  AdvanceDay      profit, StepHappiness, demand growth, win/lose check, save
  RecalculateCoverage   recomputes tileCount + weightedCovered + supply from placements
  HappinessTarget / ProjectedHappiness / StepHappiness   the happiness model
  Draw            map -> data centers -> active drag -> HUD -> end screen
  SaveGame / LoadGame    full-state persistence (see below)

DataCenter        Bounds (tile-snapped Rectangle) + Cost. (The old unused `Value` field is gone.)

MapGenerator      Perlin-driven map: river, 2 forests, 3 towns. 48x27 grid, 40px tiles.
Noise             hand-rolled 2D Perlin noise.
```

Map generation (river / forests / towns from Perlin noise) is unchanged and documented in
`MapGenerator.cs`. It is independent of the economy work above.

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
- **Map variance affects difficulty.** Empty-land count varies by seed (rivers/forests/towns eat
  land), so some maps are harder. `R` retries the same seed; `N` rerolls.
- **`DataCenter` is still just area.** If the long-discussed "multiple variables + formula"
  (power draw, cooling, throughput, tiers) is wanted, `DataCenter` is the place to add fields and
  feed them into supply/cost/footprint. The model is already centralized enough to extend.
- **No difficulty settings / no endless mode past the win.** The run ends at the cash goal.

---

## Decision log (this iteration)

| Decision | Why |
|---|---|
| Convex, uncapped footprint penalty (`130 * x^1.8`) | Old `min(18, log)` cap made sprawl free → "middle of the road" was safe. This is the core balance fix. |
| Stateful happiness (ease toward target, fall 0.55 / rise 0.16) | Makes bad days linger and recovery slow; implements the recovery the old dead `happinessChange` code only gestured at. |
| Tile weights empty 1 / forest 1.6 / river 2.5 / town 4 | Paving towns/rivers must be a real, escalating choice; matches the user's realism sensibility. |
| Win = $7,000 cash; lose = revolt (H<35) or bankruptcy | Gives the survival treadmill a goal and two distinct failure modes. |
| Tuned via simulation before coding | Balance is a numbers problem; `tools/balance_sim.py` finds constants where naive loses / skilled wins, verified against a C# port. |
| Full-state save (`v2`) | Old save persisted only the seed; a tycoon run needs to survive restart. |
| No overlapping placement; 50% refund | Removes a supply double-count exploit and makes expansion a commitment. |
| Removed `DataCenter.Value` | Unused vestige flagged for tidy-up. |
