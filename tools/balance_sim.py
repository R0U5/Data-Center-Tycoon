#!/usr/bin/env python3
"""
Balance simulation for Data Center Tycoon.

This mirrors the economic / happiness model implemented in Game1.cs so the
core balance can be re-tuned without launching the game. Keep the constants
in CONFIG below in sync with the constants at the top of Game1.cs.

The point of the model: the player must serve a compute demand that grows
every day, by covering the map with data centers. Covering land costs money
(build + daily operating cost) and, increasingly, happiness (an *uncapped,
convex* footprint penalty). Failing to meet demand costs happiness too
(brownout). There is no safe "middle of the road": once demand outgrows the
cheap (empty) land, you can no longer both meet demand and stay liked, so
"just meet all demand" becomes a losing move. Skilled play caps the footprint,
accepts partial service, and banks the cash goal before the community revolts.

Run:  python3 tools/balance_sim.py
A healthy balance prints: meet_demand -> REVOLT, an adaptive strategy -> WIN,
and both too-lean and too-aggressive -> REVOLT/BANKRUPT.
"""
import math

CONFIG = dict(
    START_MONEY=650.0,
    WIN_CASH_GOAL=7000.0,
    COST_PER_TILE=12.0,
    REFUND_FRACTION=0.5,
    OP_COST_PER_TILE=1.0,
    REVENUE_PER_SERVED=2.4,
    COMPUTE_PER_TILE=1.0,
    BASE_DEMAND=100.0,
    DAILY_DEMAND_GROWTH=0.07,
    FOOTPRINT_WEIGHT=130.0,
    FOOTPRINT_EXP=1.8,
    BROWNOUT_WEIGHT=70.0,
    HAPPINESS_FALL=0.55,
    HAPPINESS_RISE=0.16,
    HAPPINESS_LOSE=35.0,
    GRID_AREA=48 * 27,
    # Empty buildable tiles before you spill onto town/river/forest. In-game this
    # is the actual count of empty tiles on the map and varies by seed; the spill
    # weight is the average TileWeight of non-empty land.
    USABLE=690,
    SPILL_WEIGHT=2.5,
)


def weighted_covered(tiles, c):
    if tiles <= c["USABLE"]:
        return float(tiles)
    return c["USABLE"] + (tiles - c["USABLE"]) * c["SPILL_WEIGHT"]


def happiness_target(supply, weighted, demand, c):
    served = min(1.0, supply / demand) if demand > 0 else 1.0
    footprint = c["FOOTPRINT_WEIGHT"] * (weighted / c["GRID_AREA"]) ** c["FOOTPRINT_EXP"]
    brownout = c["BROWNOUT_WEIGHT"] * (1.0 - served)
    return 100.0 - footprint - brownout


def simulate(strategy, c, max_days=200):
    money = c["START_MONEY"]
    demand = c["BASE_DEMAND"]
    happiness = 100.0
    tiles = 0
    day = 1
    while day < max_days:
        target = max(0, strategy(day, demand, tiles, c))
        if target > tiles:
            affordable = int(money // c["COST_PER_TILE"])
            new_tiles = min(target, tiles + affordable)
            money -= (new_tiles - tiles) * c["COST_PER_TILE"]
            tiles = new_tiles
        elif target < tiles:
            money += (tiles - target) * c["COST_PER_TILE"] * c["REFUND_FRACTION"]
            tiles = target

        supply = tiles * c["COMPUTE_PER_TILE"]
        served = min(supply, demand)
        money += c["REVENUE_PER_SERVED"] * served - c["OP_COST_PER_TILE"] * tiles

        tgt = happiness_target(supply, weighted_covered(tiles, c), demand, c)
        rate = c["HAPPINESS_FALL"] if tgt < happiness else c["HAPPINESS_RISE"]
        happiness = max(0.0, min(100.0, happiness + rate * (tgt - happiness)))

        demand *= 1.0 + c["DAILY_DEMAND_GROWTH"]
        day += 1
        if happiness < c["HAPPINESS_LOSE"]:
            return ("REVOLT", day, happiness, money, tiles)
        if money < 0:
            return ("BANKRUPT", day, happiness, money, tiles)
        if money >= c["WIN_CASH_GOAL"]:
            return ("WIN", day, happiness, money, tiles)
    return ("TIMEOUT", day, happiness, money, tiles)


STRATEGIES = {
    "meet_demand (naive)": lambda day, d, t, c: math.ceil(d),
    "cap_usable (skilled)": lambda day, d, t, c: min(math.ceil(d), c["USABLE"]),
    "lean 0.5": lambda day, d, t, c: math.ceil(d * 0.5),
    "lean 0.8": lambda day, d, t, c: math.ceil(d * 0.8),
    "aggressive 1.25": lambda day, d, t, c: math.ceil(d * 1.25),
}


def main():
    print("Data Center Tycoon - balance check (constants mirror Game1.cs)\n")
    for name, strat in STRATEGIES.items():
        outcome, day, h, money, tiles = simulate(strat, CONFIG)
        print(f"  {name:22s} -> {outcome:8s} day {day:3d}  happiness {h:5.1f}  "
              f"cash ${money:7.0f}  tiles {tiles:4d}")
    print("\nHealthy balance: 'meet_demand' loses, 'cap_usable' wins, lean/aggressive lose.")


if __name__ == "__main__":
    main()
