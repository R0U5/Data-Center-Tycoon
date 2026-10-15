using System;
using System.Collections.Generic;
using System.Linq;

namespace DataCenterTycoon;

public static class BalanceHarness
{
    private class Policy
    {
        public string Name = "";
        public bool ChaseDemand;
        public float ComfortTarget = 0.9f;
        public bool UseTiers;
        public bool UseParks;
        public bool UseCooling;
        public bool UsePower;
        public bool UseRecycler;
        public bool Sprawl;
    }

    private static readonly Policy[] Policies =
    {
        new() { Name = "naive sheds (chase demand)", ChaseDemand = true },
        new() { Name = "sheds only, disciplined", ComfortTarget = 0.9f },
        new() { Name = "tiered, no parks", ComfortTarget = 0.9f, UseTiers = true },
        new() { Name = "tiered + parks (skilled)", ComfortTarget = 0.9f, UseTiers = true, UseParks = true },
        new() { Name = "tiered + parks, bold 1.1", ComfortTarget = 1.1f, UseTiers = true, UseParks = true },
        new() { Name = "tiered + parks, overpack 1.3", ComfortTarget = 1.3f, UseTiers = true, UseParks = true },
        new() { Name = "sprawl outside catchments", Sprawl = true },
    };

    private static readonly Policy[] ExtendedPolicies =
    {
        new() { Name = "skilled + cooling + power", ComfortTarget = 0.82f, UseTiers = true, UseParks = true, UseCooling = true, UsePower = true },
        new() { Name = "cooling+power, no parks", ComfortTarget = 0.82f, UseTiers = true, UseCooling = true, UsePower = true },
        new() { Name = "recycler economy", ComfortTarget = 0.9f, UseTiers = true, UseParks = true, UseRecycler = true },
    };

    public static void Run(string[] args)
    {
        int seeds = 16;
        foreach (var a in args)
            if (int.TryParse(a, out var n) && n > 0) seeds = n;

        if (args.Contains("--trace"))
        {
            Trace(Policies[3]);
            return;
        }

        if (args.Contains("--selftest"))
        {
            SelfTest();
            return;
        }

        Console.WriteLine($"DATA TYCOON balance harness - {seeds} real maps per policy, {MaxDays} day cap");
        Console.WriteLine($"goal ${Sim.WIN_CASH_GOAL:F0}  growth {Sim.DAILY_DEMAND_GROWTH:P1}/day  nimby {Sim.NIMBY_WEIGHT}x^{Sim.NIMBY_EXP}  comfort {Sim.COMFORT_PER_SIZE}/size");
        Console.WriteLine();

        Console.WriteLine("== VANILLA LADDER (four-structure regression baseline) ==");
        foreach (var policy in Policies)
            Report(seeds, policy);

        Console.WriteLine();
        Console.WriteLine("== EXTENDED SYSTEMS (power / cooling / recycler) ==");
        foreach (var policy in ExtendedPolicies)
            Report(seeds, policy);

        Console.WriteLine();
        Console.WriteLine("== SATIRICAL EVENTS ON (opt-in 'news cycle' mode) ==");
        foreach (var policy in new[] { Policies[3], ExtendedPolicies[0] })
            Report(seeds, policy, events: true);

        Console.WriteLine();
        Console.WriteLine("== DIFFICULTY SETTINGS (skilled policy, events off) ==");
        foreach (var d in Enum.GetValues<Difficulty>())
            Report(seeds, Policies[3], difficulty: d);

        Console.WriteLine();
        Console.WriteLine("healthy: naive REVOLTS, sheds-only never WINS, skilled WINS on ~all seeds,");
        Console.WriteLine("         overpack mostly REVOLTS, sprawl goes BANKRUPT.");
    }

    private static void Report(int seeds, Policy policy, bool events = false, Difficulty difficulty = Difficulty.Standard)
    {
        var outcomes = new List<(string outcome, int day, float peak, float happy)>();
        for (int s = 0; s < seeds; s++)
            outcomes.Add(RunOne(new MapGenerator(1000L + s * 7919L), policy, events, difficulty));

        int wins = outcomes.Count(o => o.outcome == "WIN");
        int revolts = outcomes.Count(o => o.outcome == "REVOLT");
        int bankrupts = outcomes.Count(o => o.outcome == "BANKRUPT");
        int timeouts = outcomes.Count(o => o.outcome == "TIMEOUT");
        var winDays = outcomes.Where(o => o.outcome == "WIN").Select(o => o.day).OrderBy(d => d).ToList();
        string med = winDays.Count > 0 ? winDays[winDays.Count / 2].ToString() : "-";
        var endDays = outcomes.Where(o => o.outcome is "REVOLT" or "BANKRUPT").Select(o => o.day).OrderBy(d => d).ToList();
        string medEnd = endDays.Count > 0 ? endDays[endDays.Count / 2].ToString() : "-";
        var peaks = outcomes.Select(o => o.peak).OrderBy(p => p).ToList();
        float medPeak = peaks[peaks.Count / 2];

        Console.WriteLine($"  {policy.Name,-32} WIN {wins,2}/{seeds}  REVOLT {revolts,2}  BANKRUPT {bankrupts,2}  TIMEOUT {timeouts,2}   median win day {med,3}  median fail day {medEnd,3}  median peak value ${medPeak,7:F0}");
    }

    private const int MaxDays = 120;

    public static void PlayDays(Sim sim, int days)
    {
        var towns = BuildTownPlans(sim.Map);
        var policy = Policies[3];
        for (int i = 0; i < days && sim.State == RunState.Playing; i++)
        {
            StepManager(sim, towns, policy);
            sim.AdvanceDay();
        }
    }

    private static void SelfTest()
    {
        int failures = 0;
        void Check(bool ok, string name)
        {
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}");
            if (!ok) failures++;
        }

        var sim = new Sim(new MapGenerator(42));
        int forestX = -1, forestY = -1, grassX = -1, grassY = -1;
        for (int x = 0; x < MapGenerator.GridWidth && (forestX < 0 || grassX < 0); x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                if (forestX < 0 && sim.Map.Tiles[x, y] == TileType.Forest) { forestX = x; forestY = y; }
                if (grassX < 0 && sim.Map.Tiles[x, y] == TileType.Empty && sim.Map.CatchmentTown[x, y] >= 0) { grassX = x; grassY = y; }
            }

        Check(sim.TryPlace(grassX, grassY, StructureType.Shed), "place shed on grass");
        Check(Math.Abs(sim.TileCostAt(forestX, forestY, StructureType.Shed) - Structures.Shed.Cost * Sim.FOREST_BUILD_MULT) < 0.01f, "forest costs 2.5x");
        Check(sim.TryPlace(forestX, forestY, StructureType.Park), "place park on forest");
        Check(sim.CanPlaceAt(grassX, grassY, StructureType.Hall), "can upgrade shed to hall");
        Check(!sim.CanPlaceAt(grassX, grassY, StructureType.Shed), "cannot rebuild same type");
        float cashBefore = sim.Money;
        Check(sim.TryPlace(grassX, grassY, StructureType.Hall)
              && Math.Abs(cashBefore - sim.Money - (Structures.Hall.Cost - Structures.Shed.Cost * Sim.REFUND_FRACTION)) < 0.01f,
            "upgrade credits 50% of old");
        sim.AdvanceDay();
        sim.AdvanceDay();

        var restored = Sim.Deserialize(sim.Serialize());
        bool same = restored != null
            && restored.Day == sim.Day
            && Math.Abs(restored.Money - sim.Money) < 0.01f
            && Math.Abs(restored.TotalDemand - sim.TotalDemand) < 0.01f
            && Math.Abs(restored.Happiness - sim.Happiness) < 0.01f
            && Math.Abs(restored.NetWorth - sim.NetWorth) < 0.01f
            && restored.Map.Seed == sim.Map.Seed;
        if (same)
            for (int x = 0; x < MapGenerator.GridWidth; x++)
                for (int y = 0; y < MapGenerator.GridHeight; y++)
                    if (restored.Grid[x, y] != sim.Grid[x, y]) same = false;
        Check(same, "save/load round trip");

        var legacy = Sim.Deserialize("12345\n");
        Check(legacy != null && legacy.Map.Seed == 12345 && legacy.Day == 1, "legacy seed save starts fresh");
        var v2 = Sim.Deserialize("v2\nseed=777\nday=9\nmoney=100\n");
        Check(v2 != null && v2.Map.Seed == 777 && v2.Day == 1, "v2 save keeps map, restarts run");
        var v3 = Sim.Deserialize("v3\nseed=555\nday=9\nmoney=100\n");
        Check(v3 != null && v3.Map.Seed == 555 && v3.Day == 9, "v3 save upgrades to v4");

        bool demolished = sim.DemolishAt(grassX, grassY);
        Check(demolished && sim.Grid[grassX, grassY] == StructureType.None, "demolish clears tile");

        var win = new Sim(new MapGenerator(7)) { };
        win.TryPlace(win.Map.Towns.Count > 0 ? 0 : 0, 0, StructureType.Shed);
        var winSim = new Sim(new MapGenerator(7));
        int wx = -1, wy = -1;
        for (int x = 0; x < MapGenerator.GridWidth && wx < 0; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                if (winSim.Map.Tiles[x, y] == TileType.Empty && winSim.Map.CatchmentTown[x, y] >= 0) { wx = x; wy = y; break; }
        winSim.PlaceRegion(0, 0, MapGenerator.GridWidth - 1, MapGenerator.GridHeight - 1, StructureType.Campus);
        Check(winSim.State == RunState.Won || winSim.Money >= 0, "mass build does not crash");

        Check(new Sim(new MapGenerator(11)).LocalCooled != null, "local cooling grid allocated");
        Check(Structures.Get(StructureType.Recycler).GeneratesValue, "recycler generates passive income");
        Check(Structures.Get(StructureType.PowerPlant).Powers, "power plant produces power");
        Check(Structures.Get(StructureType.Campus).ConsumesPower, "campus consumes power");

        Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static void Trace(Policy policy)
    {
        var map = new MapGenerator(1000L);
        var sim = new Sim(map);
        var towns = BuildTownPlans(map);
        Console.WriteLine($"trace: {policy.Name}");
        while (sim.State == RunState.Playing && sim.Day <= MaxDays)
        {
            StepManager(sim, towns, policy);
            var counts = new int[Structures.All.Length + 1];
            for (int x = 0; x < MapGenerator.GridWidth; x++)
                for (int y = 0; y < MapGenerator.GridHeight; y++)
                    counts[(int)sim.Grid[x, y]]++;
            var (d, s) = sim.DemandServed();
            Console.WriteLine($"d{sim.Day,3} money {sim.Money,7:F0} worth {sim.NetWorth,7:F0} happy {sim.Happiness,5:F1} dem {d,5:F0} srv {s,5:F0} shed {counts[1],3} hall {counts[2],3} camp {counts[3],3} park {counts[4],3} pwr {sim.PowerProduced,4:F0}/{sim.PowerDemanded,4:F0} ratios {string.Join(" ", Enumerable.Range(0, map.Towns.Count).Select(i => sim.TownLoadRatio(i).ToString("F2")))}");
            sim.AdvanceDay();
        }
        Console.WriteLine($"end: {sim.State} {sim.EndReason}");
    }

    private static (string, int, float, float) RunOne(MapGenerator map, Policy policy, bool events = false, Difficulty difficulty = Difficulty.Standard)
    {
        var sim = new Sim(map) { EventsEnabled = events };
        sim.Configure(difficulty, events);
        var towns = BuildTownPlans(map);
        float peak = sim.Money;

        while (sim.State == RunState.Playing && sim.Day <= MaxDays)
        {
            if (policy.Sprawl) StepSprawl(sim, map);
            else StepManager(sim, towns, policy);
            sim.AdvanceDay();
            peak = Math.Max(peak, sim.NetWorth);
        }

        string outcome = sim.State switch
        {
            RunState.Won => "WIN",
            RunState.Lost => sim.EndReason.Contains("BANKRUPT") ? "BANKRUPT" : "REVOLT",
            _ => "TIMEOUT"
        };
        return (outcome, sim.Day, peak, sim.Happiness);
    }

    private class TownPlan
    {
        public int Id;
        public int Size;
        public List<(int x, int y)> DcOrder = new();
        public List<(int x, int y)> ParkOrder = new();
        public List<(int x, int y)> ForestCells = new();
        public List<(int x, int y)> CoolOrder = new();
    }

    private static List<TownPlan> BuildTownPlans(MapGenerator map)
    {
        var plans = map.Towns.Select(t => new TownPlan { Id = t.Id, Size = t.Size }).ToList();
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                int t = map.CatchmentTown[x, y];
                if (t < 0) continue;
                if (map.Tiles[x, y] == TileType.Empty) plans[t].DcOrder.Add((x, y));
                else if (map.Tiles[x, y] == TileType.Forest) plans[t].ForestCells.Add((x, y));
            }
        foreach (var p in plans)
        {
            p.DcOrder = p.DcOrder.OrderByDescending(c => map.Cooled[c.x, c.y]).ToList();
            p.ParkOrder = Enumerable.Reverse(p.DcOrder).ToList();
            p.CoolOrder = p.DcOrder.Where(c => !map.Cooled[c.x, c.y]).ToList();
        }
        return plans;
    }

    private static void StepManager(Sim sim, List<TownPlan> towns, Policy policy)
    {
        if (policy.UseRecycler) EnsureRecycler(sim, towns);
        if (policy.UsePower) ReservePowerTiles(sim, towns, policy);

        foreach (var plan in towns)
        {
            float demand = sim.DemandForTown(plan.Id);
            float baseComfort = Sim.COMFORT_PER_SIZE * plan.Size;

            if (policy.ChaseDemand)
            {
                int want = (int)Math.Ceiling(demand);
                ApplyTown(sim, plan, Structures.Shed, want, 0, useForest: true);
                continue;
            }

            int parksBuilt = CountParks(sim, plan);
            var tier = Structures.Shed;
            if (policy.UseTiers)
            {
                foreach (var t in Structures.TierLadder)
                {
                    tier = t;
                    float cap = policy.ComfortTarget * (baseComfort + Structures.Park.ComfortAdd * parksBuilt) / t.NimbyLoad * t.Compute;
                    if (cap >= demand) break;
                }
            }

            if (policy.UseCooling && tier == Structures.Campus)
                EnsureCooling(sim, plan, demand);

            int infra = CountInfrastructure(sim, plan);
            int grass = plan.DcOrder.Count - infra;
            int parksWant = parksBuilt;
            if (policy.UseParks && tier == Structures.Campus)
            {
                float capNow = policy.ComfortTarget * (baseComfort + Structures.Park.ComfortAdd * parksBuilt) / tier.NimbyLoad * tier.Compute;
                if (capNow < demand)
                {
                    float comfortNeeded = demand * tier.NimbyLoad / tier.Compute / policy.ComfortTarget - baseComfort;
                    parksWant = Math.Max(parksBuilt, (int)Math.Ceiling(comfortNeeded / Structures.Park.ComfortAdd));
                }
                float tilesPerPark = policy.ComfortTarget * Structures.Park.ComfortAdd / tier.NimbyLoad + 1f;
                int pMax = (int)((grass - policy.ComfortTarget * baseComfort / tier.NimbyLoad) / tilesPerPark);
                parksWant = Math.Clamp(parksWant, parksBuilt, Math.Max(parksBuilt, pMax));
            }

            BuildParks(sim, plan, parksWant);
            int parksNow = CountParks(sim, plan);

            float budget = policy.ComfortTarget * (baseComfort + Structures.Park.ComfortAdd * parksNow);
            float servable = Math.Min(demand, budget / tier.NimbyLoad * tier.Compute);
            int dcWant = (int)Math.Ceiling(servable / tier.Compute);
            dcWant = Math.Min(dcWant, Math.Max(0, grass - parksNow));

            ApplyTown(sim, plan, tier, dcWant, parksNow, useForest: false);
        }

        if (policy.UsePower) EnsurePower(sim, towns);
    }

    private static int CountInfrastructure(Sim sim, TownPlan plan)
    {
        int n = 0;
        foreach (var c in plan.DcOrder)
            if (IsInfrastructure(sim.Grid[c.x, c.y])) n++;
        return n;
    }

    private static void ReservePowerTiles(Sim sim, List<TownPlan> towns, Policy policy)
    {
        int needed = (int)Math.Ceiling(Math.Max(1f, sim.PowerDemanded + 8f) / Structures.PowerPlant.PowerOutput);
        int have = 0;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                if (sim.Grid[x, y] == StructureType.PowerPlant) have++;
        if (have >= needed) return;
        foreach (var plan in towns)
            foreach (var c in plan.ParkOrder)
            {
                if (sim.Grid[c.x, c.y] != StructureType.None) continue;
                if (sim.TryPlace(c.x, c.y, StructureType.PowerPlant)) return;
            }
    }

    private static void EnsurePower(Sim sim, List<TownPlan> towns)
    {
        if (sim.PowerMargin > 6f) return;
        foreach (var plan in towns)
            foreach (var c in plan.ParkOrder)
            {
                if (sim.Grid[c.x, c.y] != StructureType.None) continue;
                if (sim.TryPlace(c.x, c.y, StructureType.PowerPlant)) return;
            }
    }

    private static void EnsureRecycler(Sim sim, List<TownPlan> towns)
    {
        int recyclers = 0;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                if (sim.Grid[x, y] == StructureType.Recycler) recyclers++;
        if (recyclers >= 2) return;
        foreach (var plan in towns)
        {
            foreach (var c in plan.ParkOrder)
            {
                if (sim.Grid[c.x, c.y] != StructureType.None) continue;
                if (sim.TryPlace(c.x, c.y, StructureType.Recycler)) return;
            }
        }
    }

    private static void EnsureCooling(Sim sim, TownPlan plan, float demand)
    {
        int towers = 0;
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
                if (sim.Grid[x, y] == StructureType.CoolingTower) towers++;
        if (towers >= 3) return;
        foreach (var c in plan.CoolOrder)
        {
            if (sim.Grid[c.x, c.y] != StructureType.None) continue;
            if (sim.CanPlaceAt(c.x, c.y, StructureType.CoolingTower) && sim.TryPlace(c.x, c.y, StructureType.CoolingTower))
                return;
        }
    }

    private static void BuildParks(Sim sim, TownPlan plan, int parksWant)
    {
        foreach (var c in plan.ParkOrder.Take(parksWant))
        {
            if (sim.Grid[c.x, c.y] == StructureType.Park) continue;
            if (IsInfrastructure(sim.Grid[c.x, c.y])) continue;
            if (sim.Grid[c.x, c.y] != StructureType.None) sim.DemolishAt(c.x, c.y);
            if (!sim.TryPlace(c.x, c.y, StructureType.Park)) return;
        }
    }

    private static int CountParks(Sim sim, TownPlan plan)
    {
        int n = 0;
        foreach (var (x, y) in plan.ParkOrder)
            if (sim.Grid[x, y] == StructureType.Park) n++;
        return n;
    }

    private static bool IsInfrastructure(StructureType s)
        => s == StructureType.PowerPlant || s == StructureType.CoolingTower || s == StructureType.Recycler;

    private static void ApplyTown(Sim sim, TownPlan plan, StructureDef tier, int dcWant, int parksWant, bool useForest)
    {
        var parkSet = new HashSet<(int, int)>(plan.ParkOrder.Take(parksWant));
        var dcCells = new List<(int x, int y)>();
        foreach (var c in plan.DcOrder)
        {
            if (dcCells.Count >= dcWant) break;
            if (parkSet.Contains(c)) continue;
            if (IsInfrastructure(sim.Grid[c.x, c.y])) continue;
            dcCells.Add(c);
        }
        if (useForest && dcCells.Count < dcWant)
            foreach (var c in plan.ForestCells)
            {
                if (dcCells.Count >= dcWant) break;
                dcCells.Add(c);
            }

        var dcSet = new HashSet<(int, int)>(dcCells);
        foreach (var c in plan.DcOrder.Concat(plan.ForestCells))
        {
            var s = sim.Grid[c.x, c.y];
            if (s == StructureType.None) continue;
            if (IsInfrastructure(s)) continue;
            bool keepAsDc = dcSet.Contains(c);
            bool keepAsPark = parkSet.Contains(c) && s == StructureType.Park;
            if (keepAsPark) continue;
            if (keepAsDc && s != StructureType.Park) continue;
            sim.DemolishAt(c.x, c.y);
        }

        foreach (var c in dcCells)
        {
            var s = sim.Grid[c.x, c.y];
            if (s == tier.Type) continue;
            if (s != StructureType.None && Structures.Get(s).Cost >= tier.Cost) continue;
            if (!sim.TryPlace(c.x, c.y, tier.Type)) return;
        }

        foreach (var c in plan.ParkOrder.Take(parksWant))
        {
            if (sim.Grid[c.x, c.y] != StructureType.None) continue;
            if (!sim.TryPlace(c.x, c.y, StructureType.Park)) return;
        }
    }

    private static void StepSprawl(Sim sim, MapGenerator map)
    {
        for (int x = 0; x < MapGenerator.GridWidth; x++)
            for (int y = 0; y < MapGenerator.GridHeight; y++)
            {
                if (map.CatchmentTown[x, y] >= 0) continue;
                if (sim.Grid[x, y] != StructureType.None) continue;
                if (!sim.CanPlaceAt(x, y, StructureType.Campus)) continue;
                if (!sim.TryPlace(x, y, StructureType.Campus)) return;
            }
    }
}
