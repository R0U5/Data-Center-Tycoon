# DATA TYCOON — Satirical Events: research notes

Grounding for the random-event system. Every event is a real AI-industry news theme from
2024-2026, lightly disguised. Rules: no real names; the paraphrase should be *recognisable*.

## Real-world source material (2024-2026)

1. **Chip export controls / tariffs.** US export restrictions on AI accelerators to China
   (H200/A100 bans, reversals); China restricting gallium/germanium/indium-phosphide exports;
   tariffs on imported GPUs. Theme: hardware costs swing wildly on policy.
2. **Data-centre community backlash.** Q1 2026: ~75 projects worth ~$130B blocked or delayed;
   bipartisan opposition; "not in my back yard" for water + power + land. Theme: local anger.
3. **Electricity bills / voters.** Voters blaming data centres for rising utility bills;
   grid-upgrade fights; "ratepayers on the hook". Theme: operations cost + anger.
4. **AI bubble fears.** Aug 2025 Altman "bubble" comment + MIT "95% of gen-AI pilots show no
   returns" report → tech selloff. Theme: demand volatility.
5. **The "superintelligence" branding race.** Rebranding to chase the next hype cycle; naming
   competitions; "AGI"/"superintelligence" claims. Theme: demand spikes.
6. **Hyperscaler capex arms race.** Record capital spending on AI infra. Theme: demand boost.
7. **Water usage / drought.** Data centres consuming billions of gallons; drought-year backlash.
   Theme: cooling + anger.
8. **Talent wars / poaching.** Multi-hundred-million-dollar packages for AI researchers.
   Theme: upkeep / cost spike.
9. **Chip shortages / allocation.** GPUs sold out; waitlists; "allocate to the biggest buyers".
   Theme: build cost spike.
10. **AI regulation / safety laws.** EU AI Act, state-level bills, safety-testing rules.
    Theme: compliance cost, or a demand shift.

## Satirical characters (thinly veiled, recognisable)

- **Ellen Musky** — mercurial billionaire, posts a lot, renames things.
- **Sam Altmann** (→ "Sal Tman" / "Sammy Altman-Feldman") — the "bubble" quote.
- **The President** — never named; "a Very Stable Genius"-style unnamed official.
- **A senator from a state shaped like a mitten** — generic politician.
- **"The Big Nine"** — the hyperscalers collectively.

## Design

Events fire at the start of a day (after `AdvanceDay`). Some are instant (one-shot), some are
**timed** (last N days), some have a **choice** (two options — the good tycoon hook).

Modifiers are multiplicative scalars on existing quantities:
- `demandMult`, `revenueMult`, `upkeepMult`, `buildCostMult`, `nimbyMult`, `comfortAdd`,
  `powerOutputMult`.

Each event: id, headline, blurb, duration, effects (with sign), optional choice.

### Event list (first pass)

| id | headline | effect |
|---|---|---|
| TARIFF_SHOCK | "IMPORT TARIFF ON AI CHIPS" | buildCost x1.35, 6 days |
| MINERAL_EMBARGO | "RARE-EARTH EXPORT BAN" | upkeep x1.25, 5 days |
| SUPERINTELLIGENCE_RENAME | "EVERYONE IS CALLING IT SUPERINTELLIGENCE NOW" | demand x1.20, 5 days |
| BAD_PRESS | "INVESTIGATIVE PIECE ON DATA-CENTRE WATER USE" | demand x0.85, 4 days; nimby x1.15 |
| ELON_POST | "INDUSTRY FIGURE POSTS AN UNHINGED THREAD" | demand x1.15, 2 days; comedic |
| BUBBLE_FEAR | "ANALYST WARNS OF AN AI BUBBLE" | demand x0.90, 3 days |
| GPU_SHORTAGE | "ACCELERATORS ON BACKORDER" | buildCost x1.5, 5 days |
| TALENT_WAR | "RIVAL POACHES YOUR WHOLE TEAM" | upkeep x1.3, 4 days |
| POWER_PRICE_SPIKE | "VOTERS FURIOUS OVER ELECTRICITY BILLS" | upkeep x1.2 + nimby x1.1, 5 days |
| REGULATION | "SWEEPING AI SAFETY LAW PASSES" | upkeep x1.15, 8 days; demand x1.05 (compliance certainty) |
| GOLD_RUSH_CAPEX | "HYPERSCALERS ANNOUNCE RECORD CAPEX" | demand x1.25, 4 days |
| NAME_CHANGE | "A RIVAL REBRANDS TO 'SYNERGETIC INTELLIGENCE'" | demand x1.1, 3 days |
| DROUGHT | "DROUGHT YEAR: COOLING RESERVES LOW" | upkeep x1.3, 6 days |
| NIMBY_COUNCIL | "COUNTY COUNCIL VOTES AGAINST NEW ZONING" | nimby x1.25, 6 days |
| GIFT_SUBSIDY | "STATE OFFERS COMPUTE SUBSIDIES" | revenue x1.2, 5 days |
| CELEBRITY_ENDORSEMENT | "A MOVIE STAR MENTIONS YOU" | demand x1.15, 3 days |

Choices (the interesting ones):
| id | choice |
|---|---|
| SUBSIDY_VS_STRINGS | take a state grant (cash now, nimby up later) or decline |
| BUY_THE_STARTUP | acquire a rival (cash cost, demand boost) |
