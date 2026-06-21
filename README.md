# Data Center Tycoon

A turn-based tycoon game (C# / MonoGame). You place data centers around towns to serve their
growing compute demand, balancing profit against the towns' patience — build near a town to serve it,
but crowd it and it turns against you. Hug the river for cheap cooling; keep forests standing to calm
the neighbors. Reach the cash goal before the towns revolt.

## Run it (Windows)

**Just double-click `Play.bat`.** It builds and launches the game.
(One-time requirement: the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
`Play.bat` will point you there if it's missing.)

Prefer the command line? `dotnet run`

### Want a portable copy with no .NET install?

Double-click **`MakeStandalone.bat`** once. It produces a `Standalone\` folder containing
`DataCenterTycoon.exe` that runs on any Windows PC with nothing else installed — copy the folder
anywhere and double-click the exe.

## Controls

| Input | Action |
|---|---|
| Left-drag / click | Build data centers (inside a town's tinted catchment to serve it; blue tiles = cheap cooling) |
| Right-click | Remove a data center (50% refund) |
| Space | Next day |
| R / N | Retry this map / new map |
| Esc | Quit |

See `PROJECT_NOTES.md` for the full design, economy model, and how to re-tune the balance.
