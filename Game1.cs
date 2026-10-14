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
    private const int TargetWidth = 1920;
    private const int TargetHeight = 1080;
    private const int Ts = 33;
    private const int MapW = MapGenerator.GridWidth * Ts;
    private const int MapH = MapGenerator.GridHeight * Ts;
    private const int SideX = MapW;
    private const int SideW = TargetWidth - MapW;

    private static readonly string SaveFileName = Path.Combine(AppContext.BaseDirectory, "save.dat");
    private static readonly string SettingsFileName = Path.Combine(AppContext.BaseDirectory, "settings.dat");
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    private static readonly Color BgColor = new(24, 26, 34);
    private static readonly Color PanelColor = new(16, 18, 24);
    private static readonly Color PanelLine = new(70, 76, 96);
    private static readonly Color ChunkFill = new(28, 30, 40);
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

    private enum Tool { Shed, Hall, Campus, Park, Cooling, Power, Recycler, Raze }
    private enum Screen { Menu, Playing, Ended }

    private static readonly (Tool tool, StructureType type, Sprite icon, string key)[] ToolDefs =
    {
        (Tool.Shed, StructureType.Shed, Sprite.Shed, "1"),
        (Tool.Hall, StructureType.Hall, Sprite.HallA, "2"),
        (Tool.Campus, StructureType.Campus, Sprite.CampusA, "3"),
        (Tool.Park, StructureType.Park, Sprite.Park, "4"),
        (Tool.Cooling, StructureType.CoolingTower, Sprite.Cooling, "5"),
        (Tool.Power, StructureType.PowerPlant, Sprite.Power, "6"),
        (Tool.Recycler, StructureType.Recycler, Sprite.Recycler, "7"),
        (Tool.Raze, StructureType.None, Sprite.Raze, "8"),
    };

    private readonly record struct UndoStep(int x0, int y0, int x1, int y1, StructureType type, bool wasBuild);
    private readonly record struct MenuButton(string label, Rectangle rect, int action);

    private readonly GraphicsDeviceManager _graphics;
    private RenderTarget2D _scene;
    private SpriteBatch _spriteBatch;
    private Texture2D _pixel;
    private PixelFont _font;
    private PixelArt _art;
    private Sfx _sfx;
    private Music _music;

    private Sim _sim;
    private Screen _screen = Screen.Menu;
    private Tool _tool = Tool.Shed;
    private Point _dragStart;
    private bool _dragging;
    private MouseState _prevMouse;
    private KeyboardState _prevKeyboard;
    private float _animTime;
    private bool _showHelp;
    private bool _warned;
    private bool _soundOn = true;
    private readonly List<(string text, Color color, float expires)> _toasts = new();
    private readonly Stack<UndoStep> _undo = new();
    private float _clock;
    private int _hover = -1;
    private string _tickerText = "";
    private float _tickerUntil;

    private bool _tutorialSeen;
    public bool TutorialJustSeen => _tutorialSeen;
    private int _hoverTooltip = -1;

    private readonly string _shotPath;
    private readonly int _shotDays;
    private readonly long? _shotSeed;
    private int _frameCount;

    private MenuButton[] _menuButtons = Array.Empty<MenuButton>();

    private enum Screen2 { Title, Setup }
    private Difficulty _selDifficulty = Difficulty.Standard;
    private bool _selEvents;
    private bool _setupOpen;
    private readonly record struct SetupRect(string label, Rectangle rect, int action);
    private SetupRect[] _setupRects = Array.Empty<SetupRect>();
    private int _setupHover = -1;

    public Game1(string shotPath = null, long? shotSeed = null, int shotDays = 0)
    {
        _shotPath = shotPath;
        _shotSeed = shotSeed;
        _shotDays = shotDays;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = TargetWidth,
            PreferredBackBufferHeight = TargetHeight,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "DATA TYCOON";
        IsFixedTimeStep = false;
    }

    protected override void Initialize()
    {
        if (_shotPath != null)
        {
            _sim = new Sim(new MapGenerator(_shotSeed ?? 1000L));
            if (_shotDays <= -200) { _screen = Screen.Menu; _setupOpen = true; }
            else if (_shotDays <= -100) { _screen = Screen.Menu; _setupOpen = false; }
            else
            {
                _screen = Screen.Playing;
                if (_shotDays < 0) _showHelp = true;
                else BalanceHarness.PlayDays(_sim, _shotDays);
            }
        }
        else
        {
            LoadSettings();
            _sim = new Sim(new MapGenerator(Environment.TickCount64));
            _screen = Screen.Menu;
        }
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _scene = new RenderTarget2D(GraphicsDevice, TargetWidth, TargetHeight);
        _pixel = new Texture2D(GraphicsDevice, 8, 8);
        var px = new Color[64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.White;
        _pixel.SetData(px);
        _font = new PixelFont(GraphicsDevice);
        _art = new PixelArt(GraphicsDevice);
        _sfx = new Sfx();
        _music = new Music();
        ApplySoundState();
        BuildMenuButtons();
    }

    private void BuildMenuButtons()
    {
        int w = 440, h = 56, cx = TargetWidth / 2 - w / 2;
        int y = TargetHeight / 2 - 100;
        var list = new List<MenuButton>();
        if (File.Exists(SaveFileName))
        {
            list.Add(new MenuButton("CONTINUE", new Rectangle(cx, y, w, h), 0));
            y += h + 16;
        }
        list.Add(new MenuButton("NEW GAME", new Rectangle(cx, y, w, h), 1));
        y += h + 16;
        list.Add(new MenuButton(_soundOn ? "SOUND: ON" : "SOUND: OFF", new Rectangle(cx, y, w, h), 2));
        y += h + 16;
        list.Add(new MenuButton("QUIT", new Rectangle(cx, y, w, h), 3));
        _menuButtons = list.ToArray();
        BuildSetupRects();
    }

    private void BuildSetupRects()
    {
        int w = 360, h = 52, cx = TargetWidth / 2 - w / 2;
        int y = 470;
        var list = new List<SetupRect>();
        list.Add(new SetupRect("EASIER", new Rectangle(cx - 190, y, 120, h), 10));
        list.Add(new SetupRect("HARDER", new Rectangle(cx + 190 - 120 + 250, y, 120, h), 11));
        y += h + 18;
        list.Add(new SetupRect("TOGGLE EVENTS", new Rectangle(cx, y, w, h), 12));
        y += h + 18;
        list.Add(new SetupRect("START", new Rectangle(cx, y + 12, w, 60), 13));
        list.Add(new SetupRect("BACK", new Rectangle(60, TargetHeight - 90, 160, 52), 14));
        _setupRects = list.ToArray();
    }

    private void ApplySoundState()
    {
        _sfx.Muted = !_soundOn;
        _music.Muted = !_soundOn;
    }

    private void StartNewGame()
    {
        _sim = new Sim(new MapGenerator(Environment.TickCount64));
        _sim.Configure(_selDifficulty, _selEvents);
        _warned = false;
        _toasts.Clear();
        _undo.Clear();
        _tutorialSeen = false;
        _screen = Screen.Playing;
        SaveGame();
    }

    private void ContinueGame()
    {
        if (LoadGame() && _sim != null)
        {
            _tutorialSeen = true;
            _screen = _sim.State == RunState.Playing ? Screen.Playing : Screen.Ended;
        }
        else StartNewGame();
    }

    private void RetryMap()
    {
        var d = _sim.Difficulty;
        bool ev = _sim.EventsOptIn;
        _sim = new Sim(new MapGenerator(_sim.Map.Seed));
        _sim.Configure(d, ev);
        _warned = false;
        _toasts.Clear();
        _undo.Clear();
        _tutorialSeen = true;
        _screen = Screen.Playing;
        SaveGame();
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);
        _animTime += dt;
        _clock += dt;
        _toasts.RemoveAll(t => t.expires < _clock);

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        var v = ToVirtual(mouse);

        if (_screen == Screen.Menu)
        {
            if (_setupOpen) UpdateSetup(v, mouse, keyboard);
            else UpdateMenu(v, mouse, keyboard);
            _prevMouse = mouse;
            _prevKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (Pressed(keyboard, Keys.Escape))
        {
            if (_showHelp) _showHelp = false;
            else { _screen = Screen.Menu; BuildMenuButtons(); }
        }
        if (Pressed(keyboard, Keys.H) && _screen == Screen.Playing) _showHelp = !_showHelp;
        if (Pressed(keyboard, Keys.M)) { _soundOn = !_soundOn; ApplySoundState(); SaveSettings(); Toast(_soundOn ? "SOUND ON" : "SOUND OFF", TextDim); }

        if (_screen == Screen.Ended)
        {
            if (Pressed(keyboard, Keys.R)) RetryMap();
            if (Pressed(keyboard, Keys.N)) StartNewGame();
        }

        if (_sim.State == RunState.Playing && _screen == Screen.Playing && !_showHelp)
        {
            if (Pressed(keyboard, Keys.D1)) SelectTool(Tool.Shed);
            if (Pressed(keyboard, Keys.D2)) SelectTool(Tool.Hall);
            if (Pressed(keyboard, Keys.D3)) SelectTool(Tool.Campus);
            if (Pressed(keyboard, Keys.D4)) SelectTool(Tool.Park);
            if (Pressed(keyboard, Keys.D5)) SelectTool(Tool.Cooling);
            if (Pressed(keyboard, Keys.D6)) SelectTool(Tool.Power);
            if (Pressed(keyboard, Keys.D7)) SelectTool(Tool.Recycler);
            if (Pressed(keyboard, Keys.X)) SelectTool(Tool.Raze);
            if (Pressed(keyboard, Keys.Space)) AdvanceDay();
            if (Pressed(keyboard, Keys.Z)) Undo();
            if (_sim.PendingChoice != null)
            {
                if (Pressed(keyboard, Keys.D1)) { _sim.ResolveChoice(true); _sfx.Play(Sound.Select, 0.5f); SaveGame(); }
                if (Pressed(keyboard, Keys.D2)) { _sim.ResolveChoice(false); _sfx.Play(Sound.Select, 0.5f); SaveGame(); }
            }
            else HandleMouse(v, mouse);
        }

        _prevMouse = mouse;
        _prevKeyboard = keyboard;
        base.Update(gameTime);
    }

    private void UpdateMenu(Point v, MouseState raw, KeyboardState keyboard)
    {
        _hover = -1;
        for (int i = 0; i < _menuButtons.Length; i++)
            if (_menuButtons[i].rect.Contains(v.X, v.Y)) _hover = i;

        if (Pressed(keyboard, Keys.Down) || Pressed(keyboard, Keys.S))
        { _hoverSel = (_hoverSel + 1) % _menuButtons.Length; _sfx.Play(Sound.Select, 0.3f); }
        if (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.W))
        { _hoverSel = (_hoverSel - 1 + _menuButtons.Length) % _menuButtons.Length; _sfx.Play(Sound.Select, 0.3f); }
        if (_hover >= 0) _hoverSel = _hover;
        _hover = _hoverSel;

        bool click = raw.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if ((click && _hover >= 0) || Pressed(keyboard, Keys.Enter))
        {
            if (_hover < 0) _hover = _menuButtons.Length > 1 ? 0 : 0;
            _sfx.Play(Sound.Select, 0.4f);
            DoMenuAction(_menuButtons[_hover].action);
        }
    }

    private void DoMenuAction(int action)
    {
        switch (action)
        {
            case 0: ContinueGame(); break;
            case 1: _setupOpen = true; break;
            case 2: _soundOn = !_soundOn; ApplySoundState(); SaveSettings(); BuildMenuButtons(); break;
            case 3: Exit(); break;
        }
        _tutorialSeen = true;
    }

    private void UpdateSetup(Point v, MouseState raw, KeyboardState keyboard)
    {
        _setupHover = -1;
        for (int i = 0; i < _setupRects.Length; i++)
            if (_setupRects[i].rect.Contains(v.X, v.Y)) _setupHover = i;

        if (Pressed(keyboard, Keys.Down) || Pressed(keyboard, Keys.S))
        { _setupSel = (_setupSel + 1) % _setupRects.Length; _sfx.Play(Sound.Select, 0.3f); }
        if (Pressed(keyboard, Keys.Up) || Pressed(keyboard, Keys.W))
        { _setupSel = (_setupSel - 1 + _setupRects.Length) % _setupRects.Length; _sfx.Play(Sound.Select, 0.3f); }
        if (Pressed(keyboard, Keys.Left) || Pressed(keyboard, Keys.A)) { CycleDifficulty(-1); _sfx.Play(Sound.Select, 0.35f); }
        if (Pressed(keyboard, Keys.Right) || Pressed(keyboard, Keys.D)) { CycleDifficulty(1); _sfx.Play(Sound.Select, 0.35f); }
        if (_setupHover >= 0) _setupSel = _setupHover;

        bool click = raw.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (click && _setupHover >= 0)
        {
            _sfx.Play(Sound.Select, 0.4f);
            DoSetupAction(_setupRects[_setupHover].action);
        }
        else if (Pressed(keyboard, Keys.Enter))
        {
            _sfx.Play(Sound.Select, 0.45f);
            DoSetupAction(_setupRects[_setupSel].action);
        }
        if (Pressed(keyboard, Keys.Escape)) _setupOpen = false;
    }

    private int _hoverSel;
    private int _setupSel;
    private string _lastScoreOutcome = "";
    private int _lastScoreRank = -1;

    private void DoSetupAction(int action)
    {
        switch (action)
        {
            case 10: CycleDifficulty(-1); break;
            case 11: CycleDifficulty(1); break;
            case 12: _selEvents = !_selEvents; break;
            case 13: StartNewGame(); break;
            case 14: _setupOpen = false; break;
        }
    }

    private void CycleDifficulty(int dir)
    {
        int d = (int)_selDifficulty + dir;
        int max = Enum.GetValues<Difficulty>().Length - 1;
        _selDifficulty = (Difficulty)Math.Clamp(d, 0, max);
    }

    private bool Pressed(KeyboardState kb, Keys key) => kb.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        _sfx.Play(Sound.Select, 0.35f);
    }

    private void Undo()
    {
        if (_undo.Count == 0) { Toast("NOTHING TO UNDO", TextDim); return; }
        var step = _undo.Pop();
        if (step.wasBuild)
        {
            _sim.DemolishRegion(step.x0, step.y0, step.x1, step.y1);
            Toast("UNDID BUILD", TextCyan);
        }
        else
        {
            _sim.PlaceRegion(step.x0, step.y0, step.x1, step.y1, step.type);
            Toast("UNDID RAZE", TextCyan);
        }
        SaveGame();
    }

    private void HandleMouse(Point mouse, MouseState raw)
    {
        bool leftPressed = raw.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool leftReleased = raw.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightPressed = raw.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

        if (leftPressed && mouse.X >= SideX)
        {
            foreach (var (tool, _, _, _) in ToolDefs)
                if (ToolButtonRect(tool).Contains(mouse.X, mouse.Y)) { SelectTool(tool); return; }
            return;
        }

        if (leftPressed && InMap(mouse)) { _dragStart = TileAt(mouse); _dragging = true; }

        if (leftReleased && _dragging) { _dragging = false; ApplyTool(_dragStart, TileAt(mouse)); }

        if (rightPressed && InMap(mouse))
        {
            var p = TileAt(mouse);
            var s = _sim.Grid[p.X, p.Y];
            if (s != StructureType.None)
            {
                _undo.Push(new UndoStep(p.X, p.Y, p.X, p.Y, s, false));
                if (_sim.DemolishAt(p.X, p.Y)) { _sfx.Play(Sound.Demolish); SaveGame(); }
            }
        }
    }

    private void ApplyTool(Point a, Point b)
    {
        if (_tool == Tool.Raze)
        {
            var snapshot = new List<(int x, int y, StructureType s)>();
            for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                    if (_sim.Grid[x, y] != StructureType.None) snapshot.Add((x, y, _sim.Grid[x, y]));
            int removed = _sim.DemolishRegion(a.X, a.Y, b.X, b.Y);
            if (removed > 0)
            {
                if (snapshot.Count == 1)
                    _undo.Push(new UndoStep(snapshot[0].x, snapshot[0].y, snapshot[0].x, snapshot[0].y, snapshot[0].s, false));
                _sfx.Play(Sound.Demolish);
                Toast($"RAZED {removed} TILES", TextOrange);
                SaveGame();
            }
            else _sfx.Play(Sound.Deny, 0.35f);
            return;
        }

        var type = ToolDefs[(int)_tool].type;
        var (tiles, cost) = _sim.RegionPlan(a.X, a.Y, b.X, b.Y, type);
        if (tiles == 0) { _sfx.Play(Sound.Deny, 0.35f); Toast(CantPlaceReason(a.X, a.Y, type), TextRed); return; }
        if (_sim.Money < cost) { _sfx.Play(Sound.Deny, 0.35f); Toast("NOT ENOUGH CASH", TextRed); return; }

        _sim.PlaceRegion(a.X, a.Y, b.X, b.Y, type);
        _undo.Push(new UndoStep(a.X, a.Y, b.X, b.Y, type, true));
        _sfx.Play(Sound.Build);
        Toast($"BUILT {tiles} FOR {Money(cost)}", TextGreen);
        SaveGame();
    }

    private string CantPlaceReason(int x, int y, StructureType type)
    {
        if (x < 0 || y < 0 || x >= MapGenerator.GridWidth || y >= MapGenerator.GridHeight) return "OFF MAP";
        var t = _sim.Map.Tiles[x, y];
        if (t == TileType.River) return "CANNOT BUILD ON RIVER";
        if (t == TileType.Town) return "CANNOT BUILD ON TOWN";
        if (_sim.Grid[x, y] == type) return "ALREADY BUILT";
        if (_sim.Grid[x, y] != StructureType.None) return "UPGRADE ONLY TO HIGHER TIER";
        return "CANNOT BUILD HERE";
    }

    private void AdvanceDay()
    {
        _sim.AdvanceDay();
        _sfx.Play(Sound.Day, 0.4f);
        string sign = _sim.LastNet >= 0 ? "+" : "";
        _tickerText = $"DAY {_sim.Day}   REVENUE {Money(_sim.LastRevenue)}   UPKEEP {Money(-_sim.LastOpCost)}   NET {sign}{Money(_sim.LastNet)}";
        _tickerUntil = _clock + 3.4f;

        if (_sim.State == RunState.Won) { _sfx.Play(Sound.Win, 0.7f); FinishRun(); }
        else if (_sim.State == RunState.Lost) { _sfx.Play(Sound.Lose, 0.7f); FinishRun(); }
        else if (_sim.Happiness < Sim.HAPPINESS_WARN && !_warned)
        {
            _warned = true;
            _sfx.Play(Sound.Warn, 0.6f);
            Toast("THE TOWNS ARE TURNING ON YOU", TextRed);
        }
        else if (_sim.Happiness >= Sim.HAPPINESS_WARN + 8f) _warned = false;

        SaveGame();
    }

    private void FinishRun()
    {
        _screen = Screen.Ended;
        if (_shotPath != null) return;

        var score = new Score
        {
            Value = _sim.NetWorth,
            Day = _sim.Day,
            Structures = _sim.StructureTiles,
            Difficulty = DifficultyName(_sim.Difficulty),
            Outcome = _sim.State == RunState.Won ? "WIN" : (Money(_sim.NetWorth).Length > 0 ? "LOSS" : "LOSS"),
        };
        bool isHigh = HighScores.IsHighScore(score.Value);
        HighScores.Submit(score);
        _lastScoreOutcome = score.Outcome;
        var list = HighScores.Load();
        _lastScoreRank = -1;
        for (int i = 0; i < list.Count; i++)
            if (Math.Abs(list[i].Value - score.Value) < 0.5f && list[i].Day == score.Day) { _lastScoreRank = i + 1; break; }
        if (isHigh) Toast("NEW HIGH SCORE!", TextGold);
    }

    private void Toast(string text, Color color)
    {
        if (_toasts.Count > 0 && _toasts[^1].text == text) { _toasts[^1] = (text, color, _clock + 3.5f); return; }
        _toasts.Add((text, color, _clock + 3.5f));
        if (_toasts.Count > 4) _toasts.RemoveAt(0);
    }

    private static bool InMap(Point m) => m.X >= 0 && m.X < MapW && m.Y >= 0 && m.Y < MapH;

    private static Point TileAt(Point m)
        => new(Math.Clamp(m.X / Ts, 0, MapGenerator.GridWidth - 1), Math.Clamp(m.Y / Ts, 0, MapGenerator.GridHeight - 1));

    private Point ToVirtual(MouseState m)
    {
        var vp = GraphicsDevice.Viewport;
        if (vp.Width <= 0 || vp.Height <= 0) return new Point(m.X, m.Y);
        return new Point(m.X * TargetWidth / vp.Width, m.Y * TargetHeight / vp.Height);
    }

    private static string Money(float v) => v.ToString("C0", Usd);
    private static string ShortMoney(float v) => Math.Abs(v) >= 10000f ? $"${v / 1000f:F1}K" : v.ToString("C0", Usd);

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(_scene);
        GraphicsDevice.Clear(BgColor);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        if (_screen == Screen.Menu)
        {
            if (_setupOpen) DrawSetup();
            else DrawMenu();
        }
        else
        {
            var v = ToVirtual(Mouse.GetState());
            UpdateHover(v);
            DrawMap();
            DrawDragPreview();
            DrawTownLabels();
            DrawSidebar();
            DrawBottomBar(v);
            DrawToasts();
            DrawTicker();
            DrawTooltip(v);
            DrawEventBanner();
            if (_showHelp) DrawHelp();
            if (_screen == Screen.Ended) DrawEndScreen();
        }

        _spriteBatch.End();
        GraphicsDevice.SetRenderTarget(null);

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawSceneScaled();
        _spriteBatch.End();

        base.Draw(gameTime);

        _frameCount++;
        if (_shotPath != null && _frameCount >= 2)
        {
            SaveScreenshot();
            Exit();
        }
    }

    private void SaveScreenshot()
    {
        var canvas = new SoftCanvas(TargetWidth, TargetHeight);
        canvas.Clear(BgColor);
        ComposeSoft(canvas);
        PngWriter.Write(_shotPath, canvas.Pixels, canvas.Width, canvas.Height);
    }

    private void ComposeSoft(SoftCanvas c)
    {
        var pal = PixelArt.PaletteDict;
        var map = _sim.Map;
        int frame = AnimFrame;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var dest = new Rectangle(x * Ts, y * Ts, Ts, Ts);
                var tile = map.Tiles[x, y];
                var structure = _sim.Grid[x, y];
                Sprite baseSprite;
                if (tile == TileType.River) baseSprite = frame == 0 ? Sprite.WaterA : Sprite.WaterB;
                else if (structure != StructureType.None) baseSprite = StructureSprite(structure, frame, x, y);
                else if (tile == TileType.Forest) baseSprite = Sprite.Tree;
                else if (tile == TileType.Town) baseSprite = (x + y) % 2 == 0 ? Sprite.HouseA : Sprite.HouseB;
                else baseSprite = (x * 7 + y * 13) % 3 == 0 ? Sprite.GrassB : Sprite.GrassA;
                c.BlitPattern(PixelArt.PatternFor(baseSprite), pal, dest, Color.White);
            }

        c.Fill(new Rectangle(SideX, 0, SideW, TargetHeight), PanelColor);
        c.Fill(new Rectangle(SideX, 0, 3, TargetHeight), PanelLine);
        ComposeSoftSidebar(c, pal);
        c.Fill(new Rectangle(0, MapH, MapW, TargetHeight - MapH), BgColor);
        c.Fill(new Rectangle(0, MapH, MapW, 3), PanelLine);
        ComposeSoftBottom(c);
    }

    private void ComposeSoftSidebar(SoftCanvas c, Dictionary<char, Color> pal)
    {
        int x = SideX + 12, y = 12;
        var logo = new Rectangle(x, y, SideW - 24, 48);
        SoftBevel(c, logo, new Color(22, 24, 32));
        SoftText(c, "DATA", logo.X + 14, logo.Y + 8, TextGold, 3);
        SoftText(c, "TYCOON", logo.X + 14, logo.Y + 26, TextCyan, 3);
        y += 58;

        y = SoftChunk(c, x, y, "FINANCE", new (string, Color)[]
        {
            ($"DAY {_sim.Day}", Color.White),
            ($"CASH {ShortMoney(_sim.Money)}", Color.White),
            ($"VALUE {ShortMoney(_sim.NetWorth)}", TextGold),
            ($"GOAL {ShortMoney(_sim.EffectiveWinGoal)}", TextDim),
        }, bar: _sim.NetWorth / _sim.EffectiveWinGoal, barColor: TextGold);

        Color hColor = HappinessColor(_sim.Happiness);
        y = SoftChunk(c, x, y, "MORALE", new (string, Color)[]
        {
            ($"HAPPY {_sim.Happiness:F0}", hColor),
            ($"REVOLT {Sim.HAPPINESS_LOSE:F0}", TextDim),
        }, bar: _sim.Happiness / 100f, barColor: hColor, markFrac: Sim.HAPPINESS_LOSE / 100f);

        var (demand, served) = _sim.DemandServed();
        float pct = demand > 0f ? served / demand * 100f : 100f;
        Color sColor = pct >= 99.5f ? TextGreen : (pct >= 70f ? Color.White : TextOrange);
        y = SoftChunk(c, x, y, "GRID", new (string, Color)[]
        {
            ($"SERVED {served:F0}/{demand:F0}", sColor),
            ($"BROWNOUT {100f - pct:F0}%", pct >= 99.5f ? TextDim : TextOrange),
            ($"POWER {_sim.PowerProduced:F0}/{_sim.PowerDemanded:F0}", _sim.PowerShortfallActive ? TextRed : TextCyan),
            ($"COOLED {_sim.CooledStructureTiles}/{_sim.StructureTiles}", TextCyan),
        });

        SoftText(c, "BUILD", x + 6, y + 4, TextDim, 2);
        y += 24;
        for (int i = 0; i < ToolDefs.Length; i++)
        {
            var (tool, type, icon, key) = ToolDefs[i];
            var r = ToolButtonRect(tool);
            bool selected = _tool == tool;
            SoftBevel(c, r, selected ? new Color(46, 50, 68) : ChunkFill);
            c.BlitPattern(PixelArt.PatternFor(icon), pal, new Rectangle(r.X + 3, r.Y + 3, 32, 32), Color.White);
            string nm = Structures.Display(type);
            SoftText(c, $"{key} {nm}", r.X + 44, r.Y + 5, selected ? Color.White : TextDim, 2);
            SoftText(c, type != StructureType.None ? ShortMoney(Structures.Get(type).Cost) : "50% REFUND", r.X + 44, r.Y + 21, selected ? TextGold : TextDim, 2);
        }
    }

    private void ComposeSoftBottom(SoftCanvas c)
    {
        var map = _sim.Map;
        int towns = map.Towns.Count;
        int chipGap = 12;
        int chipW = Math.Max(360, (MapW - chipGap * (towns + 1)) / Math.Max(1, towns));
        int chipH = TargetHeight - MapH - (chipGap + 10);
        for (int i = 0; i < towns; i++)
        {
            var t = map.Towns[i];
            int cx = chipGap + i * (chipW + chipGap), cy = MapH + 10;
            var chip = new Rectangle(cx, cy, chipW, chipH);
            SoftBevel(c, chip, new Color(22, 24, 32));
            c.Fill(new Rectangle(chip.X + 4, chip.Y + 4, chip.Width - 8, chip.Height - 8), ChunkFill);
            c.Fill(new Rectangle(chip.X + 4, chip.Y + 4, 4, chip.Height - 8), TownTints[i % TownTints.Length]);
            float dem = _sim.DemandForTown(t.Id);
            float served = Math.Min(dem, _sim.TownCompute(t.Id));
            float ratio = _sim.TownLoadRatio(t.Id);
            SoftText(c, TownName(t.Id), cx + 16, cy + 8, Color.White, 2);
            SoftText(c, $"SRV {served:F0}/{dem:F0}", cx + chipW - 260, cy + 8, served >= dem - 0.5f ? TextGreen : TextOrange, 2);
            SoftText(c, MoodWord(ratio), cx + chipW - 120, cy + 8, MoodColor(ratio), 2);
            SoftText(c, $"CROWDING {ratio * 100f:F0}%", cx + 16, cy + 34, TextDim, 2);
        }
    }

    private void SoftBevel(SoftCanvas c, Rectangle r, Color fill)
    {
        c.Fill(r, fill);
        c.Fill(new Rectangle(r.X, r.Y, r.Width, 2), PanelLine);
        c.Fill(new Rectangle(r.X, r.Y, 2, r.Height), PanelLine);
        c.Fill(new Rectangle(r.X, r.Y + r.Height - 2, r.Width, 2), new Color(8, 8, 12));
        c.Fill(new Rectangle(r.X + r.Width - 2, r.Y, 2, r.Height), new Color(8, 8, 12));
    }

    private int SoftChunk(SoftCanvas c, int x, int y, string title, (string text, Color color)[] lines, float bar = -1f, Color? barColor = null, float markFrac = -1f)
    {
        int h = 30 + lines.Length * 21 + (bar >= 0f ? 16 : 4);
        var panel = new Rectangle(x, y, SideW - 24, h);
        SoftBevel(c, panel, new Color(22, 24, 32));
        c.Fill(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, panel.Height - 8), ChunkFill);
        c.Fill(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, 18), new Color(38, 42, 56));
        SoftText(c, title, panel.X + 10, panel.Y + 7, TextCyan, 2);
        int ly = panel.Y + 28;
        foreach (var (text, color) in lines) { SoftText(c, text, panel.X + 10, ly, color, 2); ly += 21; }
        if (bar >= 0f)
        {
            var br = new Rectangle(panel.X + 10, ly + 2, panel.Width - 20, 8);
            c.Fill(br, new Color(50, 52, 62));
            c.Fill(new Rectangle(br.X, br.Y, (int)(br.Width * Math.Clamp(bar, 0f, 1f)), br.Height), barColor ?? TextGold);
            c.Outline(br, new Color(12, 12, 16));
            if (markFrac >= 0f) c.Fill(new Rectangle(br.X + (int)(br.Width * markFrac), br.Y - 2, 2, br.Height + 4), TextRed);
        }
        return y + h + 8;
    }

    private void SoftText(SoftCanvas c, string text, int x, int y, Color color, int scale = 2)
    {
        int cx = x;
        foreach (var raw in text)
        {
            char ch = char.ToUpperInvariant(raw);
            if (ch == '\n') continue;
            if (PixelFont.GlyphRows(ch) is string[] rows)
                c.BlitPattern(rows, PixelFont.GlyphPalette(color), new Rectangle(cx, y, PixelFont.GlyphW * scale, PixelFont.GlyphH * scale), Color.White);
            else if (ch != ' ')
                c.BlitPattern(PixelFont.GlyphRows('?')!, PixelFont.GlyphPalette(color), new Rectangle(cx, y, PixelFont.GlyphW * scale, PixelFont.GlyphH * scale), Color.White);
            cx += PixelFont.Advance * scale;
        }
    }

    private void DrawSceneScaled()
    {
        var vp = GraphicsDevice.Viewport;
        GraphicsDevice.Clear(Color.Black);
        float scale = Math.Min(vp.Width / (float)TargetWidth, vp.Height / (float)TargetHeight);
        int w = (int)(TargetWidth * scale);
        int h = (int)(TargetHeight * scale);
        _spriteBatch.Draw(_scene, new Rectangle((vp.Width - w) / 2, (vp.Height - h) / 2, w, h), Color.White);
    }

    private void DrawMenu()
    {
        _font.Draw(_spriteBatch, "DATA", TargetWidth / 2 - 190, 150, TextGold, 10);
        _font.Draw(_spriteBatch, "TYCOON", TargetWidth / 2 - 130, 260, TextCyan, 10);
        DrawCentered("AN 8-BIT COMPUTE EMPIRE", 390, TextDim);

        var panel = new Rectangle(TargetWidth / 2 - 260, TargetHeight / 2 - 130, 520, _menuButtons.Length * 50 + 40);
        BevelPanel(panel, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        FillRect(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, panel.Height - 8), ChunkFill);

        for (int i = 0; i < _menuButtons.Length; i++)
        {
            var b = _menuButtons[i];
            bool sel = _hover == i;
            int ty = b.rect.Y + 14;
            if (sel) DrawCursor(b.rect.X - 34, ty, 3);
            _font.Draw(_spriteBatch, b.label, b.rect.X, ty, sel ? TextGold : TextDim, 3);
        }

        if (Blink)
            DrawCentered("PRESS START", TargetHeight - 200, TextGreen);
        DrawCentered("ARROWS OR MOUSE  +  ENTER TO SELECT", TargetHeight - 130, new Color(90, 96, 112));
        DrawCentered("© 2026 GOBLIN SOFTWORKS      ALL RIGHTS RESERVED", TargetHeight - 60, new Color(70, 74, 90));

        var hs = HighScores.Load();
        if (hs.Count > 0)
            _font.Draw(_spriteBatch, $"HI {ShortMoney(hs[0].Value)}", 24, 24, TextGold);
    }

    private static string DifficultyName(Difficulty d) => d switch
    {
        Difficulty.Relaxed => "RELAXED",
        Difficulty.Standard => "STANDARD",
        Difficulty.Hard => "HARD",
        _ => "BRUTAL",
    };

    private static string DifficultyBlurb(Difficulty d) => d switch
    {
        Difficulty.Relaxed => "MORE START CASH, GENTLER DEMAND, FORGIVING ANGER.",
        Difficulty.Standard => "THE CALIBRATED GAME. WINS ARE EARNED.",
        Difficulty.Hard => "LESS CASH, FASTER DEMAND, TOUCHIER NEIGHBOURS.",
        _ => "BARELY ANY CASH. THE TOWNS HATE YOU. GOOD LUCK.",
    };

    private void DrawSetup()
    {
        var frame = new Rectangle(TargetWidth / 2 - 520, 90, 1040, TargetHeight - 210);
        BevelPanel(frame, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        FillRect(new Rectangle(frame.X + 4, frame.Y + 4, frame.Width - 8, frame.Height - 8), ChunkFill);

        _font.Draw(_spriteBatch, "NEW GAME", TargetWidth / 2 - 130, 130, TextGold, 6);
        DrawCentered("CONFIGURE YOUR OPERATION", 220, TextDim);

        var t = Sim.DifficultyTuning(_selDifficulty);
        int cx = TargetWidth / 2;

        _font.Draw(_spriteBatch, "DIFFICULTY", cx - 420, 300, TextCyan, 2);
        DrawCursor(cx - 250, 296, 2);
        _font.Draw(_spriteBatch, $"< {DifficultyName(_selDifficulty)} >", cx - 226, 296, Color.White, 2);
        _font.Draw(_spriteBatch, DifficultyBlurb(_selDifficulty), cx - 226, 332, TextDim);
        _font.Draw(_spriteBatch, $"START {t.startMoney:C0}   GOAL {t.winGoal:C0}   DEMAND +{(Sim.DAILY_DEMAND_GROWTH + t.growthDelta) * 100f:F1}%/DAY", cx - 420, 360, TextDim);

        _font.Draw(_spriteBatch, "EVENTS", cx - 420, 440, TextCyan, 2);
        DrawCursor(cx - 250, 436, 2);
        _font.Draw(_spriteBatch, _selEvents ? "[ ON ]" : "[ OFF ]", cx - 226, 436, _selEvents ? TextGreen : TextDim, 2);
        _font.Draw(_spriteBatch, _selEvents ? "THE NEWS CYCLE WILL SHAPE YOUR RUN." : "A CLEAN, CALIBRATED ECONOMY.", cx - 226, 472, TextDim);

        foreach (var s in _setupRects)
        {
            bool sel = _setupSel >= 0 && _setupRects[_setupSel].action == s.action;
            int tw = _font.Width(s.label, 3);
            int tx = s.rect.X + (s.rect.Width - tw) / 2;
            int ty = s.rect.Y + (s.rect.Height - _font.Height(3)) / 2;
            if (s.action >= 13)
            {
                BevelPanel(s.rect, sel ? new Color(44, 48, 66) : new Color(30, 32, 42),
                    sel ? TextGold : new Color(80, 86, 104), new Color(10, 10, 14));
                if (sel) DrawCursor(s.rect.X - 34, ty, 3);
                _font.Draw(_spriteBatch, s.label, tx, ty, sel ? Color.White : TextDim, 3);
            }
            else
            {
                if (sel) DrawCursor(s.rect.X - 30, s.rect.Y + 14, 2);
                _font.Draw(_spriteBatch, s.label, tx, s.rect.Y + 14, sel ? TextWhiteOrGold() : TextDim, 2);
            }
        }
        DrawCentered("ARROWS MOVE / SELECT   ESC BACK", frame.Bottom - 44, new Color(90, 96, 112));
    }

    private Color TextWhiteOrGold() => Color.White;

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
                if (tile == TileType.River) baseSprite = frame == 0 ? Sprite.WaterA : Sprite.WaterB;
                else if (structure != StructureType.None) baseSprite = StructureSprite(structure, frame, x, y);
                else if (tile == TileType.Forest) baseSprite = Sprite.Tree;
                else if (tile == TileType.Town) baseSprite = (x + y) % 2 == 0 ? Sprite.HouseA : Sprite.HouseB;
                else baseSprite = (x * 7 + y * 13) % 3 == 0 ? Sprite.GrassB : Sprite.GrassA;

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
                    if (t >= 0) FillRect(dest, TownTints[t % TownTints.Length] * 0.10f);
                    if (map.Cooled[x, y] || _sim.LocalCooled[x, y])
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
            StructureType.CoolingTower => Sprite.Cooling,
            StructureType.PowerPlant => Sprite.Power,
            StructureType.Recycler => Sprite.Recycler,
            _ => Sprite.Park
        };
    }

    private void DrawDragPreview()
    {
        if (_sim.State != RunState.Playing || _showHelp) return;
        var mouse = ToVirtual(Mouse.GetState());
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

    private static string ToolName(Tool t) => Structures.Display(ToolDefs[(int)t].type);

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

            var srvBar = new Rectangle(Math.Clamp(cx - 44, 4, MapW - 92), ly + 18, 80, 7);
            Color srvColor = frac >= 0.99f ? TextGreen : (frac >= 0.6f ? TextGold : TextOrange);
            DrawBar(srvBar, frac, srvColor);
            DrawMoodGlyph(new Rectangle(srvBar.Right + 4, srvBar.Y, 8, 8), frac >= 0.99f);

            var moodBar = new Rectangle(srvBar.X, srvBar.Y + 10, 80, 7);
            DrawBar(moodBar, Math.Min(1f, ratio), MoodColor(ratio));
            DrawMoodGlyph(new Rectangle(moodBar.Right + 4, moodBar.Y, 8, 8), ratio <= 0.85f);
        }
    }

    private void DrawMoodGlyph(Rectangle r, bool good)
    {
        if (good)
        {
            FillRect(new Rectangle(r.X, r.Y + 2, r.Width, 2), TextGreen);
            FillRect(new Rectangle(r.X + 2, r.Y, r.Width - 4, 2), TextGreen);
        }
        else
        {
            FillRect(new Rectangle(r.X, r.Y, 2, r.Height), TextRed);
            FillRect(new Rectangle(r.X + r.Width - 2, r.Y, 2, r.Height), TextRed);
        }
    }

    private string TownName(int id)
    {
        int idx = (int)((Math.Abs(_sim.Map.Seed) / 7919 + id * 7) % TownNames.Length);
        if (idx < 0) idx = 0;
        return TownNames[idx];
    }

    private Rectangle ToolButtonRect(Tool tool)
        => new(SideX + 8, 376 + (int)tool * 40, SideW - 16, 38);

    private Rectangle ToolHoverRect(Tool tool) => ToolButtonRect(tool);

    private void UpdateHover(Point v)
    {
        _hoverTooltip = -1;
        for (int i = 0; i < ToolDefs.Length; i++)
            if (ToolHoverRect(ToolDefs[i].tool).Contains(v.X, v.Y)) _hoverTooltip = i;

        if (v.X < SideX)
        {
            if (v.Y < 260) _hoverTooltip = 100;
            else if (v.Y >= MapH) _hoverTooltip = 101;
        }
    }

    private int DrawChunk(int x, int y, string title, (string text, Color color)[] lines, float bar = -1f, Color? barColor = null, float markFrac = -1f)
    {
        int h = 26 + lines.Length * 17 + (bar >= 0f ? 14 : 4);
        var panel = new Rectangle(x, y, SideW - 24, h);
        BevelPanel(panel, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        FillRect(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, panel.Height - 8), ChunkFill);
        FillRect(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, 16), new Color(38, 42, 56));
        _font.Draw(_spriteBatch, title, panel.X + 10, panel.Y + 7, TextCyan);
        int ly = panel.Y + 26;
        foreach (var (text, color) in lines)
        {
            _font.Draw(_spriteBatch, text, panel.X + 10, ly, color);
            ly += 17;
        }
        if (bar >= 0f)
        {
            var barRect = new Rectangle(panel.X + 10, ly + 2, panel.Width - 20, 8);
            DrawBar(barRect, bar, barColor ?? TextGold);
            if (markFrac >= 0f)
            {
                int mx = barRect.X + (int)(barRect.Width * Math.Clamp(markFrac, 0f, 1f));
                FillRect(new Rectangle(mx, barRect.Y - 2, 2, barRect.Height + 4), TextRed);
            }
        }
        return y + h + 8;
    }

    private void DrawSidebar()
    {
        FillRect(new Rectangle(SideX, 0, SideW, TargetHeight), BgColor);
        FillRect(new Rectangle(SideX, 0, 3, TargetHeight), PanelLine);
        FillRect(new Rectangle(SideX + 3, 0, 1, TargetHeight), new Color(8, 8, 12));

        int x = SideX + 12;
        int y = 12;

        var logoFrame = new Rectangle(x, y, SideW - 24, 48);
        BevelPanel(logoFrame, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        _font.Draw(_spriteBatch, "DATA", logoFrame.X + 14, logoFrame.Y + 8, TextGold, 3);
        _font.Draw(_spriteBatch, "TYCOON", logoFrame.X + 14, logoFrame.Y + 26, TextCyan, 3);
        y += 58;

        y = DrawChunk(x, y, "FINANCE", new (string, Color)[]
        {
            ($"DAY {_sim.Day}", Color.White),
            ($"CASH {ShortMoney(_sim.Money)}", Color.White),
            ($"VALUE {ShortMoney(_sim.NetWorth)} / {ShortMoney(_sim.EffectiveWinGoal)}", TextGold),
        }, bar: _sim.NetWorth / _sim.EffectiveWinGoal, barColor: TextGold);

        Color hColor = HappinessColor(_sim.Happiness);
        float proj = _sim.ProjectedHappiness();
        string happy = Math.Abs(proj - _sim.Happiness) >= 1f ? $"HAPPY {_sim.Happiness:F0} > {proj:F0}" : $"HAPPY {_sim.Happiness:F0}";
        y = DrawChunk(x, y, "MORALE", new (string, Color)[]
        {
            (happy, hColor),
            ($"REVOLT BELOW {Sim.HAPPINESS_LOSE:F0}", TextDim),
        }, bar: _sim.Happiness / 100f, barColor: hColor, markFrac: Sim.HAPPINESS_LOSE / 100f);

        var (demand, served) = _sim.DemandServed();
        float pct = demand > 0f ? served / demand * 100f : 100f;
        Color servedColor = pct >= 99.5f ? TextGreen : (pct >= 70f ? Color.White : TextOrange);
        y = DrawChunk(x, y, "GRID", new (string, Color)[]
        {
            ($"SERVED {served:F0}/{demand:F0}", servedColor),
            ($"BROWNOUT {100f - pct:F0}%", pct >= 99.5f ? TextDim : TextOrange),
            ($"POWER {_sim.PowerProduced:F0}/{_sim.PowerDemanded:F0}", _sim.PowerShortfallActive ? TextRed : TextCyan),
            ($"COOLED {_sim.CooledStructureTiles}/{_sim.StructureTiles}", TextCyan),
        });

        if (_sim.HasModifiers)
        {
            var mods = new List<(string, Color)>();
            if (Math.Abs(_sim.EvDemandMult - 1f) > 0.0001f) mods.Add(($"DEMAND x{_sim.EvDemandMult:F2}", _sim.EvDemandMult >= 1f ? TextGreen : TextRed));
            if (Math.Abs(_sim.EvRevenueMult - 1f) > 0.0001f) mods.Add(($"REVENUE x{_sim.EvRevenueMult:F2}", _sim.EvRevenueMult >= 1f ? TextGreen : TextRed));
            if (Math.Abs(_sim.EvUpkeepMult - 1f) > 0.0001f) mods.Add(($"UPKEEP x{_sim.EvUpkeepMult:F2}", _sim.EvUpkeepMult <= 1f ? TextGreen : TextRed));
            if (Math.Abs(_sim.EvBuildCostMult - 1f) > 0.0001f) mods.Add(($"BUILD x{_sim.EvBuildCostMult:F2}", _sim.EvBuildCostMult <= 1f ? TextGreen : TextRed));
            if (Math.Abs(_sim.EvNimbyMult - 1f) > 0.0001f) mods.Add(($"ANGER x{_sim.EvNimbyMult:F2}", _sim.EvNimbyMult <= 1f ? TextGreen : TextRed));
            if (mods.Count > 0) y = DrawChunk(x, y, "NEWS CYCLE", mods.ToArray());
        }

        DrawBuildPalette(x, y);
    }

    private void DrawBuildPalette(int x, int y)
    {
        int headY = y + 4;
        _font.Draw(_spriteBatch, "BUILD", x + 6, headY, TextDim);
        int listY = headY + 20;
        for (int i = 0; i < ToolDefs.Length; i++)
        {
            var (tool, type, icon, key) = ToolDefs[i];
            var r = ToolButtonRect(tool);
            bool selected = _tool == tool;
            bool hover = _hoverTooltip == i;
            BevelPanel(r, selected ? new Color(46, 50, 68) : ChunkFill,
                selected ? TextGold : new Color(72, 78, 96), new Color(8, 8, 12));
            if (selected) DrawCursor(r.X - 22, r.Y + 11, 2);
            _spriteBatch.Draw(_art.Atlas, new Rectangle(r.X + 3, r.Y + 3, 32, 32), _art.Src(icon), Color.White);
            string name = Structures.Display(type);
            _font.Draw(_spriteBatch, $"{key} {name}", r.X + 42, r.Y + 5, selected ? Color.White : (hover ? Color.White : TextDim));
            string sub = type != StructureType.None ? ShortMoney(Structures.Get(type).Cost) : "50% BACK";
            _font.Draw(_spriteBatch, sub, r.X + 42, r.Y + 21, selected ? TextGold : TextDim);
        }

        int iy = ToolButtonRect(Tool.Raze).Bottom + 14;
        DrawToolInfo(x + 6, iy);
    }

    private void DrawToolInfo(int x, int y)
    {
        if (_tool == Tool.Raze)
        {
            _font.Draw(_spriteBatch, "BULLDOZE", x, y, Color.White); y += 18;
            _font.Draw(_spriteBatch, "REFUND 50%", x, y, TextOrange); y += 18;
            _font.Draw(_spriteBatch, "RCLICK = 1 TILE", x, y, TextDim);
            return;
        }
        var def = Structures.Get(ToolDefs[(int)_tool].type);
        _font.Draw(_spriteBatch, def.Name, x, y, Color.White); y += 18;
        _font.Draw(_spriteBatch, $"UPKEEP {def.Upkeep:F1}", x, y, TextDim); y += 17;
        if (def.PowerDraw > 0f) { _font.Draw(_spriteBatch, $"POWER DRAW {def.PowerDraw:F1}", x, y, TextCyan); y += 17; }
        if (def.PowerOutput > 0f) { _font.Draw(_spriteBatch, $"POWER OUT {def.PowerOutput:F0}", x, y, TextGreen); y += 17; }
        if (def.Compute > 0f)
        {
            _font.Draw(_spriteBatch, $"SERVES {def.Compute:F0}", x, y, TextGreen); y += 17;
            _font.Draw(_spriteBatch, $"ANGER {def.NimbyLoad:F1}", x, y, TextOrange); y += 17;
        }
        if (def.ComfortAdd > 0f) { _font.Draw(_spriteBatch, $"COMFORT +{def.ComfortAdd:F0}", x, y, TextGreen); y += 17; }
        if (def.CoolingReach > 0) { _font.Draw(_spriteBatch, "COOLS NEARBY 55%", x, y, TextCyan); y += 17; }
        if (def.PassiveIncome > 0f) { _font.Draw(_spriteBatch, $"INCOME {Money(def.PassiveIncome)}/D", x, y, TextGreen); y += 17; }
    }

    private void DrawBottomBar(Point v)
    {
        FillRect(new Rectangle(0, MapH, MapW, TargetHeight - MapH), BgColor);
        FillRect(new Rectangle(0, MapH, MapW, 3), PanelLine);
        FillRect(new Rectangle(0, MapH + 3, MapW, 1), new Color(8, 8, 12));

        var map = _sim.Map;
        int towns = map.Towns.Count;
        int chipGap = 12;
        int chipW = Math.Max(360, (MapW - chipGap * (towns + 1)) / Math.Max(1, towns));
        int chipH = TargetHeight - MapH - (chipGap + 10);
        for (int i = 0; i < towns; i++)
        {
            var t = map.Towns[i];
            int cx = chipGap + i * (chipW + chipGap);
            int cy = MapH + 10;
            var chip = new Rectangle(cx, cy, chipW, chipH);
            BevelPanel(chip, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
            FillRect(new Rectangle(chip.X + 4, chip.Y + 4, chip.Width - 8, chip.Height - 8), ChunkFill);
            FillRect(new Rectangle(chip.X + 4, chip.Y + 4, 4, chip.Height - 8), TownTints[i % TownTints.Length]);

            float dem = _sim.DemandForTown(t.Id);
            float served = Math.Min(dem, _sim.TownCompute(t.Id));
            float ratio = _sim.TownLoadRatio(t.Id);

            _font.Draw(_spriteBatch, TownName(t.Id), cx + 16, cy + 8, Color.White);
            string srv = $"SRV {served:F0}/{dem:F0}";
            _font.Draw(_spriteBatch, srv, cx + chipW - 260, cy + 8, served >= dem - 0.5f ? TextGreen : TextOrange);
            string mood = MoodWord(ratio);
            _font.Draw(_spriteBatch, mood, cx + chipW - 120, cy + 8, MoodColor(ratio));
            DrawMoodGlyph(new Rectangle(cx + chipW - 26, cy + 8, 10, 10), ratio <= 0.85f);

            _font.Draw(_spriteBatch, $"CROWDING {ratio * 100f:F0}%", cx + 16, cy + 34, TextDim);
            var bar = new Rectangle(cx + chipW - 260, cy + 38, 220, 8);
            DrawBar(bar, Math.Min(1f, ratio / 1.3f), MoodColor(ratio));
            int comfortMark = bar.X + (int)(bar.Width / 1.3f);
            FillRect(new Rectangle(comfortMark, bar.Y - 2, 2, bar.Height + 4), Color.White);
            float relief = _sim.TownRelief(t.Id);
            if (relief > 0.001f)
                _font.Draw(_spriteBatch, $"FOREST -{relief * 100f:F0}%", cx + chipW - 120, cy + 34, TextGreen);

            _font.Draw(_spriteBatch, $"COMFORT {_sim.TownComfort(t.Id):F0}", cx + 16, cy + 58, TextDim);
            _font.Draw(_spriteBatch, $"ANGER {_sim.TownNimby(t.Id):F1}", cx + chipW - 260, cy + 58, ratio > 1f ? TextRed : TextDim);
            int parks = (int)(_sim.TownParkComfort(t.Id) / Structures.Park.ComfortAdd + 0.5f);
            if (parks > 0) _font.Draw(_spriteBatch, $"PARKS {parks}", cx + chipW - 120, cy + 58, TextGreen);
        }
    }

    private void DrawToasts()
    {
        int ty = MapH - 30;
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

    private void DrawTicker()
    {
        if (_clock >= _tickerUntil || _tickerText.Length == 0) return;
        int w = _font.Width(_tickerText) + 24;
        var r = new Rectangle((MapW - w) / 2, MapH + 6, w, 30);
        FillRect(r, Color.Black * 0.75f);
        OutlineRect(r, TextGold);
        _font.Draw(_spriteBatch, _tickerText, r.X + 12, r.Y + 9, TextGold);
    }

    private void DrawEventBanner()
    {
        if (_sim.PendingChoice != null) { DrawChoiceDialog(_sim.PendingChoice); return; }
        if (_sim.LastEventDay < 0 || _sim.LastEventDay != _sim.Day) return;
        if (_clock >= _tickerUntil + 4.5f) return;

        var ev = _sim.HasModifiers ? _sim : _sim;
        string headline = _sim.LastEventHeadline;
        string blurb = _sim.LastEventBlurb;
        Color accent = _sim.LastEventGood ? TextGreen : TextOrange;

        int w = 900;
        var r = new Rectangle((MapW - w) / 2, 16, w, 74);
        FillRect(r, new Color(10, 10, 16) * 0.92f);
        OutlineRect(r, accent);
        FillRect(new Rectangle(r.X, r.Y, 4, r.Height), accent);
        _font.Draw(_spriteBatch, _sim.LastEventGood ? "EVENT" : "EVENT", r.X + 16, r.Y + 12, accent, 2);
        _font.Draw(_spriteBatch, headline, r.X + 110, r.Y + 12, Color.White, 2);
        _font.Draw(_spriteBatch, blurb, r.X + 16, r.Y + 42, TextDim);
    }

    private void DrawChoiceDialog(GameEvent ev)
    {
        FillRect(new Rectangle(0, 0, TargetWidth, TargetHeight), Color.Black * 0.78f);
        int w = 980, h = 330;
        var panel = new Rectangle((TargetWidth - w) / 2, (TargetHeight - h) / 2, w, h);
        FillRect(panel, PanelColor);
        OutlineRect(panel, TextGold);

        int x = panel.X + 28, y = panel.Y + 24;
        _font.Draw(_spriteBatch, "DECISION", x, y, TextGold, 3); y += 46;
        _font.Draw(_spriteBatch, ev.Headline, x, y, Color.White, 2); y += 34;
        _font.Draw(_spriteBatch, ev.Blurb, x, y, TextDim); y += 48;

        _font.Draw(_spriteBatch, $"1) {ev.ChoiceA!.Value.label}", x, y, TextCyan, 2);
        _font.Draw(_spriteBatch, $"2) {ev.ChoiceB!.Value.label}", x, y + 44, TextCyan, 2); y += 120;
        _font.Draw(_spriteBatch, "PRESS 1 OR 2 TO CHOOSE", x, y, TextGold);
    }

    private void DrawActiveModifiers(int x, int y)
    {
        if (!_sim.HasModifiers) return;
        _font.Draw(_spriteBatch, "ACTIVE EVENTS", x, y, TextOrange); y += 18;
        if (Math.Abs(_sim.EvDemandMult - 1f) > 0.0001f)
        { _font.Draw(_spriteBatch, $"DEMAND x{_sim.EvDemandMult:F2}", x, y, _sim.EvDemandMult >= 1f ? TextGreen : TextRed); y += 16; }
        if (Math.Abs(_sim.EvRevenueMult - 1f) > 0.0001f)
        { _font.Draw(_spriteBatch, $"REVENUE x{_sim.EvRevenueMult:F2}", x, y, _sim.EvRevenueMult >= 1f ? TextGreen : TextRed); y += 16; }
        if (Math.Abs(_sim.EvUpkeepMult - 1f) > 0.0001f)
        { _font.Draw(_spriteBatch, $"UPKEEP x{_sim.EvUpkeepMult:F2}", x, y, _sim.EvUpkeepMult <= 1f ? TextGreen : TextRed); y += 16; }
        if (Math.Abs(_sim.EvBuildCostMult - 1f) > 0.0001f)
        { _font.Draw(_spriteBatch, $"BUILD COST x{_sim.EvBuildCostMult:F2}", x, y, _sim.EvBuildCostMult <= 1f ? TextGreen : TextRed); y += 16; }
        if (Math.Abs(_sim.EvNimbyMult - 1f) > 0.0001f)
        { _font.Draw(_spriteBatch, $"ANGER x{_sim.EvNimbyMult:F2}", x, y, _sim.EvNimbyMult <= 1f ? TextGreen : TextRed); y += 16; }
    }

    private static readonly string[] Tooltips =
    {
        "SERVER SHED: cheap, serves 1. Best early ROI.",
        "DATA HALL: serves 3, 1.6 compute per anger.",
        "HYPERSCALE: serves 7, densest compute. Runs hot.",
        "PARK: +3 comfort to its town. Uses a tile.",
        "COOLING TOWER: cools nearby tiles to 55% upkeep.",
        "POWER PLANT: supplies 22 power for your grid.",
        "E-WASTE RECYCLER: $6/day passive, mild anger.",
        "BULLDOZE: 50% refund. Right-click a single tile.",
    };

    private void DrawTooltip(Point v)
    {
        if (_hoverTooltip < 0) return;
        string text;
        if (_hoverTooltip == 100) text = "HAPPINESS: below 35 the towns revolt.";
        else if (_hoverTooltip == 101) text = "Per-town readout: served, crowding, comfort, anger.";
        else text = Tooltips[_hoverTooltip];

        var size = _font.Width(text);
        int tx = Math.Min(v.X + 16, MapW - size - 8);
        int ty = Math.Min(v.Y + 16, MapH - 30);
        var r = new Rectangle(tx - 6, ty - 6, size + 12, _font.Height() + 12);
        FillRect(r, new Color(8, 8, 12) * 0.92f);
        OutlineRect(r, PanelLine);
        _font.Draw(_spriteBatch, text, tx, ty, Color.White);
    }

    private static string MoodWord(float ratio)
        => ratio <= 0.5f ? "CONTENT" : (ratio <= 0.85f ? "UNEASY" : (ratio <= 1.05f ? "TENSE" : "FURIOUS"));

    private Color MoodColor(float ratio)
        => ratio <= 0.5f ? TextGreen : (ratio <= 0.85f ? TextGold : (ratio <= 1.05f ? TextOrange : TextRed));

    private void DrawHelp()
    {
        FillRect(new Rectangle(0, 0, TargetWidth, TargetHeight), Color.Black * 0.78f);
        int w = 1000, h = 700;
        var panel = new Rectangle((TargetWidth - w) / 2, (TargetHeight - h) / 2, w, h);
        FillRect(panel, PanelColor);
        OutlineRect(panel, TextGold);

        int x = panel.X + 28;
        int y = panel.Y + 22;
        _font.Draw(_spriteBatch, "HOW TO PLAY", x, y, TextGold, 3); y += 44;

        string[] lines =
        {
            "TOWNS EMIT COMPUTE DEMAND THAT GROWS EVERY DAY.",
            "ONLY BUILDINGS INSIDE A TOWN'S TINTED CATCHMENT SERVE IT.",
            "",
            "SHEDS ARE CHEAP CASH FLOW. HALLS AND HYPERSCALE SERVE MORE",
            "DEMAND PER TILE AND PER POINT OF ANGER - BUT RUN HOT.",
            "BLUE RIVERFRONT TILES COOL UPKEEP TO 55%. COOLING TOWERS",
            "EXTEND THAT COOLING INLAND.",
            "",
            "CROWDING A TOWN PAST ITS COMFORT MAKES IT FURIOUS - ANGER",
            "GROWS FASTER THE FURTHER PAST COMFORT YOU PACK.",
            "PARKS RAISE A TOWN'S COMFORT. FORESTS LEFT STANDING CALM",
            "TOWNS TOO - CLEARING THEM COSTS 2.5X AND REMOVES THE CALM.",
            "",
            "POWER PLANTS SUPPLY POWER; HYPERSCALE AND EQUIPMENT DRAW IT.",
            "RUN SHORT AND MORALE TANKS. E-WASTE RECYCLERS EARN PASSIVE CASH.",
            "UNSERVED DEMAND (BROWNOUTS) ALSO ANGERS EVERYONE.",
            "YOU CANNOT SERVE IT ALL FOREVER. CHOOSE WHAT TO SACRIFICE.",
            "",
            "BUILD OVER A CHEAPER BUILDING TO UPGRADE IT IN PLACE.",
            "PRESS Z TO UNDO YOUR LAST BUILD OR RAZE.",
            "REACH THE COMPANY VALUE GOAL BEFORE THE TOWNS REVOLT",
            "OR THE MONEY RUNS OUT.",
        };
        foreach (var line in lines)
        {
            _font.Draw(_spriteBatch, line, x, y, line.Length == 0 ? TextDim : Color.White);
            y += 24;
        }
        y += 10;
        _font.Draw(_spriteBatch, "PRESS H TO CLOSE", x, y, TextGold);
    }

    private void DrawEndScreen()
    {
        FillRect(new Rectangle(0, 0, TargetWidth, TargetHeight), Color.Black * 0.78f);
        bool won = _sim.State == RunState.Won;
        string title = won ? "YOU WIN" : "GAME OVER";
        Color titleColor = won ? TextGold : TextRed;
        int scale = 8;
        int tw = _font.Width(title, scale);
        _font.Draw(_spriteBatch, title, (TargetWidth - tw) / 2, 120, titleColor, scale);

        if (Blink && _sim.State == RunState.Won)
            DrawCentered("CONGRATULATIONS", 210, TextGold);
        DrawCentered(_sim.EndReason, 250, Color.White);

        int pw = 620, ph = 250;
        var card = new Rectangle((TargetWidth - pw) / 2 - 330, 300, pw, ph);
        BevelPanel(card, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        FillRect(new Rectangle(card.X + 4, card.Y + 4, card.Width - 8, 18), new Color(38, 42, 56));
        _font.Draw(_spriteBatch, "FINAL TALLY", card.X + 12, card.Y + 7, TextCyan);
        int cx = card.X + 20, cy = card.Y + 34;

        var rows = new (string, string)[]
        {
            ("DIFFICULTY", DifficultyName(_sim.Difficulty)),
            ("DAYS SURVIVED", $"{_sim.Day}"),
            ("FINAL VALUE", Money(_sim.NetWorth)),
            ("PEAK VALUE", $"{Money(_sim.PeakValue)} (DAY {_sim.PeakDay})"),
            ("STRUCTURES", $"{_sim.StructureTiles}  (PEAK {_sim.PeakStructures})"),
            ("BEST SERVED", $"{_sim.PeakDemandServed}"),
            ("FINAL HAPPINESS", $"{_sim.Happiness:F0}"),
        };
        foreach (var (label, value) in rows)
        {
            _font.Draw(_spriteBatch, label, cx, cy, TextDim);
            _font.Draw(_spriteBatch, value, cx + 300, cy, Color.White);
            cy += 24;
        }

        DrawHighScoreTable(new Rectangle((TargetWidth - pw) / 2 + 330, 300, pw, ph));

        DrawSaveIndicator();
        DrawCentered("R RETRY THIS REGION      N NEW REGION      ESC MENU", TargetHeight - 110, TextGold);
    }

    private void DrawHighScoreTable(Rectangle panel)
    {
        BevelPanel(panel, new Color(22, 24, 32), PanelLine, new Color(8, 8, 12));
        FillRect(new Rectangle(panel.X + 4, panel.Y + 4, panel.Width - 8, 18), new Color(38, 42, 56));
        _font.Draw(_spriteBatch, "TOP OPERATORS", panel.X + 12, panel.Y + 7, TextCyan);

        var scores = HighScores.Load();
        int cy = panel.Y + 36;
        bool isTop = !string.IsNullOrEmpty(_lastScoreOutcome);
        int rank = 1;
        for (int i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            bool mine = isTop && rank == _lastScoreRank;
            Color c = mine ? TextGold : TextDim;
            _font.Draw(_spriteBatch, $"{i + 1}.", panel.X + 12, cy, c);
            _font.Draw(_spriteBatch, ShortMoney(s.Value), panel.X + 44, cy, c);
            _font.Draw(_spriteBatch, $"{s.Difficulty,-8}", panel.X + 150, cy, c);
            _font.Draw(_spriteBatch, s.Outcome, panel.X + 302, cy, c);
            cy += 22;
            rank++;
        }
        if (scores.Count == 0)
            _font.Draw(_spriteBatch, "NO RUNS RECORDED", panel.X + 12, cy, TextDim);
    }

    private void DrawSaveIndicator()
    {
        if (!Blink) return;
        string text = "SAVED";
        int w = _font.Width(text) + 20;
        var r = new Rectangle(TargetWidth - w - 24, 24, w, 28);
        FillRect(r, new Color(10, 10, 16) * 0.85f);
        OutlineRect(r, TextGreen);
        FillRect(new Rectangle(r.X + 6, r.Y + 10, 8, 8), TextGreen);
        _font.Draw(_spriteBatch, text, r.X + 20, r.Y + 9, TextGreen);
    }

    private void DrawCentered(string text, int y, Color color)
        => _font.Draw(_spriteBatch, text, (TargetWidth - _font.Width(text)) / 2, y, color);

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

    private void BevelPanel(Rectangle r, Color fill, Color light, Color dark)
    {
        FillRect(r, fill);
        FillRect(new Rectangle(r.X, r.Y, r.Width, 2), light);
        FillRect(new Rectangle(r.X, r.Y, 2, r.Height), light);
        FillRect(new Rectangle(r.X, r.Y + r.Height - 2, r.Width, 2), dark);
        FillRect(new Rectangle(r.X + r.Width - 2, r.Y, 2, r.Height), dark);
    }

    private bool Blink => (int)(_animTime * 3f) % 2 == 0;

    private void DrawCursor(int x, int y, int scale = 2)
    {
        if (!Blink) return;
        _font.Draw(_spriteBatch, ">", x, y, TextGold, scale);
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
        catch { return false; }
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(SettingsFileName, _soundOn ? "sound=1" : "sound=0"); } catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFileName)) return;
            _soundOn = File.ReadAllText(SettingsFileName).Contains("sound=1");
        }
        catch { }
    }
}
