#!/usr/bin/env python3
"""
Balance simulation for Data Center Tycoon (per-town catchment model).

Mirrors the economy in Game1.cs so balance can be re-tuned without launching the
game. Keep the constants below in sync with the constants at the top of Game1.cs.

The model: demand lives at the towns. Each town t emits demand proportional to
its size (growing over time) and can only be served by data centers within its
service catchment. Packing compute around a town angers it - a convex NIMBY
penalty normalised by a size-based "comfortable capacity" (COMFORT_PER_SIZE *
size). A town's hard serving cap is how many tiles physically fit in its
catchment (geometry; varies by map - here approximated by CAPACITY per town).

There is no safe "serve everyone": demand grows ~5.5%/day forever, so to keep
every town fully served you must pack them past comfort and they revolt. Skilled
play serves each town up to ~its comfortable capacity, accepts the growing
brownout, and banks the cash goal before happiness erodes.

Run:  python3 tools/balance_sim.py
Healthy balance prints: meet_demand -> REVOLT, comfort 0.9/1.0 -> WIN,
comfort 1.1 (over-pack) -> mostly REVOLT.
"""
import math

# ---- constants (mirror Game1.cs) ----
START_MONEY = 650.0
WIN_CASH_GOAL = 6000.0
COST_PER_TILE = 12.0
REFUND_FRACTION = 0.5
OP_COST_PER_TILE = 1.0
REVENUE_PER_SERVED = 2.7
BASE_DEMAND = 100.0
DAILY_DEMAND_GROWTH = 0.055
NIMBY_WEIGHT = 11.0
NIMBY_EXP = 2.2
COMFORT_PER_SIZE = 3.85
BROWNOUT_WEIGHT = 50.0
HAPPINESS_FALL = 0.55
HAPPINESS_RISE = 0.16
HAPPINESS_LOSE = 35.0

# Representative town layout. SIZE drives demand share + comfortable capacity;
# CAPACITY is the catchment hard cap (set by terrain/geometry in-game; these are
# the measured averages for ServiceRadius=6 on a fair map: ~126/100/81).
TOWNS = [
    dict(size=22, capacity=126),
    dict(size=16, capacity=100),
    dict(size=10, capacity=81),
]
DENOM = sum(t["size"] for t in TOWNS)


def simulate(strategy, days=200):
    money = START_MONEY
    demand = BASE_DEMAND
    happiness = 100.0
    supply = [0] * len(TOWNS)
    day = 1
    while day < days:
        dem = [t["size"] / DENOM * demand for t in TOWNS]
        desired = [max(0, min(strategy(dem[i], TOWNS[i]), TOWNS[i]["capacity"]))
                   for i in range(len(TOWNS))]
        # removals refund, then cash-limited builds (greedy by need)
        for i in range(len(TOWNS)):
            if desired[i] < supply[i]:
                money += (supply[i] - desired[i]) * COST_PER_TILE * REFUND_FRACTION
                supply[i] = desired[i]
        budget = int(money // COST_PER_TILE)
        for i in sorted(range(len(TOWNS)), key=lambda i: desired[i] - supply[i], reverse=True):
            take = min(max(0, desired[i] - supply[i]), budget)
            money -= take * COST_PER_TILE
            supply[i] += take
            budget -= take

        served = sum(min(dem[i], supply[i]) for i in range(len(TOWNS)))
        total_demand = sum(dem)
        tiles = sum(supply)
        money += REVENUE_PER_SERVED * served - OP_COST_PER_TILE * tiles

        nimby = sum(NIMBY_WEIGHT * (supply[i] / (COMFORT_PER_SIZE * TOWNS[i]["size"])) ** NIMBY_EXP
                    for i in range(len(TOWNS)))
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
    print("\nHealthy: meet_demand REVOLTS, comfort 0.9/1.0 WIN, comfort 1.1 over-packs into a REVOLT.")


if __name__ == "__main__":
    main()
