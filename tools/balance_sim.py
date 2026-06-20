#!/usr/bin/env python3
"""
Balance simulation for Data Center Tycoon (per-town catchment model + terrain).

Mirrors the economy in Game1.cs so balance can be re-tuned without launching the
game. Keep the constants below in sync with the constants at the top of Game1.cs.

The model: demand lives at the towns. Each town t emits demand proportional to
its size (growing over time) and is served only by data centers within its
catchment. Packing compute around a town angers it - a convex NIMBY penalty
normalised by a size-based comfortable capacity (COMFORT_PER_SIZE * size).

Terrain ties:
- River = cooling. Tiles near a river ("cooled") cost COOLED_OP_FACTOR of normal
  upkeep, so you build on cheap cooled grass first.
- Forest = buffer. Forest left standing in a town's catchment reduces its NIMBY
  (up to BUFFER_RELIEF). A town's hard cap is empty cells (capacity); past that you
  must clear forest to keep serving - costing FOREST_BUILD_MULT and removing the
  buffer. (Skilled comfort-play stays under capacity and never clears forest.)

Demand grows ~5.5%/day forever, so each town's demand eventually exceeds its
comfortable capacity. Serving every town fully means over-packing -> revolt.
Skilled play serves each town to ~its comfortable capacity and banks the goal.

Run:  python3 tools/balance_sim.py
Healthy: meet_demand REVOLTS, comfort 0.9/1.0 WIN, comfort 1.1 mostly REVOLTS.
The per-town capacity/cooled/forest values below are measured averages from real
maps (ServiceRadius=6); for a per-seed check, run the economy over actual
MapGenerator instances (throwaway harness referencing MapGenerator.cs + Noise.cs).
"""
import math

# ---- constants (mirror Game1.cs) ----
START_MONEY = 650.0
WIN_CASH_GOAL = 6000.0
COST_PER_TILE = 12.0
REFUND_FRACTION = 0.5
OP_COST_PER_TILE = 1.0
COOLED_OP_FACTOR = 0.5
REVENUE_PER_SERVED = 2.7
FOREST_BUILD_MULT = 2.5
BASE_DEMAND = 100.0
DAILY_DEMAND_GROWTH = 0.055
NIMBY_WEIGHT = 14.0
NIMBY_EXP = 2.2
COMFORT_PER_SIZE = 3.85
BROWNOUT_WEIGHT = 50.0
BUFFER_RELIEF = 0.20
BUFFER_PER_SIZE = 1.0
HAPPINESS_FALL = 0.55
HAPPINESS_RISE = 0.16
HAPPINESS_LOSE = 35.0

# Representative towns (measured averages at ServiceRadius=6):
#   size drives demand share + comfort; cap = empty catchment cells (hard cap on
#   grass); cooled = cooled empty cells; forest = forest tiles in catchment.
TOWNS = [
    dict(size=22, cap=123, cooled=29, forest=21),
    dict(size=16, cap=109, cooled=20, forest=18),
    dict(size=10, cap=109, cooled=14, forest=14),
]
DENOM = sum(t["size"] for t in TOWNS)


def simulate(strategy, days=200):
    money = START_MONEY
    demand = BASE_DEMAND
    happiness = 100.0
    n = len(TOWNS)
    be = [0] * n   # built on empty (grass) cells
    bf = [0] * n   # built on cleared forest cells
    day = 1
    while day < days:
        dem = [TOWNS[i]["size"] / DENOM * demand for i in range(n)]
        # desired build composition: grass first (up to cap), then clear forest
        dE, dF = [0] * n, [0] * n
        for i in range(n):
            tgt = max(0, strategy(dem[i], TOWNS[i]))
            dE[i] = min(tgt, TOWNS[i]["cap"])
            dF[i] = max(0, min(tgt - TOWNS[i]["cap"], TOWNS[i]["forest"]))
        # removals (refund), then cash-limited builds: cheap grass first, then forest
        for i in range(n):
            if dE[i] < be[i]:
                money += (be[i] - dE[i]) * COST_PER_TILE * REFUND_FRACTION
                be[i] = dE[i]
            if dF[i] < bf[i]:
                money += (bf[i] - dF[i]) * COST_PER_TILE * FOREST_BUILD_MULT * REFUND_FRACTION
                bf[i] = dF[i]
        for i in sorted(range(n), key=lambda i: dE[i] - be[i], reverse=True):
            take = min(dE[i] - be[i], int(money // COST_PER_TILE))
            if take > 0:
                money -= take * COST_PER_TILE
                be[i] += take
        for i in sorted(range(n), key=lambda i: dF[i] - bf[i], reverse=True):
            take = min(dF[i] - bf[i], int(money // (COST_PER_TILE * FOREST_BUILD_MULT)))
            if take > 0:
                money -= take * COST_PER_TILE * FOREST_BUILD_MULT
                bf[i] += take

        served = total_demand = nimby = opcost = 0.0
        for i in range(n):
            supply = be[i] + bf[i]
            served += min(dem[i], supply)
            total_demand += dem[i]
            cooled_used = min(be[i], TOWNS[i]["cooled"])
            opcost += OP_COST_PER_TILE * ((be[i] - cooled_used) + COOLED_OP_FACTOR * cooled_used + bf[i])
            comfort = COMFORT_PER_SIZE * TOWNS[i]["size"]
            preserved = TOWNS[i]["forest"] - bf[i]
            relief = BUFFER_RELIEF * min(1.0, preserved / (BUFFER_PER_SIZE * TOWNS[i]["size"])) if preserved > 0 else 0.0
            nimby += NIMBY_WEIGHT * (supply / comfort) ** NIMBY_EXP * (1 - relief)
        money += REVENUE_PER_SERVED * served - opcost

        brownout = BROWNOUT_WEIGHT * (1 - served / total_demand) if total_demand > 0 else 0.0
        target = 100 - nimby - brownout
        rate = HAPPINESS_FALL if target < happiness else HAPPINESS_RISE
        happiness = max(0.0, min(100.0, happiness + rate * (target - happiness)))

        demand *= 1 + DAILY_DEMAND_GROWTH
        day += 1
        if happiness < HAPPINESS_LOSE:
            return ("REVOLT", day, happiness, money)
        if money < 0:
            return ("BANKRUPT", day, happiness, money)
        if money >= WIN_CASH_GOAL:
            return ("WIN", day, happiness, money)
    return ("TIMEOUT", day, happiness, money)


STRATEGIES = {
    "meet_demand (naive)": lambda dem, t: math.ceil(dem),
    "comfort 0.9 (skilled)": lambda dem, t: min(dem, 0.9 * COMFORT_PER_SIZE * t["size"]),
    "comfort 1.0 (skilled)": lambda dem, t: min(dem, 1.0 * COMFORT_PER_SIZE * t["size"]),
    "comfort 1.1 (over-pack)": lambda dem, t: min(dem, 1.1 * COMFORT_PER_SIZE * t["size"]),
}


def main():
    print("Data Center Tycoon - per-town balance check (constants mirror Game1.cs)\n")
    for name, strat in STRATEGIES.items():
        outcome, day, h, money = simulate(strat)
        print(f"  {name:24s} -> {outcome:8s} day {day:3d}  happiness {h:5.1f}  cash ${money:7.0f}")
    print("\nHealthy: meet_demand REVOLTS, comfort 0.9/1.0 WIN comfortably, comfort 1.1 rides the")
    print("edge (barely survives this average-case sim; revolts on most real maps - 2/16 seeds win).")


if __name__ == "__main__":
    main()
