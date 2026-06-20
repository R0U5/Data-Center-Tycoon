using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    private static readonly Color BackgroundColor = new(90, 170, 90);
    private static readonly Color ActiveFillColor = new(80, 160, 230);
    private static readonly Color ActiveOutlineColor = new(150, 210, 255);
    private static readonly Color PlacedFillColor = new(220, 70, 70);
    private static readonly Color PlacedOutlineColor = new(255, 130, 130);
    private static readonly Color RiverColor = new(60, 110, 200);
    private static readonly Color TownColor = new(160, 110, 70);
    private static readonly Color ForestColor = new(30, 80, 30);

    private MapGenerator _map;
    private readonly List<DataCenter> _dataCenters = new();
    private Vector2 _dragStart;
    private Rectangle? _activeRect;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private float _money = 1000f;
    private const float CostPerTile = 10f;

    private const float UNDERSUPPLY_WEIGHT = 40f;
    private const float COVERAGE_WEIGHT = 35f;
    // private const float TILE_PENALTY = 0.5f; // replaced by log curve
    private const float TILE_PENALTY_LOG_SCALE = 6f;     // controls steepness of log curve
    private const float TILE_PENALTY_CAP = 18f;           // hard ceiling on tile happiness penalty
    private const float COMPUTE_PER_TILE = 1f;
    private const float DAILY_DEMAND_GROWTH = 0.07f;      // reduced from 0.10, now daily
    private const float BASE_HAPPINESS = 100f;
    private const float BASE_DEMAND = 100f;
    private const float PROFIT_PER_DEMAND_UNIT = 2.0f;    // reverted from 2.5
    private const float HAPPINESS_RECOVERY_RATE = 0.5f;
    // op cost scales with demand — idle tiles become increasingly painful in late game
    private const float OVERSTOCK_BONUS_MULTIPLIER = 1.12f;     // 12% bonus when ratio >= 1.2

    private float _computeDemand;
    private float _computeSupply;
    private float _supplyDemandRatio;
    private float _previousDayRatio;
    private int _day;
    private float _dailyProfit;
    private float _dailyOperatingCost;
    private int _undersupplyStreak = 0;
    private bool _showNextDayButton;
    private bool _gameOver;
    private bool _happinessWarning;

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
        LoadOrCreateMap();
        ResetEconomy();
        base.Initialize();
    }

    private void LoadOrCreateMap()
    {
        long seed;
        if (File.Exists(SaveFileName) && long.TryParse(File.ReadAllText(SaveFileName).Trim(), out var saved))
            seed = saved;
        else
        {
            seed = Environment.TickCount64;
            File.WriteAllText(SaveFileName, seed.ToString());
        }
        _map = new MapGenerator(seed);
    }

    private void StartNewGame()
    {
        long seed = Environment.TickCount64;
        File.WriteAllText(SaveFileName, seed.ToString());
        _map = new MapGenerator(seed);
        _dataCenters.Clear();
        _money = 1000f;
        ResetEconomy();
    }

    private void ResetEconomy()
    {
        _computeDemand = BASE_DEMAND;
        _computeSupply = 0f;
        _supplyDemandRatio = 0f;
        _previousDayRatio = 0f;
        _day = 1;
        _dailyProfit = 0f;
        _dailyOperatingCost = 0f;
        _undersupplyStreak = 0;
        _showNextDayButton = true;
        _gameOver = false;
        _happinessWarning = false;
        RecalculateSupply();
        UpdateRatio();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _font = Content.Load<SpriteFont>("Font");
    }

    protected override void Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        
        if (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape))
            Exit();
        if (keyboard.IsKeyDown(Keys.R) && !_previousKeyboard.IsKeyDown(Keys.R))
        {
            _dataCenters.Clear();
            _money = 1000f;
            ResetEconomy();
        }
        if (keyboard.IsKeyDown(Keys.N) && !_previousKeyboard.IsKeyDown(Keys.N))
            StartNewGame();
        if (keyboard.IsKeyDown(Keys.Space) && !_previousKeyboard.IsKeyDown(Keys.Space) && _showNextDayButton && !_gameOver)
            AdvanceDay();

        if (_gameOver)
        {
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        var mousePos = new Vector2(mouse.X, mouse.Y);

        bool justPressed = mouse.LeftButton == ButtonState.Pressed
                           && _previousMouse.LeftButton == ButtonState.Released;
        bool justReleased = mouse.LeftButton == ButtonState.Released
                            && _previousMouse.LeftButton == ButtonState.Pressed;

        if (justPressed)
        {
            if (TryPlaceSingleTile(mouse.X, mouse.Y))
            {
                _activeRect = null;
            }
            else
            {
                _dragStart = mousePos;
                _activeRect = new Rectangle((int)mousePos.X, (int)mousePos.Y, 0, 0);
            }
        }
        else if (_activeRect != null && mouse.LeftButton == ButtonState.Pressed)
        {
            _activeRect = RectFromCorners(_dragStart, mousePos);
        }

        if (justReleased && _activeRect is Rectangle final)
        {
            if (final.Width > 0 && final.Height > 0)
            {
                var gridRect = AlignRectToGrid(final);
                if (gridRect.Width > 0 && gridRect.Height > 0)
                {
                    float cost = CalculateCost(gridRect);
                    if (_money >= cost)
                    {
                        _dataCenters.Add(new DataCenter(gridRect, cost));
                        _money -= cost;
                        RecalculateSupply();
                        UpdateRatio();
                    }
                }
            }
            _activeRect = null;
        }

        bool rightJustPressed = mouse.RightButton == ButtonState.Pressed
                                && _previousMouse.RightButton == ButtonState.Released;
        if (rightJustPressed)
        {
            var clicked = _dataCenters.FirstOrDefault(dc => dc.Bounds.Contains(mouse.X, mouse.Y));
            if (clicked != null)
            {
                _dataCenters.Remove(clicked);
                _money += clicked.Cost;
                RecalculateSupply();
                UpdateRatio();
            }
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    private void AdvanceDay()
    {
        _dailyProfit = CalculateDailyProfit();
        _dailyOperatingCost = CalculateDailyOperatingCost();
        float netProfit = _dailyProfit - _dailyOperatingCost;
        _money += netProfit;
        _day++;
        
        float unmetDemand = Math.Max(0f, _computeDemand - _computeSupply);
        float happinessChange = 0f;
        if (_previousDayRatio >= 1.0f)
        {
            float unhappiness = BASE_HAPPINESS - CalculateHappiness();
            happinessChange = unhappiness * HAPPINESS_RECOVERY_RATE;
        }
        else if (_previousDayRatio > 0f)
        {
            float unmetRatio = 1f - _previousDayRatio;
            happinessChange = -unmetRatio * UNDERSUPPLY_WEIGHT * 0.5f;
        }
        
        _computeDemand *= (1f + DAILY_DEMAND_GROWTH);
        RecalculateSupply();
        
        if (_previousDayRatio < 1.0f)
            _undersupplyStreak++;
        else
            _undersupplyStreak = 0;
        
        _previousDayRatio = _supplyDemandRatio;
        UpdateRatio();
        _showNextDayButton = true;
        
        CheckGameOver();
    }

    private float CalculateDailyOperatingCost()
    {
        int tileCount = (int)(_computeSupply / COMPUTE_PER_TILE);
        float effectiveOpCost = 0.60f + (0.004f * (_computeDemand / 100f));
        return tileCount * effectiveOpCost;
    }

    private void UpdateRatio()
    {
        _supplyDemandRatio = _computeDemand > 0f ? _computeSupply / _computeDemand : 0f;
    }

    private float CalculateDailyProfit()
    {
        if (_day == 1)
            return 0f;
        
        float ratio = _previousDayRatio;
        if (ratio <= 0f)
            return 0f;
        
        float cappedRatio = Math.Min(ratio, 1f);
        float overstockBonus = (ratio >= 1.2f) ? OVERSTOCK_BONUS_MULTIPLIER : 1.0f;
        return _computeDemand * PROFIT_PER_DEMAND_UNIT * cappedRatio * overstockBonus;
    }

    private void CheckGameOver()
    {
        float happiness = CalculateHappiness();
        _happinessWarning = happiness < 50f;
        _gameOver = happiness < 40f;
        if (_gameOver)
        {
            _showNextDayButton = false;
        }
    }

    private void RecalculateSupply()
    {
        int tileCount = 0;
        int ts = MapGenerator.TileSize;
        foreach (var dc in _dataCenters)
        {
            int startX = Math.Max(0, dc.Bounds.X / ts);
            int startY = Math.Max(0, dc.Bounds.Y / ts);
            int endX = Math.Min(MapGenerator.GridWidth, (dc.Bounds.X + dc.Bounds.Width + ts - 1) / ts);
            int endY = Math.Min(MapGenerator.GridHeight, (dc.Bounds.Y + dc.Bounds.Height + ts - 1) / ts);
            tileCount += (endX - startX) * (endY - startY);
        }
        _computeSupply = tileCount * COMPUTE_PER_TILE;
    }

    private static Rectangle RectFromCorners(Vector2 a, Vector2 b)
    {
        var x = (int)Math.Min(a.X, b.X);
        var y = (int)Math.Min(a.Y, b.Y);
        var w = (int)Math.Abs(b.X - a.X);
        var h = (int)Math.Abs(b.Y - a.Y);
        return new Rectangle(x, y, w, h);
    }

    private static Rectangle AlignRectToGrid(Rectangle rect)
    {
        int ts = MapGenerator.TileSize;
        int startX = Math.Max(0, rect.X / ts);
        int startY = Math.Max(0, rect.Y / ts);
        int endX = Math.Min(MapGenerator.GridWidth, (rect.X + rect.Width + ts - 1) / ts);
        int endY = Math.Min(MapGenerator.GridHeight, (rect.Y + rect.Height + ts - 1) / ts);
        return new Rectangle(
            startX * ts,
            startY * ts,
            (endX - startX) * ts,
            (endY - startY) * ts);
    }

    private float CalculateHappiness()
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

        int empty = 0, river = 0, town = 0, forest = 0;
        int coveredTiles = 0;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
        {
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                if (!covered[x, y]) continue;
                coveredTiles++;
                switch (_map.Tiles[x, y])
                {
                    case TileType.Empty: empty++; break;
                    case TileType.River: river++; break;
                    case TileType.Town: town++; break;
                    case TileType.Forest: forest++; break;
                }
            }
        }

        int total = MapGenerator.GridWidth * MapGenerator.GridHeight;
        int weighted = empty + river * 2 + town * 3 + forest;
        float coveragePenalty = (weighted / (float)total) * COVERAGE_WEIGHT;
        float undersupplyPenalty = (_undersupplyStreak > 2)
            ? Math.Max(0f, 1f - _previousDayRatio) * UNDERSUPPLY_WEIGHT
            : 0f;
        float tilePenalty = Math.Min(TILE_PENALTY_CAP, (float)Math.Log(coveredTiles + 1) * TILE_PENALTY_LOG_SCALE);
        return Math.Min(BASE_HAPPINESS, BASE_HAPPINESS - coveragePenalty - undersupplyPenalty - tilePenalty);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(BackgroundColor);

        _spriteBatch.Begin();

        DrawMap();
        DrawDataCenters();
        DrawActiveRect();
        DrawHappiness();
        DrawProfit();
        DrawMoney();
        DrawSupplyDemand();
        DrawDayInfo();
        DrawActiveRectCost();

        if (_gameOver)
        {
            string gameOverText = "GAME OVER - Happiness below 40";
            var textSize = _font.MeasureString(gameOverText);
            var pos = new Vector2((WindowWidth - textSize.X) / 2, (WindowHeight - textSize.Y) / 2);
            _spriteBatch.DrawString(_font, gameOverText, pos, Color.Red);
        }
        else if (_happinessWarning)
        {
            string warningText = "WARNING: Happiness below 50 - Game over at 40!";
            _spriteBatch.DrawString(_font, warningText, new Vector2(10, WindowHeight - 30), Color.Orange);
        }

        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawHappiness()
    {
        float happiness = CalculateHappiness();
        Color color = happiness < 50f ? Color.Red : (happiness < 70f ? Color.Orange : Color.White);
        string text = $"Happiness: {happiness:F0}";
        _spriteBatch.DrawString(_font, text, new Vector2(10, 10), color);
    }

    private void DrawProfit()
    {
        float grossProfit = CalculateDailyProfit();
        float currentOpCost = CalculateDailyOperatingCost();
        float netProfit = grossProfit - currentOpCost;
        Color profitColor = netProfit >= 0 ? Color.LightGreen : Color.Red;
        string text = $"Gross: {grossProfit.ToString("C", CultureInfo.GetCultureInfo("en-US"))}  OpCost: {currentOpCost.ToString("C", CultureInfo.GetCultureInfo("en-US"))}  Net: {netProfit.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
        _spriteBatch.DrawString(_font, text, new Vector2(10, 35), profitColor);
    }

    private void DrawMoney()
    {
        string text = $"Money: {_money.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
        _spriteBatch.DrawString(_font, text, new Vector2(10, 60), Color.White);
    }

    private void DrawSupplyDemand()
    {
        string text = $"Demand: {_computeDemand:F0}  Supply: {_computeSupply:F0}  Ratio: {_supplyDemandRatio:F2}";
        _spriteBatch.DrawString(_font, text, new Vector2(10, 85), Color.White);
    }

    private void DrawDayInfo()
    {
        string text = $"Day: {_day}";
        _spriteBatch.DrawString(_font, text, new Vector2(10, 110), Color.White);
        if (_showNextDayButton && !_gameOver)
        {
            string nextDayText = "Press SPACE for Next Day";
            _spriteBatch.DrawString(_font, nextDayText, new Vector2(10, 135), Color.Yellow);
        }
        if (_day > 1)
        {
            float netProfit = _dailyProfit - _dailyOperatingCost;
            Color netColor = netProfit >= 0 ? Color.LightGreen : Color.Red;
            string lastDayText = $"Last Day: Gross {_dailyProfit.ToString("C", CultureInfo.GetCultureInfo("en-US"))} - OpCost {_dailyOperatingCost.ToString("C", CultureInfo.GetCultureInfo("en-US"))} = Net {netProfit.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
            _spriteBatch.DrawString(_font, lastDayText, new Vector2(10, 160), netColor);
        }
    }

    private void DrawActiveRectCost()
    {
        if (_activeRect is Rectangle active && active.Width > 0 && active.Height > 0)
        {
            float placementCost = CalculateCost(active);
            string text = $"Placement Cost: {placementCost.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
            _spriteBatch.DrawString(_font, text, new Vector2(10, 185), Color.White);
        }
    }

    private int CountTilesInRect(Rectangle rect)
    {
        int ts = MapGenerator.TileSize;
        int startX = Math.Max(0, rect.X / ts);
        int startY = Math.Max(0, rect.Y / ts);
        int endX = Math.Min(MapGenerator.GridWidth, (rect.X + rect.Width + ts - 1) / ts);
        int endY = Math.Min(MapGenerator.GridHeight, (rect.Y + rect.Height + ts - 1) / ts);
        return Math.Max(0, (endX - startX) * (endY - startY));
    }

    private float CalculateCost(Rectangle rect)
    {
        int tilesWide = (int)Math.Ceiling(rect.Width / (float)MapGenerator.TileSize);
        int tilesHigh = (int)Math.Ceiling(rect.Height / (float)MapGenerator.TileSize);
        return tilesWide * tilesHigh * CostPerTile;
    }

    private bool TryPlaceSingleTile(int screenX, int screenY)
    {
        int ts = MapGenerator.TileSize;
        int gridX = screenX / ts;
        int gridY = screenY / ts;

        if (gridX < 0 || gridX >= MapGenerator.GridWidth || gridY < 0 || gridY >= MapGenerator.GridHeight)
            return false;

        if (IsTileOccupied(gridX, gridY))
            return false;

        if (!IsAdjacentToDataCenter(gridX, gridY))
            return false;

        var rect = new Rectangle(gridX * ts, gridY * ts, ts, ts);
        float cost = CalculateCost(rect);
        if (_money >= cost)
        {
            _dataCenters.Add(new DataCenter(rect, cost));
            _money -= cost;
            RecalculateSupply();
            return true;
        }
        return false;
    }

    private bool IsTileOccupied(int gridX, int gridY)
    {
        int ts = MapGenerator.TileSize;
        foreach (var dc in _dataCenters)
        {
            int startX = Math.Max(0, dc.Bounds.X / ts);
            int startY = Math.Max(0, dc.Bounds.Y / ts);
            int endX = Math.Min(MapGenerator.GridWidth, (dc.Bounds.X + dc.Bounds.Width + ts - 1) / ts);
            int endY = Math.Min(MapGenerator.GridHeight, (dc.Bounds.Y + dc.Bounds.Height + ts - 1) / ts);

            if (gridX >= startX && gridX < endX && gridY >= startY && gridY < endY)
                return true;
        }
        return false;
    }

    private bool IsAdjacentToDataCenter(int gridX, int gridY)
    {
        foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nx = gridX + dx;
            int ny = gridY + dy;
            if (nx < 0 || nx >= MapGenerator.GridWidth || ny < 0 || ny >= MapGenerator.GridHeight) continue;
            if (IsTileOccupied(nx, ny)) return true;
        }
        return false;
    }

    private void DrawMap()
    {
        for (int x = 0; x < MapGenerator.GridWidth; x++)
        {
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var tile = _map.Tiles[x, y];
                if (tile == TileType.Empty) continue;
                var rect = new Rectangle(
                    x * MapGenerator.TileSize,
                    y * MapGenerator.TileSize,
                    MapGenerator.TileSize,
                    MapGenerator.TileSize);
                FillRect(rect, tile switch
                {
                    TileType.River => RiverColor,
                    TileType.Town => TownColor,
                    TileType.Forest => ForestColor,
                    _ => Color.Transparent
                });
            }
        }
    }

    private void DrawDataCenters()
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

        for (int x = 0; x < MapGenerator.GridWidth; x++)
        {
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                if (!covered[x, y]) continue;
                var rect = new Rectangle(x * ts, y * ts, ts, ts);
                FillRect(rect, PlacedFillColor);
                OutlineRect(rect, PlacedOutlineColor);
            }
        }
    }

    private void DrawActiveRect()
    {
        if (_activeRect is not Rectangle active) return;

        int ts = MapGenerator.TileSize;
        int startX = Math.Max(0, active.X / ts);
        int startY = Math.Max(0, active.Y / ts);
        int endX = Math.Min(MapGenerator.GridWidth, (active.X + active.Width + ts - 1) / ts);
        int endY = Math.Min(MapGenerator.GridHeight, (active.Y + active.Height + ts - 1) / ts);

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                var rect = new Rectangle(x * ts, y * ts, ts, ts);
                FillRect(rect, ActiveFillColor * 0.25f);
                OutlineRect(rect, ActiveOutlineColor);
            }
        }
    }

    private void FillRect(Rectangle rect, Color color)
    {
        _spriteBatch.Draw(_pixel, rect, color);
    }

    private void OutlineRect(Rectangle rect, Color color)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y + rect.Height - 1, rect.Width, 1), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + rect.Width - 1, rect.Y, 1, rect.Height), color);
    }
}
