using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DataCenterTycoon;

public class Game1 : Game
{
    private const int WindowWidth = 1920;
    private const int WindowHeight = 1080;
    private const int Ts = MapGenerator.TileSize;
    private const int MapW = MapGenerator.GridWidth * Ts;
    private const int MapH = MapGenerator.GridHeight * Ts;
    private const int SideX = MapW;
    private const int SideW = WindowWidth - MapW;
    private const int BottomY = MapH;
    private const int BottomH = WindowHeight - MapH;

    private static readonly string SaveFileName = Path.Combine(AppContext.BaseDirectory, "save.dat");
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    private static readonly Color BgColor = new(24, 26, 34);
    private static readonly Color PanelColor = new(16, 18, 24);
    private static readonly Color PanelLine = new(70, 76, 96);
    private static readonly Color TextDim = new(150, 156, 172);
    private static readonly Color TextGold = new(250, 208, 80);
    private static readonly Color TextGreen = new(110, 230, 130);
    private static readonly Color TextRed = new(240, 90, 80);
    private static readonly Color TextOrange = new(250, 160, 70);
    private static readonly Color TextCyan = new(120, 210, 250);
    private static readonly Color ValidTint = new(120, 230, 140);
    private static readonly Color InvalidTint = new(240, 80, 70);
    private static readonly Color CoolTint = new(110, 220, 255);
    private static readonly Color[] TownTints = { new(90, 200, 255), new(210, 150, 255), new(255, 210, 100), new(130, 255, 160) };

    private static readonly string[] TownNames =
    {
        "OAKDALE", "RIVERTON", "MILLBURG", "CEDARFORD", "ELMSTEAD",
        "BRIARLEY", "FOXGLEN", "MAPLETON", "STONEBROOK", "WILLOWBY"
    };

    private enum Tool { Shed, Hall, Campus, Park, Raze }

    private static readonly (Tool tool, StructureType type, Sprite icon, string key)[] ToolDefs =
    {
        (Tool.Shed, StructureType.Shed, Sprite.Shed, "1"),
        (Tool.Hall, StructureType.Hall, Sprite.HallA, "2"),
        (Tool.Campus, StructureType.Campus, Sprite.CampusA, "3"),
        (Tool.Park, StructureType.Park, Sprite.Park, "4"),
        (Tool.Raze, StructureType.None, Sprite.Raze, "5"),
    };

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private Texture2D _pixel;
    private PixelFont _font;
    private PixelArt _art;
    private Sfx _sfx;

    private Sim _sim;
    private Tool _tool = Tool.Shed;
    private Point _dragStart;
    private bool _dragging;
    private MouseState _prevMouse;
    private KeyboardState _prevKeyboard;
    private float _animTime;
    private bool _showHelp;
    private bool _warned;
    private readonly List<(string text, Color color, float expires)> _toasts = new();
    private float _clock;

    private readonly string _shotPath;
    private readonly int _shotDays;
    private readonly long? _shotSeed;
    private int _frameCount;

    public Game1(string shotPath = null, long? shotSeed = null, int shotDays = 0)
    {
        _shotPath = shotPath;
        _shotSeed = shotSeed;
        _shotDays = shotDays;
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = WindowWidth;
        _graphics.PreferredBackBufferHeight = WindowHeight;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "DATA TYCOON";
    }

    protected override void Initialize()
    {
        if (_shotPath != null)
        {
            _sim = new Sim(new MapGenerator(_shotSeed ?? 1000L));
            if (_shotDays < 0) _showHelp = true;
            else BalanceHarness.PlayDays(_sim, _shotDays);
        }
        else if (!LoadGame())
        {
            StartNewGame();
        }
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _font = new PixelFont(GraphicsDevice);
        _art = new PixelArt(GraphicsDevice);
        _sfx = new Sfx();
    }

    private void StartNewGame()
    {
        _sim = new Sim(new MapGenerator(Environment.TickCount64));
        _warned = false;
        _toasts.Clear();
        SaveGame();
    }

    private void RetryMap()
    {
        _sim = new Sim(new MapGenerator(_sim.Map.Seed));
        _warned = false;
        _toasts.Clear();
        SaveGame();
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _animTime += dt;
        _clock += dt;
        _toasts.RemoveAll(t => t.expires < _clock);

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        if (Pressed(keyboard, Keys.Escape)) Exit();
        if (Pressed(keyboard, Keys.N)) { StartNewGame(); Toast("NEW REGION", TextCyan); }
        if (Pressed(keyboard, Keys.R)) { RetryMap(); Toast("REGION RESET", TextCyan); }
        if (Pressed(keyboard, Keys.M)) { _sfx.Muted = !_sfx.Muted; Toast(_sfx.Muted ? "SOUND OFF" : "SOUND ON", TextDim); }
        if (Pressed(keyboard, Keys.H)) _showHelp = !_showHelp;

        if (_sim.State == RunState.Playing && !_showHelp)
        {
            if (Pressed(keyboard, Keys.D1)) SelectTool(Tool.Shed);
            if (Pressed(keyboard, Keys.D2)) SelectTool(Tool.Hall);
            if (Pressed(keyboard, Keys.D3)) SelectTool(Tool.Campus);
            if (Pressed(keyboard, Keys.D4)) SelectTool(Tool.Park);
            if (Pressed(keyboard, Keys.D5) || Pressed(keyboard, Keys.X)) SelectTool(Tool.Raze);
            if (Pressed(keyboard, Keys.Space)) AdvanceDay();
            HandleMouse(mouse);
        }

        _prevMouse = mouse;
        _prevKeyboard = keyboard;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys key)
        => kb.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        _sfx.Play(Sound.Select, 0.35f);
    }

    private void HandleMouse(MouseState mouse)
    {
        bool leftPressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool leftReleased = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

        if (leftPressed && mouse.X >= SideX)
        {
            foreach (var (tool, _, _, _) in ToolDefs)
                if (ToolButtonRect(tool).Contains(mouse.X, mouse.Y))
                {
                    SelectTool(tool);
                    return;
                }
            return;
        }

        if (leftPressed && InMap(mouse))
        {
            _dragStart = TileAt(mouse);
            _dragging = true;
        }

        if (leftReleased && _dragging)
        {
            _dragging = false;
            var end = TileAt(mouse);
            ApplyTool(_dragStart, end);
        }

        if (rightPressed && InMap(mouse))
        {
            var p = TileAt(mouse);
            if (_sim.DemolishAt(p.X, p.Y))
            {
                _sfx.Play(Sound.Demolish);
                SaveGame();
            }
        }
    }

    private void ApplyTool(Point a, Point b)
    {
        if (_tool == Tool.Raze)
        {
            int removed = _sim.DemolishRegion(a.X, a.Y, b.X, b.Y);
            if (removed > 0)
            {
                _sfx.Play(Sound.Demolish);
                Toast($"RAZED {removed} TILES", TextOrange);
                SaveGame();
            }
            return;
        }

        var type = ToolDefs[(int)_tool].type;
        var (tiles, cost) = _sim.RegionPlan(a.X, a.Y, b.X, b.Y, type);
        if (tiles == 0)
        {
            _sfx.Play(Sound.Deny, 0.35f);
            return;
        }
        if (_sim.Money < cost)
        {
            _sfx.Play(Sound.Deny, 0.35f);
            Toast("NOT ENOUGH CASH", TextRed);
            return;
        }
        _sim.PlaceRegion(a.X, a.Y, b.X, b.Y, type);
        _sfx.Play(Sound.Build);
        Toast($"BUILT {tiles} FOR {Money(cost)}", TextGreen);
        SaveGame();
    }

    private void AdvanceDay()
    {
        _sim.AdvanceDay();
        _sfx.Play(Sound.Day, 0.4f);

        if (_sim.State == RunState.Won) _sfx.Play(Sound.Win, 0.7f);
        else if (_sim.State == RunState.Lost) _sfx.Play(Sound.Lose, 0.7f);
        else if (_sim.Happiness < Sim.HAPPINESS_WARN && !_warned)
        {
            _warned = true;
            _sfx.Play(Sound.Warn, 0.6f);
            Toast("THE TOWNS ARE TURNING ON YOU", TextRed);
        }
        else if (_sim.Happiness >= Sim.HAPPINESS_WARN + 8f)
        {
            _warned = false;
        }
        SaveGame();
    }

    private void Toast(string text, Color color)
    {
        _toasts.Add((text, color, _clock + 3.5f));
        if (_toasts.Count > 3) _toasts.RemoveAt(0);
    }

    private static bool InMap(MouseState m) => m.X >= 0 && m.X < MapW && m.Y >= 0 && m.Y < MapH;

    private static Point TileAt(MouseState m)
        => new(Math.Clamp(m.X / Ts, 0, MapGenerator.GridWidth - 1), Math.Clamp(m.Y / Ts, 0, MapGenerator.GridHeight - 1));

    private static string Money(float v) => v.ToString("C0", Usd);

    private static string ShortMoney(float v)
        => Math.Abs(v) >= 10000f ? $"${v / 1000f:F1}K" : v.ToString("C0", Usd);

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(BgColor);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawMap();
        DrawDragPreview();
        DrawTownLabels();
        DrawSidebar();
        DrawBottomBar();
        DrawToasts();
        if (_showHelp) DrawHelp();
        if (_sim.State != RunState.Playing) DrawEndScreen();

        _spriteBatch.End();
        base.Draw(gameTime);

        _frameCount++;
        if (_shotPath != null && _frameCount >= 3)
        {
            SaveScreenshot();
            Exit();
        }
    }

    private int AnimFrame => (int)(_animTime * 2f) % 2;

    private void DrawMap()
    {
        var map = _sim.Map;
        int frame = AnimFrame;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var dest = new Rectangle(x * Ts, y * Ts, Ts, Ts);
                var tile = map.Tiles[x, y];
                var structure = _sim.Grid[x, y];

                Sprite baseSprite;
                if (tile == TileType.River)
                    baseSprite = frame == 0 ? Sprite.WaterA : Sprite.WaterB;
                else if (structure != StructureType.None)
                    baseSprite = StructureSprite(structure, frame, x, y);
                else if (tile == TileType.Forest)
                    baseSprite = Sprite.Tree;
                else if (tile == TileType.Town)
                    baseSprite = (x + y) % 2 == 0 ? Sprite.HouseA : Sprite.HouseB;
                else
                    baseSprite = (x * 7 + y * 13) % 3 == 0 ? Sprite.GrassB : Sprite.GrassA;

                Color tint = Color.White;
                if (tile == TileType.Town)
                {
                    int t = map.CatchmentTown[x, y];
                    float anger = t >= 0 ? Math.Clamp((_sim.TownLoadRatio(t) - 0.6f) / 0.7f, 0f, 1f) : 0f;
                    tint = Color.Lerp(Color.White, new Color(255, 95, 85), anger);
                }
                _spriteBatch.Draw(_art.Atlas, dest, _art.Src(baseSprite), tint);

                if (tile != TileType.River && tile != TileType.Town)
                {
                    int t = map.CatchmentTown[x, y];
                    if (t >= 0)
                        FillRect(dest, TownTints[t % TownTints.Length] * 0.10f);
                    if (map.Cooled[x, y])
                    {
                        FillRect(dest, CoolTint * 0.13f);
                        FillRect(new Rectangle(dest.X, dest.Y, 6, 2), CoolTint * 0.8f);
                        FillRect(new Rectangle(dest.X, dest.Y, 2, 6), CoolTint * 0.8f);
                    }
                }
            }
    }

    private static Sprite StructureSprite(StructureType s, int frame, int x, int y)
    {
        int local = (x * 5 + y * 11) % 2;
        int f = (frame + local) % 2;
        return s switch
        {
            StructureType.Shed => Sprite.Shed,
            StructureType.Hall => f == 0 ? Sprite.HallA : Sprite.HallB,
            StructureType.Campus => f == 0 ? Sprite.CampusA : Sprite.CampusB,
            _ => Sprite.Park
        };
    }

    private void DrawDragPreview()
    {
        if (_sim.State != RunState.Playing || _showHelp) return;
        var mouse = Mouse.GetState();
        if (!_dragging && !InMap(mouse)) return;

        var a = _dragging ? _dragStart : TileAt(mouse);
        var b = TileAt(mouse);

        if (_tool == Tool.Raze)
        {
            for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                {
                    var r = new Rectangle(x * Ts, y * Ts, Ts, Ts);
                    FillRect(r, InvalidTint * 0.30f);
                    OutlineRect(r, InvalidTint * 0.9f);
                }
            return;
        }

        var type = ToolDefs[(int)_tool].type;
        var (tiles, cost) = _sim.RegionPlan(a.X, a.Y, b.X, b.Y, type);
        bool affordable = _sim.Money >= cost;

        for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
            {
                var r = new Rectangle(x * Ts, y * Ts, Ts, Ts);
                bool ok = _sim.CanPlaceAt(x, y, type);
                FillRect(r, (ok ? (affordable ? ValidTint : TextOrange) : InvalidTint) * 0.30f);
                OutlineRect(r, (ok ? (affordable ? ValidTint : TextOrange) : InvalidTint) * 0.9f);
            }

        if (tiles > 0)
        {
            string label = $"{tiles} x {ToolName(_tool)} {Money(cost)}";
            int lx = Math.Clamp(Math.Min(a.X, b.X) * Ts, 4, MapW - _font.Width(label) - 8);
            int ly = Math.Max(4, Math.Min(a.Y, b.Y) * Ts - 20);
            DrawLabel(label, lx, ly, affordable ? Color.White : TextRed);
        }
    }

    private static string ToolName(Tool t) => t switch
    {
        Tool.Shed => "SHED",
        Tool.Hall => "HALL",
        Tool.Campus => "HYPR",
        Tool.Park => "PARK",
        _ => "RAZE"
    };

    private void DrawTownLabels()
    {
        var map = _sim.Map;
        foreach (var t in map.Towns)
        {
            float dem = _sim.DemandForTown(t.Id);
            float served = Math.Min(dem, _sim.TownCompute(t.Id));
            float frac = dem > 0f ? served / dem : 1f;
            float ratio = _sim.TownLoadRatio(t.Id);

            int cx = (int)(t.CenterX * Ts) + Ts / 2;
            int cy = (int)(t.CenterY * Ts) + Ts / 2;

            string name = TownName(t.Id);
            int nw = _font.Width(name);
            int lx = Math.Clamp(cx - nw / 2, 4, MapW - nw - 4);
            int ly = Math.Max(4, cy - 64);
            DrawLabel(name, lx, ly, Color.White);

            var srvBar = new Rectangle(Math.Clamp(cx - 40, 4, MapW - 84), ly + 18, 80, 7);
            Color srvColor = frac >= 0.99f ? TextGreen : (frac >= 0.6f ? TextGold : TextOrange);
            DrawBar(srvBar, frac, srvColor);

            var moodBar = new Rectangle(srvBar.X, srvBar.Y + 9, 80, 7);
            Color moodColor = ratio <= 0.85f ? TextCyan : (ratio <= 1.05f ? TextGold : TextRed);
            DrawBar(moodBar, Math.Min(1f, ratio), moodColor);
        }
    }

    private string TownName(int id)
        => TownNames[(int)((Math.Abs(_sim.Map.Seed) + id * 3 + id) % TownNames.Length)];

    private Rectangle ToolButtonRect(Tool tool)
        => new(SideX + 8, 386 + (int)tool * 48, SideW - 16, 44);

    private void DrawSidebar()
    {
        FillRect(new Rectangle(SideX, 0, SideW, WindowHeight), PanelColor);
        FillRect(new Rectangle(SideX, 0, 2, WindowHeight), PanelLine);

        int x = SideX + 10;
        int y = 12;

        _font.Draw(_spriteBatch, "DATA", x, y, TextGold, 3);
        _font.Draw(_spriteBatch, "TYCOON", x + 44, y + 24, TextCyan, 3);
        y += 52;
        FillRect(new Rectangle(x, y, SideW - 20, 1), PanelLine);
        y += 10;

        _font.Draw(_spriteBatch, $"DAY {_sim.Day}", x, y, Color.White);
        y += 22;

        _font.Draw(_spriteBatch, $"CASH {ShortMoney(_sim.Money)}", x, y, Color.White);
        y += 20;

        _font.Draw(_spriteBatch, $"VALUE {ShortMoney(_sim.NetWorth)}", x, y, TextGold);
        y += 16;
        DrawBar(new Rectangle(x, y, SideW - 20, 9), _sim.NetWorth / Sim.WIN_CASH_GOAL, TextGold);
        y += 12;
        _font.Draw(_spriteBatch, $"GOAL {ShortMoney(Sim.WIN_CASH_GOAL)}", x, y, TextDim);
        y += 26;

        float proj = _sim.ProjectedHappiness();
        Color hColor = HappinessColor(_sim.Happiness);
        _font.Draw(_spriteBatch, $"HAPPY {_sim.Happiness:F0}", x, y, hColor);
        if (Math.Abs(proj - _sim.Happiness) >= 1f)
            _font.Draw(_spriteBatch, $"> {proj:F0}", x + 110, y, HappinessColor(proj));
        y += 16;
        var hbar = new Rectangle(x, y, SideW - 20, 9);
        DrawBar(hbar, _sim.Happiness / 100f, hColor);
        int markX = hbar.X + (int)(hbar.Width * (Sim.HAPPINESS_LOSE / 100f));
        FillRect(new Rectangle(markX, hbar.Y - 2, 2, hbar.Height + 4), TextRed);
        y += 20;

        var (demand, served) = _sim.DemandServed();
        float pct = demand > 0f ? served / demand * 100f : 100f;
        Color servedColor = pct >= 99.5f ? TextGreen : (pct >= 70f ? Color.White : TextOrange);
        _font.Draw(_spriteBatch, $"SRV {served:F0}/{demand:F0}", x, y, servedColor);
        y += 18;
        _font.Draw(_spriteBatch, $"BLACKOUT {100f - pct:F0}%", x, y, pct >= 99.5f ? TextDim : TextOrange);
        y += 18;
        if (_sim.Day > 1)
        {
            Color netColor = _sim.LastNet >= 0 ? TextGreen : TextRed;
            _font.Draw(_spriteBatch, $"NET {ShortMoney(_sim.LastNet)}/DAY", x, y, netColor);
        }
        else
        {
            _font.Draw(_spriteBatch, "SERVE TOWNS", x, y, TextDim);
        }
        y += 18;
        _font.Draw(_spriteBatch, $"COOLED {_sim.CooledStructureTiles}/{_sim.StructureTiles}", x, y, TextCyan);
        y += 18;
        _font.Draw(_spriteBatch, $"DEMAND +{Sim.DAILY_DEMAND_GROWTH * 100f:F1}%/D", x, y, TextDim);

        _font.Draw(_spriteBatch, "BUILD", x, 368, TextDim);
        foreach (var (tool, type, icon, key) in ToolDefs)
        {
            var r = ToolButtonRect(tool);
            bool selected = _tool == tool;
            FillRect(r, selected ? new Color(44, 48, 66) : new Color(28, 30, 40));
            OutlineRect(r, selected ? TextGold : PanelLine);
            _spriteBatch.Draw(_art.Atlas, new Rectangle(r.X + 4, r.Y + 4, 36, 36), _art.Src(icon), Color.White);
            string name = type != StructureType.None ? Structures.Get(type).Short : "RAZE";
            _font.Draw(_spriteBatch, $"{key} {name}", r.X + 46, r.Y + 6, selected ? Color.White : TextDim);
            string sub = type != StructureType.None ? ShortMoney(Structures.Get(type).Cost) : "50% BACK";
            _font.Draw(_spriteBatch, sub, r.X + 46, r.Y + 24, selected ? TextGold : TextDim);
        }

        int iy = 386 + ToolDefs.Length * 48 + 10;
        FillRect(new Rectangle(x, iy - 6, SideW - 20, 1), PanelLine);
        if (_tool != Tool.Raze)
        {
            var def = Structures.Get(ToolDefs[(int)_tool].type);
            _font.Draw(_spriteBatch, def.Name, x, iy, Color.White); iy += 20;
            _font.Draw(_spriteBatch, $"UPKEEP {def.Upkeep:F1}", x, iy, TextDim); iy += 18;
            _font.Draw(_spriteBatch, $"COOLED {def.Upkeep * Sim.COOLED_OP_FACTOR:F1}", x, iy, TextCyan); iy += 18;
            if (def.Compute > 0)
            {
                _font.Draw(_spriteBatch, $"SERVES {def.Compute:F0}", x, iy, TextGreen); iy += 18;
                _font.Draw(_spriteBatch, $"ANGER {def.NimbyLoad:F1}", x, iy, TextOrange); iy += 18;
            }
            else
            {
                _font.Draw(_spriteBatch, $"COMFORT +{def.ComfortAdd:F0}", x, iy, TextGreen); iy += 18;
                _font.Draw(_spriteBatch, "CALMS A TOWN", x, iy, TextDim); iy += 18;
            }
        }
        else
        {
            _font.Draw(_spriteBatch, "BULLDOZE", x, iy, Color.White); iy += 20;
            _font.Draw(_spriteBatch, "REMOVE TILES", x, iy, TextDim); iy += 18;
            _font.Draw(_spriteBatch, "REFUND 50%", x, iy, TextOrange); iy += 18;
        }

        int ly = 800;
        FillRect(new Rectangle(x, ly - 8, SideW - 20, 1), PanelLine);
        _font.Draw(_spriteBatch, "MAP KEY", x, ly, TextDim); ly += 20;
        FillRect(new Rectangle(x, ly + 1, 12, 12), CoolTint * 0.5f);
        _font.Draw(_spriteBatch, "COOL CHEAP", x + 18, ly, TextCyan); ly += 20;
        FillRect(new Rectangle(x, ly + 1, 12, 12), TownTints[0] * 0.5f);
        _font.Draw(_spriteBatch, "CATCHMENT", x + 18, ly, TextDim); ly += 20;
        FillRect(new Rectangle(x, ly + 1, 12, 12), new Color(28, 92, 44));
        _font.Draw(_spriteBatch, "FOREST CALMS", x + 18, ly, TextGreen);

        int hy = WindowHeight - 148;
        FillRect(new Rectangle(x, hy - 8, SideW - 20, 1), PanelLine);
        _font.Draw(_spriteBatch, "SPACE END DAY", x, hy, TextGold); hy += 18;
        _font.Draw(_spriteBatch, "DRAG BUILD", x, hy, TextDim); hy += 18;
        _font.Draw(_spriteBatch, "RCLICK RAZE 1", x, hy, TextDim); hy += 18;
        _font.Draw(_spriteBatch, "H HELP  M MUTE", x, hy, TextDim); hy += 18;
        _font.Draw(_spriteBatch, "R RETRY  N NEW", x, hy, TextDim); hy += 18;
        _font.Draw(_spriteBatch, "ESC QUIT", x, hy, TextDim);
    }

    private void DrawBottomBar()
    {
        FillRect(new Rectangle(0, BottomY, MapW, BottomH), PanelColor);
        FillRect(new Rectangle(0, BottomY, MapW, 2), PanelLine);

        var map = _sim.Map;
        int chipW = 560;
        for (int i = 0; i < map.Towns.Count && i < 3; i++)
        {
            var t = map.Towns[i];
            int cx = 12 + i * (chipW + 12);
            int cy = BottomY + 10;
            var chip = new Rectangle(cx, cy, chipW, BottomH - 20);
            FillRect(chip, new Color(28, 30, 40));
            OutlineRect(chip, PanelLine);
            FillRect(new Rectangle(cx, cy, 4, chip.Height), TownTints[i % TownTints.Length]);

            float dem = _sim.DemandForTown(t.Id);
            float served = Math.Min(dem, _sim.TownCompute(t.Id));
            float ratio = _sim.TownLoadRatio(t.Id);

            _font.Draw(_spriteBatch, TownName(t.Id), cx + 14, cy + 8, Color.White);
            string srv = $"SRV {served:F0}/{dem:F0}";
            _font.Draw(_spriteBatch, srv, cx + 190, cy + 8, served >= dem - 0.5f ? TextGreen : TextOrange);
            string mood = MoodWord(ratio);
            _font.Draw(_spriteBatch, mood, cx + 400, cy + 8, MoodColor(ratio));

            _font.Draw(_spriteBatch, $"CROWDING {ratio * 100f:F0}%", cx + 14, cy + 34, TextDim);
            var bar = new Rectangle(cx + 190, cy + 38, 160, 8);
            DrawBar(bar, Math.Min(1f, ratio / 1.3f), MoodColor(ratio));
            int comfortMark = bar.X + (int)(bar.Width / 1.3f);
            FillRect(new Rectangle(comfortMark, bar.Y - 2, 2, bar.Height + 4), Color.White);
            float relief = _sim.TownRelief(t.Id);
            if (relief > 0.001f)
                _font.Draw(_spriteBatch, $"FOREST -{relief * 100f:F0}%", cx + 400, cy + 34, TextGreen);

            _font.Draw(_spriteBatch, $"COMFORT {_sim.TownComfort(t.Id):F0}", cx + 14, cy + 60, TextDim);
            _font.Draw(_spriteBatch, $"ANGER {_sim.TownNimby(t.Id):F1}", cx + 190, cy + 60, ratio > 1f ? TextRed : TextDim);
            int parks = (int)(_sim.TownParkComfort(t.Id) / Structures.Park.ComfortAdd + 0.5f);
            if (parks > 0)
                _font.Draw(_spriteBatch, $"PARKS {parks}", cx + 400, cy + 60, TextGreen);
        }
    }

    private void DrawToasts()
    {
        int ty = BottomY - 30;
        if (_sim.Happiness < Sim.HAPPINESS_WARN && _sim.State == RunState.Playing && (int)(_animTime * 2f) % 2 == 0)
        {
            DrawLabel("! REVOLT RISK !", 12, ty, TextRed);
            ty -= 26;
        }
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            DrawLabel(_toasts[i].text, 12, ty, _toasts[i].color);
            ty -= 26;
        }
    }

    private static string MoodWord(float ratio)
        => ratio <= 0.5f ? "CONTENT" : (ratio <= 0.85f ? "UNEASY" : (ratio <= 1.05f ? "TENSE" : "FURIOUS"));

    private Color MoodColor(float ratio)
        => ratio <= 0.5f ? TextGreen : (ratio <= 0.85f ? TextGold : (ratio <= 1.05f ? TextOrange : TextRed));

    private void DrawHelp()
    {
        FillRect(new Rectangle(0, 0, WindowWidth, WindowHeight), Color.Black * 0.75f);
        int w = 900, h = 640;
        var panel = new Rectangle((WindowWidth - w) / 2, (WindowHeight - h) / 2, w, h);
        FillRect(panel, PanelColor);
        OutlineRect(panel, TextGold);

        int x = panel.X + 24;
        int y = panel.Y + 20;
        _font.Draw(_spriteBatch, "HOW TO PLAY", x, y, TextGold, 3); y += 40;

        string[] lines =
        {
            "TOWNS EMIT COMPUTE DEMAND THAT GROWS EVERY DAY.",
            "ONLY BUILDINGS INSIDE A TOWN'S TINTED CATCHMENT SERVE IT.",
            "",
            "SHEDS ARE CHEAP CASH FLOW. HALLS AND HYPERSCALE SERVE MORE",
            "DEMAND PER TILE AND PER POINT OF ANGER - BUT RUN HOT.",
            "BLUE RIVERFRONT TILES COOL UPKEEP TO 55%. CLAIM THEM.",
            "",
            "CROWDING A TOWN PAST ITS COMFORT MAKES IT FURIOUS - ANGER",
            "GROWS FASTER THE FURTHER PAST COMFORT YOU PACK.",
            "PARKS RAISE A TOWN'S COMFORT. FORESTS LEFT STANDING CALM",
            "TOWNS TOO - CLEARING THEM COSTS 2.5X AND REMOVES THE CALM.",
            "",
            "UNSERVED DEMAND (BROWNOUTS) ALSO ANGERS EVERYONE.",
            "YOU CANNOT SERVE IT ALL FOREVER. CHOOSE WHAT TO SACRIFICE.",
            "",
            "BUILD OVER A CHEAPER BUILDING TO UPGRADE IT IN PLACE.",
            "REACH THE COMPANY VALUE GOAL BEFORE THE TOWNS REVOLT",
            "OR THE MONEY RUNS OUT.",
        };
        foreach (var line in lines)
        {
            _font.Draw(_spriteBatch, line, x, y, line.Length == 0 ? TextDim : Color.White);
            y += 24;
        }
        y += 12;
        _font.Draw(_spriteBatch, "PRESS H TO CLOSE", x, y, TextGold);
    }

    private void DrawEndScreen()
    {
        FillRect(new Rectangle(0, 0, WindowWidth, WindowHeight), Color.Black * 0.72f);
        bool won = _sim.State == RunState.Won;
        string title = won ? "YOU WIN" : "GAME OVER";
        Color titleColor = won ? TextGold : TextRed;
        int scale = 8;
        int tw = _font.Width(title, scale);
        _font.Draw(_spriteBatch, title, (WindowWidth - tw) / 2, WindowHeight / 2 - 170, titleColor, scale);

        DrawCentered(_sim.EndReason, WindowHeight / 2 - 40, Color.White);
        DrawCentered($"DAY {_sim.Day}   VALUE {Money(_sim.NetWorth)}   HAPPY {_sim.Happiness:F0}", WindowHeight / 2, TextDim);
        DrawCentered("R RETRY THIS REGION      N NEW REGION", WindowHeight / 2 + 60, TextGold);
    }

    private void DrawCentered(string text, int y, Color color)
        => _font.Draw(_spriteBatch, text, (WindowWidth - _font.Width(text)) / 2, y, color);

    private void DrawLabel(string text, int x, int y, Color color)
    {
        var r = new Rectangle(x - 3, y - 3, _font.Width(text) + 6, _font.Height() + 6);
        FillRect(r, Color.Black * 0.62f);
        _font.Draw(_spriteBatch, text, x, y, color);
    }

    private Color HappinessColor(float h)
        => h < Sim.HAPPINESS_LOSE + 5f ? TextRed : (h < Sim.HAPPINESS_WARN ? TextOrange : (h < 70f ? TextGold : TextGreen));

    private void DrawBar(Rectangle rect, float frac, Color fill)
    {
        FillRect(rect, new Color(50, 52, 62));
        int w = (int)(rect.Width * Math.Clamp(frac, 0f, 1f));
        FillRect(new Rectangle(rect.X, rect.Y, w, rect.Height), fill);
        OutlineRect(rect, new Color(12, 12, 16));
    }

    private void FillRect(Rectangle rect, Color color) => _spriteBatch.Draw(_pixel, rect, color);

    private void OutlineRect(Rectangle rect, Color color)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y + rect.Height - 1, rect.Width, 1), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + rect.Width - 1, rect.Y, 1, rect.Height), color);
    }

    private void SaveGame()
    {
        if (_shotPath != null) return;
        try { File.WriteAllText(SaveFileName, _sim.Serialize()); } catch { }
    }

    private bool LoadGame()
    {
        try
        {
            if (!File.Exists(SaveFileName)) return false;
            var sim = Sim.Deserialize(File.ReadAllText(SaveFileName));
            if (sim == null) return false;
            _sim = sim;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveScreenshot()
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[w * h];
        GraphicsDevice.GetBackBufferData(data);
        using var tex = new Texture2D(GraphicsDevice, w, h);
        tex.SetData(data);
        using var stream = File.Create(_shotPath);
        tex.SaveAsPng(stream, w, h);
    }
}
