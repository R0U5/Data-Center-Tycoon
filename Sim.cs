using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DataCenterTycoon;

public enum RunState { Playing, Won, Lost }

public class Sim
{
    public const float START_MONEY = 700f;
    public const float WIN_CASH_GOAL = 16000f;
    public const float REFUND_FRACTION = 0.5f;
    public const float COOLED_OP_FACTOR = 0.55f;
    public const float REVENUE_PER_SERVED = 2.9f;
    public const float FOREST_BUILD_MULT = 2.5f;
    public const float BASE_DEMAND = 100f;
    public const float DAILY_DEMAND_GROWTH = 0.055f;
    public const float NIMBY_WEIGHT = 15f;
    public const float NIMBY_EXP = 2.6f;
    public const float COMFORT_PER_SIZE = 3.85f;
    public const float BROWNOUT_WEIGHT = 50f;
    public const float BUFFER_RELIEF = 0.20f;
    public const float BUFFER_PER_SIZE = 1.0f;
    public const float HAPPINESS_FALL = 0.55f;
    public const float HAPPINESS_RISE = 0.16f;
    public const float HAPPINESS_LOSE = 35f;
    public const float HAPPINESS_WARN = 50f;
    public const int SAVE_VERSION = 4;

    public const float POWER_SHORTFALL_PENALTY = 40f;
    public const float POWER_SHORTFALL_EXP = 0.6f;

    public MapGenerator Map { get; private set; }
    public StructureType[,] Grid { get; private set; }
    public float[,] Paid { get; private set; }
    public bool[,] LocalCooled { get; private set; }

    public float Money { get; private set; }
    public float TotalDemand { get; private set; }
    public float Happiness { get; private set; }
    public int Day { get; private set; }
    public RunState State { get; private set; }
    public string EndReason { get; private set; } = "";
    public float LastRevenue { get; private set; }
    public float LastOpCost { get; private set; }
    public float LastNet { get; private set; }
    public float LastPassive { get; private set; }
    public float LastCoolingSaving { get; private set; }

    public float PeakValue { get; private set; }
    public int PeakDay { get; private set; }
    public int PeakStructures { get; private set; }
    public int PeakDemandServed { get; private set; }

    private float[] _townCompute = Array.Empty<float>();
    private float[] _townLoad = Array.Empty<float>();
    private float[] _townParkComfort = Array.Empty<float>();
    private int[] _townCoveredForest = Array.Empty<int>();
    private int _townSizeDenom = 1;
    private float _upkeepTotal;
    private float _upkeepTotalUncooled;
    private float _assetPaid;
    private int _structureTiles;
    private int _cooledStructureTiles;
    private float _powerProduced;
    private float _powerDemanded;

    public Sim(MapGenerator map)
    {
        Map = map;
        Reset();
    }

    public void Reset()
    {
        Grid = new StructureType[MapGenerator.GridWidth, MapGenerator.GridHeight];
        Paid = new float[MapGenerator.GridWidth, MapGenerator.GridHeight];
        LocalCooled = new bool[MapGenerator.GridWidth, MapGenerator.GridHeight];
        Money = START_MONEY;
        TotalDemand = BASE_DEMAND;
        Happiness = 100f;
        Day = 1;
        State = RunState.Playing;
        EndReason = "";
        LastRevenue = LastOpCost = LastNet = LastPassive = LastCoolingSaving = 0f;
        PeakValue = NetWorth;
        PeakDay = 1;
        PeakStructures = 0;
        PeakDemandServed = 0;
        InitTownState();
        RecalculateAggregates();
    }

    private void InitTownState()
    {
        int n = Map.Towns.Count;
        _townCompute = new float[n];
        _townLoad = new float[n];
        _townParkComfort = new float[n];
        _townCoveredForest = new int[n];
        _townSizeDenom = Math.Max(1, Map.Towns.Sum(t => t.Size));
    }

    public int StructureTiles => _structureTiles;
    public int CooledStructureTiles => _cooledStructureTiles;
    public float NetWorth => Money + _assetPaid * REFUND_FRACTION;
    public float CoolingSaving => _upkeepTotalUncooled - _upkeepTotal;
    public float PowerProduced => _powerProduced;
    public float PowerDemanded => _powerDemanded;
    public float PowerMargin => _powerProduced - _powerDemanded;
    public bool PowerStarved => _powerDemanded > _powerProduced + 0.001f;
    public bool HasPowerGrid => _powerProduced > 0f;
    public bool PowerShortfallActive => HasPowerGrid && PowerStarved;

    public float PowerShortfallFraction
    {
        get
        {
            if (!HasPowerGrid || _powerDemanded <= 0f) return 0f;
            float unmet = _powerDemanded - _powerProduced;
            if (unmet <= 0f) return 0f;
            return Math.Clamp(unmet / _powerDemanded, 0f, 1f);
        }
    }

    public float DemandForTown(int id) => Map.Towns[id].Size / (float)_townSizeDenom * TotalDemand;
    public float TownCompute(int id) => _townCompute[id];
    public float TownParkComfort(int id) => _townParkComfort[id];
    public float TownLoad(int id) => _townLoad[id];
    public float TownComfort(int id) => COMFORT_PER_SIZE * Map.Towns[id].Size + _townParkComfort[id];
    public float TownLoadRatio(int id)
    {
        float c = TownComfort(id);
        return c > 0f ? _townLoad[id] / c : 0f;
    }

    public float TownRelief(int id)
    {
        var t = Map.Towns[id];
        float preserved = t.ForestInCatchment - _townCoveredForest[id];
        if (preserved <= 0f) return 0f;
        return BUFFER_RELIEF * Math.Min(1f, preserved / (BUFFER_PER_SIZE * t.Size));
    }

    public (float demand, float served) DemandServed()
    {
        float d = 0f, s = 0f;
        for (int i = 0; i < Map.Towns.Count; i++)
        {
            float dem = DemandForTown(i);
            d += dem;
            s += Math.Min(dem, _townCompute[i]);
        }
        return (d, s);
    }

    public float OperatingCost() => _upkeepTotal;

    public float NimbyTotal()
    {
        float n = 0f;
        for (int i = 0; i < Map.Towns.Count; i++)
            n += TownNimby(i);
        return n;
    }

    public float TownNimby(int id)
    {
        float comfort = TownComfort(id);
        if (comfort <= 0f) return 0f;
        float crowding = NIMBY_WEIGHT * (float)Math.Pow(_townLoad[id] / comfort, NIMBY_EXP);
        return crowding * (1f - TownRelief(id));
    }

    public float BrownoutFraction()
    {
        var (d, s) = DemandServed();
        return d > 0f ? Math.Clamp(1f - s / d, 0f, 1f) : 0f;
    }

    public float HappinessTarget()
    {
        var (d, s) = DemandServed();
        float brownout = d > 0f ? BROWNOUT_WEIGHT * (1f - s / d) : 0f;
        float powerPenalty = PowerShortfallActive
            ? POWER_SHORTFALL_PENALTY * (float)Math.Pow(PowerShortfallFraction, POWER_SHORTFALL_EXP)
            : 0f;
        return 100f - NimbyTotal() - brownout - powerPenalty;
    }

    public float ProjectedHappiness()
    {
        float target = HappinessTarget();
        float rate = target < Happiness ? HAPPINESS_FALL : HAPPINESS_RISE;
        return Math.Clamp(Happiness + rate * (target - Happiness), 0f, 100f);
    }

    public bool CanPlaceAt(int x, int y, StructureType type)
    {
        if (type == StructureType.None) return false;
        if (x < 0 || x >= MapGenerator.GridWidth || y < 0 || y >= MapGenerator.GridHeight) return false;
        var tile = Map.Tiles[x, y];
        if (tile == TileType.River || tile == TileType.Town) return false;
        var existing = Grid[x, y];
        if (existing == StructureType.None) return true;
        if (existing == type) return false;
        return Structures.Get(type).Cost > Structures.Get(existing).Cost;
    }

    public float TileCostAt(int x, int y, StructureType type)
    {
        float baseCost = Structures.Get(type).Cost;
        if (Map.Tiles[x, y] == TileType.Forest) baseCost *= FOREST_BUILD_MULT;
        float credit = Grid[x, y] != StructureType.None ? Paid[x, y] * REFUND_FRACTION : 0f;
        return Math.Max(0f, baseCost - credit);
    }

    public (int tiles, float cost) RegionPlan(int x0, int y0, int x1, int y1, StructureType type)
    {
        int tiles = 0;
        float cost = 0f;
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                if (CanPlaceAt(x, y, type))
                {
                    tiles++;
                    cost += TileCostAt(x, y, type);
                }
        return (tiles, cost);
    }

    public int PlaceRegion(int x0, int y0, int x1, int y1, StructureType type)
    {
        var tiles = new List<(int x, int y)>();
        float cost = 0f;
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                if (CanPlaceAt(x, y, type))
                {
                    tiles.Add((x, y));
                    cost += TileCostAt(x, y, type);
                }
        if (tiles.Count == 0 || Money < cost) return 0;

        float defCost = Structures.Get(type).Cost;
        foreach (var (x, y) in tiles)
        {
            float paid = defCost;
            if (Map.Tiles[x, y] == TileType.Forest) paid *= FOREST_BUILD_MULT;
            Money -= TileCostAt(x, y, type);
            Grid[x, y] = type;
            Paid[x, y] = paid;
        }
        RecalculateAggregates();
        CheckWin();
        return tiles.Count;
    }

    public bool TryPlace(int x, int y, StructureType type)
        => PlaceRegion(x, y, x, y, type) > 0;

    public bool DemolishAt(int x, int y)
    {
        if (x < 0 || x >= MapGenerator.GridWidth || y < 0 || y >= MapGenerator.GridHeight) return false;
        if (Grid[x, y] == StructureType.None) return false;
        Money += Paid[x, y] * REFUND_FRACTION;
        Grid[x, y] = StructureType.None;
        Paid[x, y] = 0f;
        RecalculateAggregates();
        return true;
    }

    public int DemolishRegion(int x0, int y0, int x1, int y1)
    {
        int removed = 0;
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                if (x >= 0 && x < MapGenerator.GridWidth && y >= 0 && y < MapGenerator.GridHeight
                    && Grid[x, y] != StructureType.None)
                {
                    Money += Paid[x, y] * REFUND_FRACTION;
                    Grid[x, y] = StructureType.None;
                    Paid[x, y] = 0f;
                    removed++;
                }
        if (removed > 0) RecalculateAggregates();
        return removed;
    }

    private void CheckWin()
    {
        if (State != RunState.Playing) return;
        if (NetWorth >= WIN_CASH_GOAL)
        {
            State = RunState.Won;
            EndReason = $"YOU BUILT A ${WIN_CASH_GOAL:N0} COMPUTE EMPIRE";
        }
    }

    public void AdvanceDay()
    {
        if (State != RunState.Playing) return;
        var (_, served) = DemandServed();
        LastRevenue = REVENUE_PER_SERVED * served;
        LastOpCost = _upkeepTotal;
        LastPassive = _passiveIncome;
        LastCoolingSaving = CoolingSaving;
        LastNet = LastRevenue - LastOpCost + LastPassive;
        Money += LastNet;

        Happiness = ProjectedHappiness();

        TotalDemand *= 1f + DAILY_DEMAND_GROWTH;
        Day++;

        if (NetWorth > PeakValue)
        {
            PeakValue = NetWorth;
            PeakDay = Day;
        }
        if (_structureTiles > PeakStructures) PeakStructures = _structureTiles;
        if (served > PeakDemandServed) PeakDemandServed = (int)served;

        if (Happiness < HAPPINESS_LOSE)
        {
            State = RunState.Lost;
            EndReason = "THE TOWNS REVOLTED AGAINST YOUR SERVERS";
        }
        else if (Money < 0f)
        {
            State = RunState.Lost;
            EndReason = "YOU WENT BANKRUPT";
        }
        else
        {
            CheckWin();
        }
    }

    private float _passiveIncome;

    private void RecalculateAggregates()
    {
        Array.Clear(_townCompute, 0, _townCompute.Length);
        Array.Clear(_townLoad, 0, _townLoad.Length);
        Array.Clear(_townParkComfort, 0, _townParkComfort.Length);
        Array.Clear(_townCoveredForest, 0, _townCoveredForest.Length);
        _upkeepTotal = 0f;
        _upkeepTotalUncooled = 0f;
        _assetPaid = 0f;
        _structureTiles = 0;
        _cooledStructureTiles = 0;
        _powerProduced = 0f;
        _powerDemanded = 0f;
        _passiveIncome = 0f;

        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                LocalCooled[x, y] = false;

        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var s = Grid[x, y];
                if (s == StructureType.None) continue;
                var def = Structures.Get(s);
                if (def.Cools)
                {
                    int r = def.CoolingReach;
                    for (int dx = -r; dx <= r; dx++)
                        for (int dy = -r; dy <= r; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= MapGenerator.GridWidth || ny < 0 || ny >= MapGenerator.GridHeight) continue;
                            LocalCooled[nx, ny] = true;
                        }
                }
            }

        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                var s = Grid[x, y];
                if (s == StructureType.None) continue;
                var def = Structures.Get(s);
                _structureTiles++;
                _assetPaid += Paid[x, y];
                bool cooled = Map.Cooled[x, y] || LocalCooled[x, y];
                if (cooled) _cooledStructureTiles++;
                _upkeepTotal += def.Upkeep * (cooled ? COOLED_OP_FACTOR : 1f);
                _upkeepTotalUncooled += def.Upkeep;
                _powerProduced += def.PowerOutput;
                _powerDemanded += def.PowerDraw;
                _passiveIncome += def.PassiveIncome;
                int t = Map.CatchmentTown[x, y];
                if (t >= 0 && t < _townCompute.Length)
                {
                    _townCompute[t] += def.Compute;
                    _townLoad[t] += def.NimbyLoad;
                    _townParkComfort[t] += def.ComfortAdd;
                    if (Map.Tiles[x, y] == TileType.Forest) _townCoveredForest[t]++;
                }
            }
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"v{SAVE_VERSION}");
        sb.AppendLine($"seed={Map.Seed}");
        sb.AppendLine($"day={Day}");
        sb.AppendLine(FormattableString.Invariant($"money={Money}"));
        sb.AppendLine(FormattableString.Invariant($"demand={TotalDemand}"));
        sb.AppendLine(FormattableString.Invariant($"happiness={Happiness}"));
        sb.AppendLine($"state={State}");
        sb.AppendLine($"reason={Escape(EndReason)}");
        sb.AppendLine(FormattableString.Invariant($"peak={PeakValue}"));
        sb.AppendLine($"peakday={PeakDay}");
        sb.AppendLine(FormattableString.Invariant($"served={PeakDemandServed}"));
        sb.AppendLine($"structs={PeakStructures}");
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                if (Grid[x, y] != StructureType.None)
                    sb.AppendLine(FormattableString.Invariant($"s={x},{y},{(int)Grid[x, y]},{Paid[x, y]}"));
        return sb.ToString();
    }

    private static string Escape(string s) => (s ?? "").Replace("\n", " ").Replace("\r", " ");

    public static Sim Deserialize(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length == 0) return null;

        if (lines[0] != $"v{SAVE_VERSION}" && lines[0] != "v3")
        {
            if (long.TryParse(lines[0], out var legacySeed))
                return new Sim(new MapGenerator(legacySeed));
            long seed2 = 0;
            foreach (var line in lines)
                if (line.StartsWith("seed=") && long.TryParse(line.Substring(5), out var s2))
                    seed2 = s2;
            return lines[0] == "v2" ? new Sim(new MapGenerator(seed2)) : null;
        }

        long seed = 0;
        int day = 1;
        float money = START_MONEY, demand = BASE_DEMAND, happiness = 100f;
        var state = RunState.Playing;
        string reason = "";
        float peak = -1f;
        int peakDay = 1, servedPeak = 0, structsPeak = 0;
        var tiles = new List<(int x, int y, StructureType s, float paid)>();

        foreach (var line in lines.Skip(1))
        {
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
                case "reason": reason = val; break;
                case "peak": peak = ParseFloat(val, -1f); break;
                case "peakday": int.TryParse(val, out peakDay); break;
                case "served": int.TryParse(val, out servedPeak); break;
                case "structs": int.TryParse(val, out structsPeak); break;
                case "s":
                    var p = val.Split(',');
                    if (p.Length == 4
                        && int.TryParse(p[0], out var tx) && int.TryParse(p[1], out var ty)
                        && int.TryParse(p[2], out var ti)
                        && Enum.IsDefined(typeof(StructureType), ti) && ti != 0
                        && ti <= Structures.All.Length)
                        tiles.Add((tx, ty, (StructureType)ti, ParseFloat(p[3], 0f)));
                    break;
            }
        }

        var sim = new Sim(new MapGenerator(seed));
        foreach (var (x, y, s, paid) in tiles)
        {
            if (x < 0 || x >= MapGenerator.GridWidth || y < 0 || y >= MapGenerator.GridHeight) continue;
            sim.Grid[x, y] = s;
            sim.Paid[x, y] = paid;
        }
        sim.Money = money;
        sim.TotalDemand = demand;
        sim.Happiness = happiness;
        sim.Day = day;
        sim.State = state;
        sim.EndReason = !string.IsNullOrEmpty(reason)
            ? reason
            : state == RunState.Won
                ? $"YOU BUILT A ${WIN_CASH_GOAL:N0} COMPUTE EMPIRE"
                : (state == RunState.Lost ? "YOUR RUN ENDED" : "");
        sim.RecalculateAggregates();
        sim.PeakValue = peak >= 0f ? Math.Max(peak, sim.NetWorth) : sim.NetWorth;
        sim.PeakDay = peakDay;
        sim.PeakDemandServed = servedPeak;
        sim.PeakStructures = structsPeak > 0 ? structsPeak : sim._structureTiles;
        return sim;
    }

    private static float ParseFloat(string s, float fallback)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
