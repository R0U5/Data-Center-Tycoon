# DATA TYCOON — Project Notes

> Single source of truth for continuing this project across sessions. Read this first before making changes.

---

## What the game is now

**DATA TYCOON**: an 8-bit, top-down, turn-based economic tycoon. You run a compute company on a
procedurally generated map. **Demand lives at the towns**: each town emits compute demand
(proportional to its size) that grows every **day**, and you can only serve a town with structures
**inside its service catchment**. The core loop is a tech-and-tolerance race:

- **Serving demand makes money** — but only structures in *that town's* catchment count.
- **Crowding a town makes it furious** — NIMBY. The penalty is *convex* in the town's **anger load**
  (a per-structure weight, not raw tiles), normalised by a **comfort** capacity.
- **Failing to serve demand also hurts** — brownouts (unmet demand across all towns) bite happiness.
- **Demand grows 5.5%/day forever** — the doom clock. You cannot serve it all indefinitely.

**The strategy space (this is the point of the game):**

| Structure | Cost | Upkeep | Serves | Anger load | Notes |
|---|---|---|---|---|---|
| Server Shed | 12 | 1.0 | 1 | 1.0 | best ROI early; anger-inefficient (1.0 compute/load) |
| Data Hall | 55 | 4.0 | 3 | 1.9 | 1.6 compute/load; hot — wants cooling |
| Hyperscale | 170 | 12.0 | 7 | 3.4 | 2.1 compute/load; very hot; big capex |
| Park | 20 | 0.4 | 0 | 0 | +3.0 comfort to its catchment town |

- **Tiers answer the doom clock**: as demand outruns a town's comfort, you replace sheds with
  halls, then hyperscale — more compute per point of anger. Building over a cheaper structure
  upgrades in place (old one credited at 50%).
- **Cooling gates the top tiers**: cooled tiles (within `CoolingRadius` of the river) pay 0.55x
  upkeep. That's -0.45/day on a shed but -5.4/day on hyperscale, so cool riverfront is prime
  real estate you want under your densest buildings.
- **Parks buy tolerance**: each park raises its town's comfort by 3, at the cost of cash, upkeep,
  and a catchment tile that can't serve. Late game is a land-allocation problem: hyperscale vs
  parks on the remaining grass.
- **Forest is a reserve**: standing forest in a catchment cuts that town's NIMBY (≤20%); building
  on forest costs 2.5x and removes the buffer.
- **Win = company VALUE** (cash + 50% of what you paid for standing structures) ≥ $16,000, so
  reinvesting is always viable — no "stop building and idle" endgame.
- **Lose** = happiness < 35 (revolt) or cash < 0 (bankrupt).

This replaces three earlier designs (drag-rectangles toy → global footprint penalty → single
structure type per-town model). If an old note or commit describes those, it is obsolete.

---

## Quick Start

**To just play:** double-click **`Play.bat`** (Windows), or `dotnet run` anywhere.
For a portable no-.NET copy: **`MakeStandalone.bat`** → `Standalone\DataCenterTycoon.exe`.

There is **no content pipeline at all**: the font is generated in code (`PixelFont`), the art is
generated in code (`PixelArt`), and the sounds are generated in code (`Sfx`). `dotnet build` is
all it takes, on any OS or CI.

Headless tools (all safe in CI / containers):

```
dotnet run -- --sim [seeds]        # balance harness over real generated maps
dotnet run -- --sim --trace        # day-by-day trace of the skilled policy, seed 1000
dotnet run -- --sim --selftest     # economy + save/load round-trip tests
dotnet run -- --shot out.png --seed 1000 --days 40   # AI plays 40 days, screenshot, exit
                                   # (under xvfb-run on Linux; --days -1 = help overlay)
```

---

## Controls

| Input | Action |
|---|---|
| 1 / 2 / 3 / 4 | Select Shed / Hall / Hyperscale / Park (also clickable in the sidebar) |
| 5 or X | Bulldozer |
| Left-drag | Build selected structure on all valid tiles in the box (invalid tiles are skipped, cost previewed); bulldozer razes the box |
| Right-click | Raze a single tile (50% refund) |
| Space | End the day (revenue - upkeep, happiness eases, demand grows, win/lose check) |
| H / M | Help overlay / mute |
| R / N | Retry same map / new map |
| Esc | Quit |

You cannot build on river or town tiles. Building over a **cheaper** structure is an in-place
upgrade (old `Paid` credited at `REFUND_FRACTION`); same-type or downgrades are rejected.

---

## The economic model (the heart of the game)

All constants live at the top of `Sim.cs` and `Structures.cs`. The model IS the shipped code —
the balance harness (`BalanceHarness.cs`) runs the same `Sim` on real `MapGenerator` maps,
so there is no second model to keep in sync.

Per town `t` (size = tile count, catchment via `MapGenerator.CatchmentTown`):

```
demand_t  = (Size_t / Σ Size) * totalDemand
compute_t = Σ Compute of structures in t's catchment
load_t    = Σ NimbyLoad of structures in t's catchment
comfort_t = COMFORT_PER_SIZE * Size_t + Σ ComfortAdd (parks in catchment)
relief_t  = BUFFER_RELIEF * min(1, preservedForest_t / Size_t)
```

Each day (`Sim.AdvanceDay`):

```
served    = Σ min(demand_t, compute_t)
revenue   = REVENUE_PER_SERVED * served                       # 2.9
upkeep    = Σ Upkeep * (cooled ? COOLED_OP_FACTOR : 1)        # 0.55 on cooled tiles
money    += revenue - upkeep

nimby     = Σ NIMBY_WEIGHT * (load_t / comfort_t)^NIMBY_EXP * (1 - relief_t)   # 15 * x^2.6
brownout  = BROWNOUT_WEIGHT * (1 - served / Σ demand_t)       # 50 * shortfall
target    = 100 - nimby - brownout
happiness += (target < happiness ? 0.55 : 0.16) * (target - happiness)

totalDemand *= 1.055
win  : NetWorth = money + Σ Paid * 0.5  >=  16000
lose : happiness < 35  |  money < 0
```

Structures outside any catchment serve nobody and anger nobody — pure upkeep. (The "sprawl"
policy in the harness goes bankrupt on day 2 doing this.)

### Validated balance (run `dotnet run -- --sim`, 16 real maps)

| Policy | Outcome |
|---|---|
| Naive sheds, chase demand | REVOLT 16/16, ~day 19 |
| Sheds only, 0.9 comfort discipline | REVOLT 16/16, ~day 35, peak value ~$10.3K |
| Tiered, no parks | REVOLT 16/16, ~day 46, peak ~$12.9K — near miss |
| **Tiered + parks, 0.9 comfort** | **WIN 16/16, ~day 52** |
| Tiered + parks, bold 1.1 | WIN 15/16, ~day 51 — faster but one revolt |
| Tiered + parks, overpack 1.3 | WIN 2/16, REVOLT 14, ~day 26 |
| Sprawl outside catchments | BANKRUPT 16/16, day 2 |

Every rung of that ladder is a real strategy a player can discover: tier up, buy tolerance,
claim the riverfront, ride as close to comfort as you dare.

---

## Tunable constants

`Sim.cs`:

| Constant | Value | Effect |
|---|---|---|
| `START_MONEY` | 700 | Early-game tightness |
| `WIN_CASH_GOAL` | 16000 | Net-worth goal; sits between the sheds-only ceiling (~10K) and the skilled reach |
| `REFUND_FRACTION` | 0.5 | Refund on raze; also upgrade credit and the asset value in NetWorth |
| `COOLED_OP_FACTOR` | 0.55 | Upkeep multiplier on cooled tiles |
| `REVENUE_PER_SERVED` | 2.9 | Income per served compute unit |
| `FOREST_BUILD_MULT` | 2.5 | Build-cost multiplier on forest |
| `BASE_DEMAND` / `DAILY_DEMAND_GROWTH` | 100 / 0.055 | The doom clock |
| `NIMBY_WEIGHT` / `NIMBY_EXP` | 15 / 2.6 | Crowding penalty scale / convexity (2.6 punishes 1.3x packing ~2.6x harder than 0.9x) |
| `COMFORT_PER_SIZE` | 3.85 | Base comfort per town tile |
| `BROWNOUT_WEIGHT` | 50 | How much under-serving hurts |
| `BUFFER_RELIEF` / `BUFFER_PER_SIZE` | 0.20 / 1.0 | Forest calming cap / forest per town tile for full effect |
| `HAPPINESS_FALL` / `RISE` | 0.55 / 0.16 | Resentment fast, trust slow |
| `HAPPINESS_LOSE` / `WARN` | 35 / 50 | Lose threshold / warning banner |

`Structures.cs`: per-structure `Cost / Upkeep / Compute / NimbyLoad / ComfortAdd` (table above).
`MapGenerator.cs`: `ServiceRadius` 6, `CoolingRadius` 2, `TileSize` 36, `TownSizes` {22,16,10}.

**To re-tune**: edit the constants, run `dotnet run -- --sim`, and keep the ladder shape:
naive/sheds-only/no-parks all lose, skilled wins ~16/16, bold ~14+/16, overpack ≤3/16,
sprawl bankrupts. `--trace` prints the skilled policy day by day if something looks off.

---

## Architecture

```
Program.cs        args: --sim|--shot -> harness/screenshot, else new Game1().Run()

Sim.cs            THE GAME RULES. Pure, MonoGame-free (money, demand, happiness, day,
                  per-tile StructureType/Paid grids, per-town aggregates, place/upgrade/
                  demolish validation, AdvanceDay, win/lose, Serialize/Deserialize).
Structures.cs     StructureType enum + StructureDef table (cost/upkeep/compute/load/comfort).
BalanceHarness.cs Scripted policies (parameterised manager) played on real maps: the balance
                  proof, a day-by-day trace, PlayDays (used by --shot), and --selftest.

Game1.cs          Presentation + input only. Layout: map 1728x972 (48x27 @ 36px) top-left,
                  192px sidebar right (title, stats, bars, tool palette, tool info, map key,
                  hints), 108px bottom bar (one chip per town: served, crowding bar vs the
                  1.0-comfort mark, forest relief, anger, parks). Toasts bottom-left of map.
                  Drag preview with per-tile validity + cost. End screen, help overlay (H).
PixelFont.cs      5x7 glyph bitmap font built into a texture at startup. No Font.xnb.
PixelArt.cs       12x12 procedural sprites (grass, animated water, tree, houses, shed, hall,
                  campus with blinking LEDs, park, raze icon) in one atlas. NES-ish palette.
Sfx.cs            Square-wave PCM SoundEffects generated at startup (build/raze/deny/day/
                  select/warn/win/lose). No-ops safely when no audio device exists.

MapGenerator.cs   Perlin map: river, 2 forests, 3 towns; catchments (ServiceRadius BFS),
                  cooling ring, considerate town placement (~97% fair maps). Unchanged logic.
Noise.cs          Hand-rolled 2D Perlin noise.
```

**There is no content pipeline** — no `.mgcb`, no `mgcb` tool, no `Content/` folder, no Arial
dependency. Everything is generated in code at startup. (The old prebuilt `Font.xnb` and its
csproj wiring are gone.)

---

## Persistence

- **File**: `save.dat` next to the executable; written on every build/raze/advance/reset.
- **Format** (`v3`, line-based, invariant culture, produced/parsed by `Sim`):
  ```
  v3
  seed=<long> / day / money / demand / happiness / state=Playing|Won|Lost
  s=<x>,<y>,<structureTypeInt>,<paid>     (one per occupied tile)
  ```
- **Legacy**: a bare-number file (seed-only) or a `v2` file both restore the same *map* and
  start a fresh run on it. Any parse failure falls back to a new game.
- Round-trip integrity is covered by `--sim --selftest`.

---

## Conventions

- **No comments in code** (user preference — even on tricky bits).
- Namespace `DataCenterTycoon`; PascalCase public, `_camelCase` private fields.
- Game rules go in `Sim`/`Structures` (headless-testable), never in `Game1`.
- Colors are `static readonly Color` at the top of `Game1.cs` / in `PixelArt.Palette`.
- Tile coords `(x, y)`, `(0,0)` top-left. `MapGenerator.TileSize` = 36 (12px art @ 3x).
- Don't add features the user didn't ask for. When they say "consider X", they want discussion,
  not silent implementation.
- After balance-affecting changes, run `dotnet run -- --sim` and `--selftest`; for visual
  changes grab `xvfb-run -a dotnet run -- --shot ...` screenshots.

---

## Known issues / future work

- **No music** — only SFX. A generated chiptune loop would fit `Sfx.cs`'s approach.
- **No difficulty settings / endless mode** — the run ends at the value goal.
- **~3% of seeds have one tighter town** (considerate generation guarantees the rest); reroll
  with `N`. A hard guarantee is possible if needed.
- **Bottom-bar chips assume 3 towns** (`TownSizes` has 3); adding towns needs a chip-layout pass.
- **Windowed 1920x1080 only** — no resolution scaling.

---

## Decision log (DATA TYCOON iteration — latest)

| Decision | Why |
|---|---|
| Structure tiers (shed/hall/hyperscale) with per-tile `Compute` + `NimbyLoad` | The old single-type game had one dominant strategy ("fill to comfort, press Space"). Tiers make the doom clock answerable by *density* — a real decision each day. |
| Parks add comfort | A second lever against NIMBY that consumes land + cash; late game becomes hyperscale-vs-parks land allocation. |
| Win = net worth (cash + 50% asset value), $16,000 | Cash-goal made reinvestment irrational at endgame; net worth keeps building optimal to the last day. Goal sits between the sheds-only ceiling (~$10K) and skilled reach. |
| Upgrade-in-place (build over cheaper structure, 50% credit) | Removes demolish-then-rebuild micro; makes the shed→hall→hyperscale arc smooth. |
| NIMBY 15 * x^2.6 (was 14 * x^2.2) | Sharper convexity separates 0.9x (safe) / 1.1x (bold, usually wins) / 1.3x (2/16) packing. |
| Revenue 2.7 → 2.9 | Shortens the skilled arc to ~52 days after the capex-heavy tier economy landed. |
| Balance proven by scripted policies on real maps (`--sim`) | Replaced `tools/balance_sim.py`: no mirrored model to keep in sync; the harness runs the shipped `Sim`. |
| Sim/presentation split (`Sim.cs` vs `Game1.cs`) | Rules become headless-testable (harness, selftest, CI) and the renderer is swappable. |
| Code-generated font/art/sfx; content pipeline deleted | Kills the fragile `Font.xnb`/mgcb/Arial dependency for good and gives a consistent 8-bit look. |
| Layout: map + sidebar + bottom bar | Fixes the old "HUD overlaps the play area" issue; per-town chips surface the numbers that drive strategy (crowding vs comfort, forest relief, parks). |
| `--shot` screenshot mode | Real-frame screenshots under xvfb for visual iteration and docs. |

### Earlier iterations (superseded)

Town-centric single-structure model (per-town convex NIMBY, comfort 3.85/size, forest buffer,
river cooling at flat 0.5x, cash goal $6,000) — the terrain roles and per-town demand/anger
survive; structure variety, parks, net-worth goal, and the tier race replaced its endgame.
Before that: a global-footprint happiness model, and originally a "drag red rectangles" toy.
