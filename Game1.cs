using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DataCenterTycoon;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private Texture2D _pixel;
    private SpriteFont _font;

    private const int WindowWidth = 1920;
    private const int WindowHeight = 1080;
    private const string SaveFileName = "save.dat";
    private const int GridArea = MapGenerator.GridWidth * MapGenerator.GridHeight;

    private const float START_MONEY = 650f;
    private const float WIN_CASH_GOAL = 7000f;
    private const float COST_PER_TILE = 12f;
    private const float REFUND_FRACTION = 0.5f;
    private const float OP_COST_PER_TILE = 1.0f;
    private const float REVENUE_PER_SERVED = 2.4f;
    private const float COMPUTE_PER_TILE = 1.0f;
    private const float BASE_DEMAND = 100f;
    private const float DAILY_DEMAND_GROWTH = 0.07f;
    private const float FOOTPRINT_WEIGHT = 130f;
    private const float FOOTPRINT_EXP = 1.8f;
    private const float BROWNOUT_WEIGHT = 70f;
    private const float HAPPINESS_FALL = 0.55f;
    private const float HAPPINESS_RISE = 0.16f;
    private const float HAPPINESS_LOSE = 35f;
    private const float HAPPINESS_WARN = 50f;

    private static readonly Color BackgroundColor = new(90, 170, 90);
    private static readonly Color ActiveFillColor = new(80, 160, 230);
    private static readonly Color ActiveOutlineColor = new(150, 210, 255);
    private static readonly Color InvalidFillColor = new(230, 80, 80);
    private static readonly Color InvalidOutlineColor = new(255, 150, 150);
    private static readonly Color PlacedFillColor = new(220, 70, 70);
    private static readonly Color PlacedOutlineColor = new(255, 130, 130);
    private static readonly Color RiverColor = new(60, 110, 200);
    private static readonly Color TownColor = new(160, 110, 70);
    private static readonly Color ForestColor = new(30, 80, 30);
    private static readonly Color PanelColor = new(0, 0, 0);
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    private enum GameState { Playing, Won, Lost }

    private MapGenerator _map;
    private readonly List<DataCenter> _dataCenters = new();
    private Vector2 _dragStart;
    private bool _dragging;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    private float _money;
    private float _computeDemand;
    private float _computeSupply;
    private int _tileCount;
    private float _weightedCovered;
    private float _happiness;
    private int _day;
    private float _lastRevenue;
    private float _lastOpCost;
    private float _lastNet;
    private GameState _state;
    private string _endReason = "";

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = WindowWidth;
        _graphics.PreferredBackBufferHeight = WindowHeight;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Data Center Tycoon";
    }

    protected override void Initialize()
    {
        if (!LoadGame())
            StartNewGame();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _font = Content.Load<SpriteFont>("Font");
    }

    private void StartNewGame()
    {
        long seed = Environment.TickCount64;
        _map = new MapGenerator(seed);
        ResetState();
    }

    private void RetryMap()
    {
        _map = new MapGenerator(_map.Seed);
        ResetState();
    }

    private void ResetState()
    {
        _dataCenters.Clear();
        _money = START_MONEY;
        _computeDemand = BASE_DEMAND;
        _happiness = 100f;
        _day = 1;
        _lastRevenue = 0f;
        _lastOpCost = 0f;
        _lastNet = 0f;
        _state = GameState.Playing;
        _endReason = "";
        _dragging = false;
        RecalculateCoverage();
        SaveGame();
    }

    protected override void Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        if (Pressed(keyboard, Keys.Escape)) Exit();
        if (Pressed(keyboard, Keys.N)) StartNewGame();
        if (Pressed(keyboard, Keys.R)) RetryMap();

        if (_state == GameState.Playing)
        {
            if (Pressed(keyboard, Keys.Space)) AdvanceDay();
            HandlePlacement(mouse);
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys key)
        => kb.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    private void HandlePlacement(MouseState mouse)
    {
        var mousePos = new Vector2(mouse.X, mouse.Y);

        bool leftPressed = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool leftReleased = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;

        if (leftPressed)
        {
            _dragStart = mousePos;
            _dragging = true;
        }

        if (leftReleased && _dragging)
        {
            _dragging = false;
            var box = TileBox(_dragStart, mousePos);
            if (IsRegionClear(box))
            {
                float cost = CostOf(box);
                if (_money >= cost)
                {
                    _dataCenters.Add(new DataCenter(box, cost));
                    _money -= cost;
                    RecalculateCoverage();
                    SaveGame();
                }
            }
        }

        if (rightPressed)
        {
            var clicked = _dataCenters.FirstOrDefault(dc => dc.Bounds.Contains(mouse.X, mouse.Y));
            if (clicked != null)
            {
                _dataCenters.Remove(clicked);
                _money += clicked.Cost * REFUND_FRACTION;
                RecalculateCoverage();
                SaveGame();
            }
        }
    }

    private void AdvanceDay()
    {
        float served = Math.Min(_computeSupply, _computeDemand);
        _lastRevenue = REVENUE_PER_SERVED * served;
        _lastOpCost = OP_COST_PER_TILE * _tileCount;
        _lastNet = _lastRevenue - _lastOpCost;
        _money += _lastNet;

        StepHappiness();

        _computeDemand *= 1f + DAILY_DEMAND_GROWTH;
        _day++;

        if (_happiness < HAPPINESS_LOSE)
        {
            _state = GameState.Lost;
            _endReason = "The community revolted against your sprawl.";
        }
        else if (_money < 0f)
        {
            _state = GameState.Lost;
            _endReason = "You went bankrupt.";
        }
        else if (_money >= WIN_CASH_GOAL)
        {
            _state = GameState.Won;
            _endReason = $"You built a {WIN_CASH_GOAL.ToString("C0", Usd)} compute empire.";
        }

        SaveGame();
    }

    private void StepHappiness() => _happiness = ProjectedHappiness();

    private float ProjectedHappiness()
    {
        float target = HappinessTarget(_computeSupply, _weightedCovered, _computeDemand);
        float rate = target < _happiness ? HAPPINESS_FALL : HAPPINESS_RISE;
        return Math.Clamp(_happiness + rate * (target - _happiness), 0f, 100f);
    }

    private static float HappinessTarget(float supply, float weightedCovered, float demand)
    {
        float servedFrac = demand > 0f ? Math.Min(1f, supply / demand) : 1f;
        float footprint = FOOTPRINT_WEIGHT * (float)Math.Pow(weightedCovered / GridArea, FOOTPRINT_EXP);
        float brownout = BROWNOUT_WEIGHT * (1f - servedFrac);
        return 100f - footprint - brownout;
    }

    private void RecalculateCoverage()
    {
        var covered = new bool[MapGenerator.GridWidth, MapGenerator.GridHeight];
        int ts = MapGenerator.TileSize;

        foreach (var dc in _dataCenters)
        {
            int startX = Math.Max(0, dc.Bounds.X / ts);
            int startY = Math.Max(0, dc.Bounds.Y / ts);
            int endX = Math.Min(MapGenerator.GridWidth, (dc.Bounds.X + dc.Bounds.Width + ts - 1) / ts);
            int endY = Math.Min(MapGenerator.GridHeight, (dc.Bounds.Y + dc.Bounds.Height + ts - 1) / ts);
            for (int x = startX; x < endX; x++)
                for (int y = startY; y < endY; y++)
                    covered[x, y] = true;
        }

        int count = 0;
        float weighted = 0f;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                if (!covered[x, y]) continue;
                count++;
                weighted += TileWeight(_map.Tiles[x, y]);
            }

        _tileCount = count;
        _weightedCovered = weighted;
        _computeSupply = count * COMPUTE_PER_TILE;
    }

    private static float TileWeight(TileType t) => t switch
    {
        TileType.Empty => 1f,
        TileType.Forest => 1.6f,
        TileType.River => 2.5f,
        TileType.Town => 4f,
        _ => 1f
    };

    private static Rectangle TileBox(Vector2 a, Vector2 b)
    {
        int ts = MapGenerator.TileSize;
        int ax = Math.Clamp((int)(a.X / ts), 0, MapGenerator.GridWidth - 1);
        int ay = Math.Clamp((int)(a.Y / ts), 0, MapGenerator.GridHeight - 1);
        int bx = Math.Clamp((int)(b.X / ts), 0, MapGenerator.GridWidth - 1);
        int by = Math.Clamp((int)(b.Y / ts), 0, MapGenerator.GridHeight - 1);
        int x0 = Math.Min(ax, bx), y0 = Math.Min(ay, by);
        int x1 = Math.Max(ax, bx), y1 = Math.Max(ay, by);
        return new Rectangle(x0 * ts, y0 * ts, (x1 - x0 + 1) * ts, (y1 - y0 + 1) * ts);
    }

    private bool IsRegionClear(Rectangle box)
    {
        foreach (var dc in _dataCenters)
            if (dc.Bounds.Intersects(box))
                return false;
        return box.Width > 0 && box.Height > 0;
    }

    private float CostOf(Rectangle box)
    {
        int ts = MapGenerator.TileSize;
        int tiles = (box.Width / ts) * (box.Height / ts);
        return tiles * COST_PER_TILE;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(BackgroundColor);
        _spriteBatch.Begin();

        DrawMap();
        DrawDataCenters();
        DrawActiveRect();
        DrawHud();

        if (_state != GameState.Playing)
            DrawEndScreen();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawMap()
    {
        int ts = MapGenerator.TileSize;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var tile = _map.Tiles[x, y];
                if (tile == TileType.Empty) continue;
                var rect = new Rectangle(x * ts, y * ts, ts, ts);
                FillRect(rect, tile switch
                {
                    TileType.River => RiverColor,
                    TileType.Town => TownColor,
                    TileType.Forest => ForestColor,
                    _ => Color.Transparent
                });
            }
    }

    private void DrawDataCenters()
    {
        int ts = MapGenerator.TileSize;
        foreach (var dc in _dataCenters)
        {
            int startX = Math.Max(0, dc.Bounds.X / ts);
            int startY = Math.Max(0, dc.Bounds.Y / ts);
            int endX = Math.Min(MapGenerator.GridWidth, (dc.Bounds.X + dc.Bounds.Width + ts - 1) / ts);
            int endY = Math.Min(MapGenerator.GridHeight, (dc.Bounds.Y + dc.Bounds.Height + ts - 1) / ts);
            for (int x = startX; x < endX; x++)
                for (int y = startY; y < endY; y++)
                {
                    var rect = new Rectangle(x * ts, y * ts, ts, ts);
                    FillRect(rect, PlacedFillColor);
                    OutlineRect(rect, PlacedOutlineColor);
                }
        }
    }

    private void DrawActiveRect()
    {
        if (!_dragging || _state != GameState.Playing) return;

        var mouse = Mouse.GetState();
        var box = TileBox(_dragStart, new Vector2(mouse.X, mouse.Y));
        bool clear = IsRegionClear(box);
        float cost = CostOf(box);
        bool valid = clear && _money >= cost;

        Color fill = valid ? ActiveFillColor : InvalidFillColor;
        Color outline = valid ? ActiveOutlineColor : InvalidOutlineColor;

        int ts = MapGenerator.TileSize;
        for (int x = box.X / ts; x < (box.X + box.Width) / ts; x++)
            for (int y = box.Y / ts; y < (box.Y + box.Height) / ts; y++)
            {
                var rect = new Rectangle(x * ts, y * ts, ts, ts);
                FillRect(rect, fill * 0.3f);
                OutlineRect(rect, outline);
            }

        string label = clear ? $"Build: {cost.ToString("C0", Usd)}" : "Occupied";
        var pos = new Vector2(box.X, box.Y - 24);
        _spriteBatch.DrawString(_font, label, pos, valid ? Color.White : InvalidOutlineColor);
    }

    private void DrawHud()
    {
        const int px = 10, py = 10, pw = 560, ph = 250;
        DrawPanel(new Rectangle(px, py, pw, ph), 0.55f);
        int x = px + 14;
        int y = py + 12;

        float goalFrac = Math.Clamp(_money / WIN_CASH_GOAL, 0f, 1f);
        _spriteBatch.DrawString(_font, $"Day {_day}", new Vector2(x, y), Color.White);
        _spriteBatch.DrawString(_font, $"Goal: {WIN_CASH_GOAL.ToString("C0", Usd)}", new Vector2(px + pw - 170, y), Color.Gold);
        y += 28;

        _spriteBatch.DrawString(_font, $"Money: {_money.ToString("C0", Usd)}", new Vector2(x, y), Color.White);
        DrawBar(new Rectangle(x + 200, y + 4, 320, 14), goalFrac, Color.Gold, new Color(60, 60, 60));
        y += 30;

        float proj = ProjectedHappiness();
        Color hColor = HappinessColor(_happiness);
        string hText = $"Happiness: {_happiness:F0}";
        _spriteBatch.DrawString(_font, hText, new Vector2(x, y), hColor);
        if (_dragging)
            _spriteBatch.DrawString(_font, $"-> {proj:F0}", new Vector2(x + 170, y), HappinessColor(proj));
        var hbar = new Rectangle(x + 280, y + 4, 240, 14);
        DrawBar(hbar, _happiness / 100f, hColor, new Color(60, 60, 60));
        int markX = hbar.X + (int)(hbar.Width * (HAPPINESS_LOSE / 100f));
        FillRect(new Rectangle(markX, hbar.Y - 2, 2, hbar.Height + 4), Color.OrangeRed);
        y += 30;

        float servedPct = _computeDemand > 0f ? Math.Min(1f, _computeSupply / _computeDemand) * 100f : 100f;
        Color servedColor = servedPct >= 99.5f ? Color.LightGreen : (servedPct >= 70f ? Color.White : Color.Orange);
        _spriteBatch.DrawString(_font, $"Demand: {_computeDemand:F0}    Supply: {_computeSupply:F0}", new Vector2(x, y), Color.White);
        y += 26;
        _spriteBatch.DrawString(_font, $"Served: {servedPct:F0}%    (next demand: {_computeDemand * (1f + DAILY_DEMAND_GROWTH):F0})", new Vector2(x, y), servedColor);
        y += 30;

        if (_day > 1)
        {
            Color netColor = _lastNet >= 0 ? Color.LightGreen : Color.Red;
            _spriteBatch.DrawString(_font,
                $"Last day:  Rev {_lastRevenue.ToString("C0", Usd)}   Op {_lastOpCost.ToString("C0", Usd)}   Net {_lastNet.ToString("C0", Usd)}",
                new Vector2(x, y), netColor);
        }
        else
        {
            _spriteBatch.DrawString(_font, "Drag to build data centers, then advance the day.", new Vector2(x, y), Color.LightGray);
        }
        y += 30;

        _spriteBatch.DrawString(_font, "SPACE Next Day    Right-click Remove    R Retry    N New Map", new Vector2(x, y), Color.Gold);

        if (_happiness < HAPPINESS_WARN && _state == GameState.Playing)
        {
            string warn = "WARNING: happiness critical - the community is turning against you.";
            _spriteBatch.DrawString(_font, warn, new Vector2(px, WindowHeight - 34), Color.OrangeRed);
        }
    }

    private void DrawEndScreen()
    {
        FillRect(new Rectangle(0, 0, WindowWidth, WindowHeight), PanelColor * 0.65f);

        string title = _state == GameState.Won ? "YOU WIN" : "GAME OVER";
        Color titleColor = _state == GameState.Won ? Color.Gold : Color.OrangeRed;
        float scale = 3.2f;
        var titleSize = _font.MeasureString(title) * scale;
        var titlePos = new Vector2((WindowWidth - titleSize.X) / 2f, WindowHeight / 2f - 150);
        _spriteBatch.DrawString(_font, title, titlePos, titleColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

        DrawCentered(_endReason, WindowHeight / 2f + 10, Color.White);
        DrawCentered($"Day {_day}    {_money.ToString("C0", Usd)}    Happiness {_happiness:F0}", WindowHeight / 2f + 50, Color.LightGray);
        DrawCentered("Press R to retry this map     -     N for a new map", WindowHeight / 2f + 110, Color.Gold);
    }

    private void DrawCentered(string text, float y, Color color)
    {
        var size = _font.MeasureString(text);
        _spriteBatch.DrawString(_font, text, new Vector2((WindowWidth - size.X) / 2f, y), color);
    }

    private static Color HappinessColor(float h)
        => h < HAPPINESS_LOSE + 5f ? Color.Red : (h < HAPPINESS_WARN ? Color.Orange : (h < 70f ? Color.Yellow : Color.LightGreen));

    private void DrawPanel(Rectangle rect, float alpha)
        => FillRect(rect, PanelColor * alpha);

    private void DrawBar(Rectangle rect, float frac, Color fill, Color bg)
    {
        FillRect(rect, bg);
        int w = (int)(rect.Width * Math.Clamp(frac, 0f, 1f));
        FillRect(new Rectangle(rect.X, rect.Y, w, rect.Height), fill);
        OutlineRect(rect, new Color(20, 20, 20));
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
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("v2");
            sb.AppendLine($"seed={_map.Seed}");
            sb.AppendLine($"day={_day}");
            sb.AppendLine(FormattableString.Invariant($"money={_money}"));
            sb.AppendLine(FormattableString.Invariant($"demand={_computeDemand}"));
            sb.AppendLine(FormattableString.Invariant($"happiness={_happiness}"));
            sb.AppendLine($"state={_state}");
            foreach (var dc in _dataCenters)
                sb.AppendLine(FormattableString.Invariant($"dc={dc.Bounds.X},{dc.Bounds.Y},{dc.Bounds.Width},{dc.Bounds.Height},{dc.Cost}"));
            File.WriteAllText(SaveFileName, sb.ToString());
        }
        catch { }
    }

    private bool LoadGame()
    {
        try
        {
            if (!File.Exists(SaveFileName)) return false;
            var lines = File.ReadAllLines(SaveFileName);
            if (lines.Length == 0) return false;

            if (lines[0].Trim() != "v2")
            {
                if (long.TryParse(lines[0].Trim(), out var legacySeed))
                {
                    _map = new MapGenerator(legacySeed);
                    ResetState();
                    return true;
                }
                return false;
            }

            long seed = 0;
            int day = 1;
            float money = START_MONEY, demand = BASE_DEMAND, happiness = 100f;
            var state = GameState.Playing;
            var dcs = new List<DataCenter>();

            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq);
                string val = line.Substring(eq + 1);
                switch (key)
                {
                    case "seed": long.TryParse(val, out seed); break;
                    case "day": int.TryParse(val, out day); break;
                    case "money": money = ParseFloat(val, START_MONEY); break;
                    case "demand": demand = ParseFloat(val, BASE_DEMAND); break;
                    case "happiness": happiness = ParseFloat(val, 100f); break;
                    case "state": Enum.TryParse(val, out state); break;
                    case "dc":
                        var p = val.Split(',');
                        if (p.Length == 5
                            && int.TryParse(p[0], out var rx) && int.TryParse(p[1], out var ry)
                            && int.TryParse(p[2], out var rw) && int.TryParse(p[3], out var rh))
                        {
                            float cost = ParseFloat(p[4], 0f);
                            dcs.Add(new DataCenter(new Rectangle(rx, ry, rw, rh), cost));
                        }
                        break;
                }
            }

            _map = new MapGenerator(seed);
            _dataCenters.Clear();
            _dataCenters.AddRange(dcs);
            _money = money;
            _computeDemand = demand;
            _happiness = happiness;
            _day = day;
            _state = state;
            _lastRevenue = _lastOpCost = _lastNet = 0f;
            _endReason = state == GameState.Won
                ? $"You built a {WIN_CASH_GOAL.ToString("C0", Usd)} compute empire."
                : (state == GameState.Lost ? "Your run ended." : "");
            RecalculateCoverage();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float ParseFloat(string s, float fallback)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
