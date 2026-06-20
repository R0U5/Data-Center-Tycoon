# Data Center Tycoon — Project Notes

> Single source of truth for continuing this project across sessions. Read this first before making changes.

---

## Quick Start

```powershell
cd C:\Users\willi\Desktop\local_code\data-center-tycoon
dotnet run
```

To rebuild after edits: if the previous build is still running, the `.exe` will be locked. Kill it first:

```powershell
Get-Process -Name "DataCenterTycoon" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
```

---

## Project Overview

A top-down 2D resource management game. The player places "data centers" (red rectangles) on a procedurally generated map. The map has terrain features (river, forests, towns) generated from Perlin noise. The game tracks two derived values that respond to the player's placement decisions:

- **Happiness** — starts at 100%, drops as the map is covered in red, drops faster over river/town tiles
- **Money** — increases as Happiness decreases, at a 0.5-to-1 ratio, displayed in USD

Eventually the data centers will have multiple variables and a formula combining them; the `Value` field on `DataCenter` is the placeholder for that.

---

## Tech Stack & Environment

- **Language**: C# (top-level statements in `Program.cs`, classes elsewhere)
- **Framework**: .NET 8 (downgraded from net9 template — the .NET 8 SDK is what's installed)
- **Game framework**: MonoGame 3.8 DesktopGL (`mgdesktopgl` template)
- **SDK location**: `C:\Program Files\dotnet\dotnet.exe` (the `dotnet` command is not in PATH by default — call the exe directly)
- **OS**: Windows 11
- **Working directory**: `C:\Users\willi\Desktop\local_code\data-center-tycoon`

If on a fresh machine, install the .NET 8 SDK and MonoGame templates:
```powershell
winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements --silent
dotnet new install MonoGame.Templates.CSharp
```

---

## File Structure

```
data-center-tycoon/
├── Program.cs                  # entry point — calls game.Run()
├── Game1.cs                    # main Game class: input, drawing, happiness/money
├── DataCenter.cs               # DataCenter class (Bounds + Value)
├── MapGenerator.cs             # Perlin-driven map generation
├── Noise.cs                    # hand-rolled 2D Perlin noise
├── DataCenterTycoon.csproj     # net8.0, DesktopGL, content builder task
├── save.dat                    # seed file (created on first run, in CWD)
├── app.manifest
├── Icon.bmp / Icon.ico
├── Content/
│   ├── Content.mgcb            # content pipeline config
│   └── Font.spritefont         # Arial 20pt
└── bin/, obj/                  # build output
```

---

## Current Features

1. **1920×1080 window** with grass-green background
2. **Procedural map** generated from a seed:
   - 1 **river** — largest connected component of low-elevation tiles (Perlin field, threshold 0.32)
   - 2 **forests** — two largest connected components of high-moisture tiles (Perlin field, threshold 0.62)
   - 3 **towns** — sized 22 / 16 / 10 tiles (Portsmouth / Middletown / Newport ratios) on Aquidneck Island
3. **Seed persistence** via `save.dat` in the working directory
4. **Mouse drag** to create a data center (rubber-band rectangle); release to place
5. **Happiness** calculation: weighted by what terrain the data center covers (river=2x, town=3x, others=1x)
6. **Money** calculation: 0.5× the happiness decrease
7. **HUD** in top-left: "Happiness: XX" and "Money: $X.XX"

---

## Architecture

### Class diagram (text)

```
Program
  └── new Game1() → calls game.Run()

Game1 : Game
  ├── _map: MapGenerator            (the current map)
  ├── _dataCenters: List<DataCenter> (placed data centers)
  ├── _activeRect: Rectangle?        (the rubber-band during drag)
  ├── _pixel: Texture2D              (1×1 white texture, used for all rectangle drawing)
  ├── _font: SpriteFont              (Arial 20pt, built via content pipeline)
  ├── Update()                       (input handling, drag state)
  ├── Draw()                         (orchestrates the render layers)
  ├── CalculateHappiness()           (recomputed every frame in Draw)
  ├── FillRect / OutlineRect         (helpers using _pixel)
  └── LoadOrCreateMap / StartNewGame (seed persistence)

DataCenter
  ├── Bounds: Rectangle              (pixel-space rectangle on the play area)
  └── Value: int                     (currently = Bounds.Width * Bounds.Height, placeholder for future formula)

MapGenerator
  ├── Tiles: TileType[,]             (48×27 grid)
  ├── Seed: long                     (for reproducibility / save file)
  ├── _elevation: float[,]           (Perlin field, [0,1])
  ├── _moisture: float[,]            (Perlin field, [0,1], different seed)
  ├── IdentifyRiver()                (BFS for largest low-elevation component)
  ├── PlaceForests()                 (BFS for 2 largest high-moisture components, with river buffer)
  ├── PlaceTowns()                   (3 towns in size order, near river, with forest buffer)
  ├── GrowCluster()                  (organic random-growth, used by PlaceTowns)
  ├── IsForbidden / IsNearType       (avoid + buffer logic)

Noise
  ├── _perm: int[512]                (permutation table, seeded)
  ├── Sample(x, y)                   (returns ~[-1, 1])
  └── uses 5th-order fade + gradient dots
```

### Render layers (back to front)

1. Background (grass green)
2. Map tiles (river blue, town brown, forest dark green) — drawn in `DrawMap()`
3. Data centers (red fill + outline) — drawn in `DrawDataCenters()`
4. Active drag rectangle (translucent blue + outline) — drawn in `DrawActiveRect()`
5. HUD (Happiness, Money) — drawn in `DrawHappiness()` + `DrawMoney()`

---

## Key Formulas

### Happiness

```
covered = unique tiles inside any data center rectangle
(empty * 1 + river * 2 + town * 3 + forest * 1) → weighted
happiness = 100 - (weighted / 1296) * 100
```

Forest is 1× because the user only specified river (2×) and town (3×) multipliers. If they want forest penalized differently, change the line in `CalculateHappiness()`.

Overlapping data centers do NOT double-count tiles — `covered` is a `bool[,]` that's set once and read once per frame.

### Money

```
money = (100 - happiness) * 0.5
```

Displayed with `ToString("C", en-US)` → `$1,234.56` format. Max possible is $50.00 (covers entire map with 1× tiles, which isn't possible given the penalties).

### Town size math

Aquidneck Island is ~3.7% of Rhode Island. The map is sized so 1 tile ≈ 0.8 sq mi:
- Portsmouth target 22 tiles → 17.6 sq mi (real: 17.1)
- Middletown target 16 tiles → 12.8 sq mi (real: 13.0)
- Newport target 10 tiles → 8.0 sq mi (real: 7.7)

48 total town tiles = exactly 3.7% of the 1296-tile map. Don't change the grid/tile size without rethinking the scales.

---

## Tunable Constants (single-line changes)

| Constant | Value | Location | Effect |
|---|---|---|---|
| `WindowWidth` / `WindowHeight` | 1920 / 1080 | `Game1.cs:8-9` | Window size |
| `MapGenerator.TileSize` | 40 | `MapGenerator.cs:11` | Tile size in pixels |
| `MapGenerator.GridWidth` / `GridHeight` | 48 / 27 | `MapGenerator.cs:12-13` | Grid dimensions |
| River elevation threshold | 0.32 | `MapGenerator.cs:42` | Lower = smaller river, higher = bigger |
| Moisture threshold | 0.62 | `MapGenerator.cs:84` | Lower = bigger forests |
| River buffer for forests | 1 tile | `MapGenerator.cs:85` | Distance forests stay from river |
| Forest buffer for towns | 2 tiles | `MapGenerator.cs:139` | Distance towns stay from forests |
| Elevation noise scale | 0.05 | `MapGenerator.cs:34` | Smaller = larger features |
| Moisture noise scale | 0.05 × 1.3 | `MapGenerator.cs:35` | Slightly finer than elevation |
| Town sizes | 22 / 16 / 10 | `MapGenerator.cs:124` | Don't change without re-scaling map |
| Moisure seed offset | 7919 | `MapGenerator.cs:30` | Arbitrary prime for decorrelation |
| Town RNG seed offset | 31337 | `MapGenerator.cs:122` | Arbitrary; separate from elevation/moisture seeds |

---

## Controls

| Input | Action |
|---|---|
| Hold LMB + drag | Draw a rubber-band rectangle (blue) |
| Release LMB | Place the rectangle as a data center (turns red) |
| N | New game (new seed → new map, clears data centers) |
| R | Reset data centers (keeps current map) |
| Esc | Quit |

---

## Visual Design

All colors are `static readonly Color` at the top of `Game1.cs`:

| Element | RGB | Notes |
|---|---|---|
| Background (grass) | `(90, 170, 90)` | Set in last session |
| River | `(60, 110, 200)` | Blue |
| Town | `(160, 110, 70)` | Brown |
| Forest | `(30, 80, 30)` | Dark green (set in last session) |
| Data center fill | `(220, 70, 70)` | Red |
| Data center outline | `(255, 130, 130)` | Lighter red |
| Active drag fill | `(80, 160, 230) * 0.25` | 25% alpha blue |
| Active drag outline | `(150, 210, 255)` | Light blue |
| Text | `Color.White` | HUD only |

---

## Persistence Model

- **File**: `save.dat` in the current working directory (the project folder when running via `dotnet run`)
- **Format**: plain text, the seed number on one line
- **Behavior on startup**: load seed if file exists, else generate one with `Environment.TickCount64` and write it
- **Behavior on N**: generate a new seed, write it, regenerate the map, clear data centers
- **R key**: clears data centers only, does NOT touch the save file or map

Data centers are NOT persisted — they reset on app restart. If the user wants this saved too, that's a future task.

---

## Conventions

- **No comments in code** (user explicitly asked for this — even on tricky bits)
- Namespace: `DataCenterTycoon` for everything
- Tile coordinates are `(x, y)` with `(0, 0)` at top-left
- All public-facing names use PascalCase, private fields use `_camelCase`
- Avoid list pattern for `GrowCluster`: `TileType[]` of types to never grow onto
- Buffer pattern: `(TileType, int)[]` of (type, Chebyshev distance) to stay away from
- Colors are `static readonly Color` at top of `Game1.cs`, not scattered
- Don't add features the user didn't ask for — when they say "consider X", they want discussion, not silent implementation

---

## Pending / Future Work

The project is "done for now" but explicitly unfinished. Known next steps the user has hinted at:

1. **Multiple variables + formula on DataCenter** — `Value` is currently just area. The user said: "this value will come into play later when we have multiple variables to consider and a formula to deal with them all". Likely candidates: power draw, cooling cost, throughput, build cost.
2. **Game over / win conditions** — what happens when happiness hits 0? Is there a target?
3. **Spending the money** — right now money is a passive counter. Probably should be spendable (on upgrades, larger data centers, etc.)
4. **Time-based dynamics** — happiness and money could change over time, not just on placement
5. **Data center placement constraints** — currently you can place anywhere, including on rivers/forests/towns. The user has flagged this as unrealistic in earlier sessions but never asked to enforce it.
6. **Save data center positions** — survive app restarts
7. **More terrain features** — the user mentioned ore veins, oil fields, etc. as future possibilities
8. **Sound, music, polish** — none yet

The original three techniques suggested but not implemented:
- **Wave Function Collapse** — for structured layouts (cities, roads). Skipped as overkill.
- **Voronoi Diagrams** — for territory/district division. Skipped as wrong tool.
Both could be added later if districts or detailed cityscapes become needed.

---

## Known Issues / Edge Cases

- **Town placement is best-effort** — if no valid spot is found in 200 attempts, the town is silently skipped. With 3 towns on a small map, this is rare but possible.
- **Data center rectangles that don't cover a full tile still count** — any tile whose 40×40 box intersects the data center counts. Edge case for very thin rectangles.
- **No bounds check on data center placement** — you can place a rectangle that extends beyond the window, and its tiles past the edge won't be counted in happiness (clamped to grid in `CalculateHappiness`).
- **Active rectangle has no min size** — you can place a 0×0 rectangle (it's filtered out at release time).
- **No undo for data center placement** — only `R` clears all of them.
- **The .exe locks during a running game** — rebuild requires killing the process first (see Quick Start).

---

## Decision Log

| Decision | Why |
|---|---|
| MonoGame over Pygame/HTML5 | User chose C# + MonoGame; we installed .NET 8 SDK for it |
| Perlin noise over simpler value noise | User specifically suggested "Perlin Noise with Stepping" in their analysis of options |
| Stepping = single binary threshold per field (not multi-tier plateaus) | User wanted "flat plateaus" but for v1 binary threshold is sufficient; could revisit |
| Two forests (not 1) | User asked for 2 in an earlier turn |
| Organic random-growth for town shape (not 4×4 square) | User asked for more randomized shapes |
| 3 towns (not 1) at Aquidneck ratios | User specified Portsmouth/Middletown/Newport percentages and Rhode Island scale |
| `save.dat` in CWD (not `%APPDATA%`) | Simplest for prototype; easy to find and delete |
| Forest = 1× in happiness formula | User only specified river (2×) and town (3×); forest was implicit default |
| Money starts at $0 | No other starting value was specified |
| `$#,##0.00` via `ToString("C", en-US)` | "Real US dollars" was the user's phrasing |
| Grid snap NOT applied to data center placement | User never asked for it; current behavior is freeform drag |

---

## Quick Recap for Next Session

When you come back to this project:

1. Read this file first.
2. The game runs, generates a map from a seed, lets you drag red rectangles, shows Happiness + Money.
3. The user wants to extend `DataCenter` with more variables and a formula.
4. The user has consistently preferred concise responses and small focused changes.
5. The user has a clear "this is realistic / this isn't" sensibility about the map (e.g., forests shouldn't be on rivers, towns shouldn't be in forests).
6. Build with `dotnet build` from the project folder; run with `dotnet run`.
7. If something's wrong, check the .exe lock first.
