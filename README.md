# DATA TYCOON

An 8-bit, turn-based tycoon game (C# / MonoGame). You run a compute company on a procedurally
generated map. Towns emit compute demand that grows every day; only structures inside a town's
catchment serve it. Cheap server sheds get you started, dense data halls and hyperscale campuses
serve more demand per tile of neighborhood anger — but they run hot, so the cool riverfront is
prime real estate. Parks and standing forest keep the neighbors calm. Reach the company value
goal before the towns revolt or the money runs out.

![Data Tycoon](docs/screenshot.png)

## Run it (Windows)

**Just double-click `Play.bat`.** It builds and launches the game.
(One-time requirement: the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
`Play.bat` will point you there if it's missing.)

Prefer the command line? `dotnet run` — works on Windows and Linux, no content pipeline,
no extra setup.

### Want a portable copy with no .NET install?

Double-click **`MakeStandalone.bat`** once. It produces a `Standalone\` folder containing
`DataCenterTycoon.exe` that runs on any Windows PC with nothing else installed.

## How to play

| Input | Action |
|---|---|
| 1 / 2 / 3 / 4 | Select Server Shed / Data Hall / Hyperscale / Park |
| 5 or X | Select bulldozer (50% refund) |
| Left-drag | Build the selected structure (or bulldoze an area) |
| Right-click | Bulldoze a single tile |
| Space | End the day |
| H | How-to-play overlay |
| M | Mute |
| R / N | Retry this region / new region |
| Esc | Quit |

Build over a cheaper structure to upgrade it in place (you're credited half of what the old
one cost). Blue-tinged riverfront tiles cool upkeep to 55%. Forest tiles cost 2.5x to build
on and clearing them removes their calming effect on the nearest town.

### The four structures

| | Cost | Upkeep (cooled) | Serves | Anger load |
|---|---|---|---|---|
| Server Shed | $12 | 1.0 (0.6) | 1 | 1.0 |
| Data Hall | $55 | 4.0 (2.2) | 3 | 1.9 |
| Hyperscale | $170 | 12.0 (6.6) | 7 | 3.4 |
| Park | $20 | 0.4 (0.2) | — | raises the town's comfort by 3 |

Every strategy tops out somewhere: sheds alone can't outgrow the anger they cause, and towers
without parks run out of tolerant neighbors. Demand grows 5.5% a day forever — you can't serve
it all. Serve what you can, keep the towns just short of furious, and bank the goal.

## For tinkerers

- `dotnet run -- --sim [seeds]` — headless balance harness: plays six scripted strategies
  (naive, disciplined sheds, tiered, tiered+parks, overpacked, sprawl) on real generated maps
  and prints win/revolt/bankrupt outcomes.
- `dotnet run -- --sim --selftest` — economy and save/load round-trip tests.
- `dotnet run -- --shot out.png --seed 1000 --days 40` — boots the game, lets a skilled AI
  play N days, saves a screenshot, exits (works headless under xvfb).

See `PROJECT_NOTES.md` for the full design, the economy model, and how to re-tune the balance.
