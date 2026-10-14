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

| Structure | Cost | Upkeep | Serves | Anger load | Power | Notes |
|---|---|---|---|---|---|---|
| Server Shed | 12 | 1.0 | 1 | 1.0 | 0 | best ROI early; anger-inefficient (1.0 compute/load) |
| Data Hall | 55 | 4.0 | 3 | 1.9 | 0 | 1.6 compute/load; hot — wants cooling |
| Hyperscale | 170 | 12.0 | 7 | 3.4 | 2 | 2.1 compute/load; very hot; big capex |
| Park | 20 | 0.4 | 0 | 0 | 0 | +3.0 comfort to its catchment town |
| Cooling Tower | 90 | 3.0 | 0 | 0 | 1 | cools a 3x3 area to 55% upkeep; +1 comfort |
| Power Plant | 130 | 2.0 | 0 | 2.2 | -22 | supplies 22 power to the grid; +anger |
| E-Waste Recycler | 60 | 1.2 | 0 | 0.6 | 1 | $6/day passive income |

- **Tiers answer the doom clock**: as demand outruns a town's comfort, you replace sheds with
  halls, then hyperscale — more compute per point of anger. Building over a cheaper structure
  upgrades in place (old one credited at 50%).
- **Cooling gates the top tiers**: cooled tiles (within `CoolingRadius` of the river, **or** inside
  a Cooling Tower's 3x3 reach) pay 0.55x upkeep. That's -0.45/day on a shed but -5.4/day on
  hyperscale, so cool riverfront is prime real estate.
- **Power is opt-in late-game pressure**: sheds and halls are power-free; hyperscale and support
  structures draw power. The shortfall penalty only applies **once you've built a plant** — an
  off-grid player plays the original game untouched. Build plants before you over-build campuses.
- **Parks buy tolerance**: each park raises its town's comfort by 3, at the cost of cash, upkeep,
  and a catchment tile that can't serve. Late game is a land-allocation problem.
- **Forest is a reserve**: standing forest in a catchment cuts that town's NIMBY (≤20%); building
  on forest costs 2.5x and removes the buffer.
- **Win = company VALUE** (cash + 50% of what you paid for standing structures) ≥ $16,000, so
  reinvesting is always viable — no "stop building and idle" endgame. The win check fires both
  when you build and when you advance the day.
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

`--sim` now prints two sections: the **vanilla four-structure ladder** (the regression baseline,
kept bit-for-bit identical to the pre-production-pass economy) and the **extended systems**
ladder (power / cooling / recycler).

Vanilla ladder:

| Policy | Outcome |
|---|---|
| Naive sheds, chase demand | REVOLT 16/16, ~day 19 |
| Sheds only, 0.9 comfort discipline | REVOLT 16/16, ~day 35, peak value ~$10.3K |
| Tiered, no parks | REVOLT 16/16, ~day 46, peak ~$12.9K — near miss |
| **Tiered + parks, 0.9 comfort** | **WIN 16/16, ~day 52** |
| Tiered + parks, bold 1.1 | WIN 15/16, ~day 51 — faster but one revolt |
| Tiered + parks, overpack 1.3 | WIN 2/16, REVOLT 14, ~day 26 |
| Sprawl outside catchments | BANKRUPT 16/16, day 2 |

Extended systems:

| Policy | Outcome |
|---|---|
| Skilled + cooling + power | REVOLT 16/16 (harness mis-builds the grid; player-facing feature) |
| Cooling + power, no parks | REVOLT 16/16 |
| Recycler economy | WIN 16/16, ~day 54 |

Every rung of the vanilla ladder is a real strategy a player can discover: tier up, buy tolerance,
claim the riverfront, ride as close to comfort as you dare. The key invariant to preserve: **a
player who never builds a power plant experiences the exact original economy** — the power
shortfall penalty only applies once a plant exists (`Sim.PowerShortfallActive`).

---

## Tunable constants

`Sim.cs`:

| Constant | Value | Effect |
|---|---|---|
| `START_MONEY` | 700 | Early-game tightness |
| `WIN_CASH_GOAL` | 16000 | Net-worth goal; sits between the sheds-only ceiling (~10K) and the skilled reach |
| `REFUND_FRACTION` | 0.5 | Refund on raze; also upgrade credit and the asset value in NetWorth |
| `COOLED_OP_FACTOR` | 0.55 | Upkeep multiplier on cooled tiles (river ring **or** cooling-tower reach) |
| `REVENUE_PER_SERVED` | 2.9 | Income per served compute unit |
| `FOREST_BUILD_MULT` | 2.5 | Build-cost multiplier on forest |
| `BASE_DEMAND` / `DAILY_DEMAND_GROWTH` | 100 / 0.055 | The doom clock |
| `NIMBY_WEIGHT` / `NIMBY_EXP` | 15 / 2.6 | Crowding penalty scale / convexity |
| `COMFORT_PER_SIZE` | 3.85 | Base comfort per town tile |
| `BROWNOUT_WEIGHT` | 50 | How much under-serving hurts |
| `POWER_SHORTFALL_PENALTY` | 40 | Morale hit when power demand exceeds supply (only active once a plant exists) |
| `BUFFER_RELIEF` / `BUFFER_PER_SIZE` | 0.20 / 1.0 | Forest calming cap / forest per town tile for full effect |
| `HAPPINESS_FALL` / `RISE` | 0.55 / 0.16 | Resentment fast, trust slow |
| `HAPPINESS_LOSE` / `WARN` | 35 / 50 | Lose threshold / warning banner |
| `SAVE_VERSION` | 4 | Current save format; older `v1`/`v2`/`v3` files upgrade on load |

`Structures.cs`: per-structure `Cost / Upkeep / Compute / NimbyLoad / ComfortAdd / CoolingReach /
PowerOutput / PowerDraw / PassiveIncome` (table above).
`MapGenerator.cs`: `ServiceRadius` 6, `CoolingRadius` 2, `TileSize` 36, `TownSizes` {22,16,10}.

**To re-tune**: edit the constants, run `dotnet run -- --sim`, and keep the ladder shape:
naive/sheds-only/no-parks all lose, skilled wins ~16/16, bold ~14+/16, overpack ≤3/16,
sprawl bankrupts. `--trace` prints the skilled policy day by day if something looks off.

---

## Architecture

```
Program.cs        args: --sim|--shot -> harness/screenshot, else new Game1().Run()

Sim.cs            THE GAME RULES. Pure, MonoGame-free (money, demand, happiness, day,
                  per-tile StructureType/Paid grids, per-town aggregates, power budget,
                  local cooling grid, place/upgrade/demolish validation, AdvanceDay,
                  win/lose, Serialize/Deserialize).
Structures.cs     StructureType enum + StructureDef table (cost/upkeep/compute/load/comfort/
                  cooling/power/passive-income) and the capability interfaces
                  IServesDemand / ICalms / ICools / IPowers / IConsumesPower / IGeneratesValue.
BalanceHarness.cs Two policy sets: the vanilla four-structure ladder (regression baseline)
                  and extended power/cooling/recycler policies. Plus PlayDays (used by
                  --shot), --trace, and --selftest.

Game1.cs          Presentation + input + scene graph. Renders to a fixed 1920x1080 render
                  target and letterboxes to any window size (resizable). Main menu -> game ->
                  end screen. Sidebar is grouped into FINANCE / MORALE / GRID chunks; bottom
                  bar is one chip per town (dynamic width, not fixed 3). Hover tooltips,
                  colourblind-safe mood glyphs, day-resolution ticker, undo stack (Z),
                  tutorial prompts, stat-card end screen, help overlay (H).
Music.cs          Generated chiptune loop (triangle-ish square wave) built at startup; no-ops
                  safely with no audio device. Toggled with SFX via the M key / menu.
PixelFont.cs      5x7 glyph bitmap font built into a texture at startup. No Font.xnb.
PixelArt.cs       12x12 procedural sprites (grass, animated water, tree, houses, shed, hall,
                  campus, park, cooling tower, power plant, recycler, raze icon) in one atlas.
Sfx.cs            Square-wave PCM SoundEffects generated at startup.

MapGenerator.cs   Perlin map: river, 2 forests, 3 towns; catchments (ServiceRadius BFS),
                  cooling ring, considerate town placement (~97% fair maps).
Noise.cs          Hand-rolled 2D Perlin noise.
```

**There is no content pipeline** — no `.mgcb`, no `mgcb` tool, no `Content/` folder, no Arial
dependency. Everything is generated in code at startup.

---

## Persistence

- **File**: `save.dat` next to the executable; written on every build/raze/advance/reset.
- **Format** (`v4`, line-based, invariant culture, produced/parsed by `Sim`):
  ```
  v4
  seed=<long> / day / money / demand / happiness / state=Playing|Won|Lost
  reason=<end reason text, spaces escaped>
  peak=<best value> / peakday / served=<best demand served> / structs=<peak structures>
  s=<x>,<y>,<structureTypeInt>,<paid>     (one per occupied tile)
  ```
- **Upgrade path**: `v1` (bare seed), `v2`, and `v3` files all load — `v1`/`v2` restore the same
  *map* and start a fresh run; `v3` restores full state and is treated as `v4` with default
  peak stats. Any parse failure falls back to a new game.
- **Settings**: `settings.dat` next to the executable stores the sound toggle.
- Round-trip integrity is covered by `--sim --selftest` (including the `v3`→`v4` upgrade case).

---

## Conventions

- **No comments in code** (user preference — even on tricky bits).
- Namespace `DataCenterTycoon`; PascalCase public, `_camelCase` private fields.
- Game rules go in `Sim`/`Structures` (headless-testable), never in `Game1`.
- Colors are `static readonly Color` at the top of `Game1.cs` / in `PixelArt.Palette`.
- Tile coords `(x, y)`, `(0,0)` top-left. `MapGenerator.TileSize` = 36 (12px art @ 3x).
- The renderer targets a fixed `1920x1080` `RenderTarget2D` and letterboxes; **always convert
  mouse coords through `ToVirtual()`** before hit-testing.
- After balance-affecting changes, run `dotnet run -- --sim` and `--selftest`; for visual
  changes grab `xvfb-run -a dotnet run -- --shot ...` screenshots.
- **Keep the vanilla ladder identical** unless deliberately re-tuning — it is the acceptance
  test that the new systems didn't alter the original economy.

---

## Known issues / future work

- **`--shot` under xvfb** can return a mostly-black PNG when the GL surface isn't readable;
  the game itself renders fine in a real window. Prefer manual screenshots for docs.
- **No difficulty settings / endless mode** — the run ends at the value goal.
- **~3% of seeds have one tighter town** (considerate generation guarantees the rest); reroll
  with `N`. A hard guarantee is possible if needed.
- **Bottom-bar chips** size dynamically per town count but were tuned for 3; adding a 4th town
  needs a quick layout check.
- **Power grid balance** — the harness's power policies still lose; the feature is tuned for
  player use, not the naive scripted manager. Revisit if a power-centric AI policy is wanted.

---

## Decision log (DATA TYCOON iteration — latest)

| Decision | Why |
|---|---|
| Structure tiers (shed/hall/hyperscale) with per-tile `Compute` + `NimbyLoad` | The old single-type game had one dominant strategy ("fill to comfort, press Space"). Tiers make the doom clock answerable by *density* — a real decision each day. |
| Parks add comfort | A second lever against NIMBY that consumes land + cash; late game becomes hyperscale-vs-parks land allocation. |
| Win = net worth (cash + 50% asset value), $16,000 | Cash-goal made reinvestment irrational at endgame; net worth keeps building optimal to the last day. |
| Upgrade-in-place (build over cheaper structure, 50% credit) | Removes demolish-then-rebuild micro; makes the shed→hall→hyperscale arc smooth. |
| NIMBY 15 * x^2.6 (was 14 * x^2.2) | Sharper convexity separates 0.9x (safe) / 1.1x (bold, usually wins) / 1.3x (2/16) packing. |
| Balance proven by scripted policies on real maps (`--sim`) | No mirrored model to keep in sync; the harness runs the shipped `Sim`. |
| Sim/presentation split (`Sim.cs` vs `Game1.cs`) | Rules become headless-testable (harness, selftest, CI) and the renderer is swappable. |
| Code-generated font/art/sfx/music | Kills the fragile content pipeline and gives a consistent 8-bit look. |
| Fixed render target + letterbox scaling | Makes the game run at any resolution / resizable window (was 1920x1080-only). |
| Win checked on build **and** on advance | Previously the win only fired when you pressed Space; crossing the goal by building now ends the run immediately. |
| Capability interfaces on `StructureDef` | Adding structures no longer means editing a god-`switch` in `Sim`; `IServesDemand`/`ICalms`/`ICools`/`IPowers`/`IConsumesPower`/`IGeneratesValue` (pattern adapted from ProjectOST's `cInterfaces.verse`). |
| Versioned save + end-reason persisted | Saves now carry the actual loss/win reason and peak stats; `v3` files upgrade cleanly. |
| Power is **opt-in** (`PowerShortfallActive` requires a built plant) | Guarantees a player who ignores power plays the exact original economy — the vanilla ladder is unchanged. |
| Undo stack (`Z`) | Razing drags at 50% refund was unforgiving; single-tile and single-drag undos are tracked. |
| Hover tooltips + colourblind mood glyphs | Text-only, colour-only encoding was the biggest legibility problem for new and colourblind players. |

### Earlier iterations (superseded)

Town-centric single-structure model (per-town convex NIMBY, comfort 3.85/size, forest buffer,
river cooling at flat 0.5x, cash goal $6,000) — the terrain roles and per-town demand/anger
survive; structure variety, parks, net-worth goal, and the tier race replaced its endgame.
Before that: a global-footprint happiness model, and originally a "drag red rectangles" toy.
