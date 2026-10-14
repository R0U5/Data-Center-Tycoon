using System;
using System.Collections.Generic;
using System.Linq;

namespace DataCenterTycoon;

public enum EventKind { Instant, Timed, Choice }

public class EventEffects
{
    public float DemandMult = 1f;
    public float RevenueMult = 1f;
    public float UpkeepMult = 1f;
    public float BuildCostMult = 1f;
    public float NimbyMult = 1f;
    public float ComfortAdd = 0f;
    public float Cash = 0f;

    public bool IsNeutral =>
        Math.Abs(DemandMult - 1f) < 0.0001f && Math.Abs(RevenueMult - 1f) < 0.0001f &&
        Math.Abs(UpkeepMult - 1f) < 0.0001f && Math.Abs(BuildCostMult - 1f) < 0.0001f &&
        Math.Abs(NimbyMult - 1f) < 0.0001f && Math.Abs(ComfortAdd) < 0.0001f && Math.Abs(Cash) < 0.0001f;
}

public class GameEvent
{
    public string Id = "";
    public string Headline = "";
    public string Blurb = "";
    public EventKind Kind = EventKind.Timed;
    public int Duration;
    public bool Good;
    public EventEffects Effects = new();
    public (string label, EventEffects effects)? ChoiceA;
    public (string label, EventEffects effects)? ChoiceB;
    public int MinDay = 3;
}

public static class EventCatalog
{
    public static readonly GameEvent[] All =
    {
        new GameEvent
        {
            Id = "TARIFF_SHOCK",
            Headline = "TARIFF ON IMPORTED AI CHIPS",
            Blurb = "A VERY STABLE GENIUS SLAPS A 25% LEVY ON ACCELERATORS.",
            Kind = EventKind.Timed, Duration = 6,
            Effects = new EventEffects { BuildCostMult = 1.35f, DemandMult = 0.97f },
        },
        new GameEvent
        {
            Id = "MINERAL_EMBARGO",
            Headline = "RARE-EARTH EXPORT BAN",
            Blurb = "A RIVAL SUPERSTATE CUTS OFF GALLIUM. EVERYBODY PANICS.",
            Kind = EventKind.Timed, Duration = 5,
            Effects = new EventEffects { UpkeepMult = 1.25f },
        },
        new GameEvent
        {
            Id = "SUPERINTELLIGENCE_RENAME",
            Headline = "IT IS 'SUPERINTELLIGENCE' NOW",
            Blurb = "EVERYONE REBRANDED. DEMAND GOES PARABOLIC ON VIBES ALONE.",
            Kind = EventKind.Timed, Duration = 5, Good = true,
            Effects = new EventEffects { DemandMult = 1.20f },
        },
        new GameEvent
        {
            Id = "BAD_PRESS",
            Headline = "INVESTIGATIVE PIECE ON YOUR WATER USE",
            Blurb = "A LONGREAD SAYS YOUR RACKS DRANK THE RIVER. NEIGHBOURS ARE LIVID.",
            Kind = EventKind.Timed, Duration = 4,
            Effects = new EventEffects { DemandMult = 0.85f, NimbyMult = 1.15f },
        },
        new GameEvent
        {
            Id = "BUBBLE_FEAR",
            Headline = "ANALYST WARNS OF AN AI BUBBLE",
            Blurb = "A FOUNDER SAYS IT IS A BUBBLE. THE MARKET BELIEVES HIM THIS TIME.",
            Kind = EventKind.Timed, Duration = 3,
            Effects = new EventEffects { DemandMult = 0.90f, RevenueMult = 0.92f },
        },
        new GameEvent
        {
            Id = "GPU_SHORTAGE",
            Headline = "ACCELERATORS ON BACKORDER",
            Blurb = "SOLD OUT UNTIL FURTHER NOTICE. SCALPERS REJOICE.",
            Kind = EventKind.Timed, Duration = 5,
            Effects = new EventEffects { BuildCostMult = 1.5f },
        },
        new GameEvent
        {
            Id = "TALENT_WAR",
            Headline = "RIVAL POACHES YOUR WHOLE TEAM",
            Blurb = "A COMPETITOR OFFERS NINE FIGURES AND A CAVIAR BAR.",
            Kind = EventKind.Timed, Duration = 4,
            Effects = new EventEffects { UpkeepMult = 1.3f },
        },
        new GameEvent
        {
            Id = "POWER_PRICE_SPIKE",
            Headline = "VOTERS FURIOUS OVER ELECTRICITY BILLS",
            Blurb = "LAWMAKERS POINT AT DATA CENTRES. GRID UPGRADES GET PRICEY.",
            Kind = EventKind.Timed, Duration = 5,
            Effects = new EventEffects { UpkeepMult = 1.2f, NimbyMult = 1.1f },
        },
        new GameEvent
        {
            Id = "GOLD_RUSH",
            Headline = "HYPERSCALERS ANNOUNCE RECORD CAPEX",
            Blurb = "THE BIG NINE PLEDGE HALF A TRILLION. DEMAND FOLLOWS.",
            Kind = EventKind.Timed, Duration = 4, Good = true,
            Effects = new EventEffects { DemandMult = 1.25f },
        },
        new GameEvent
        {
            Id = "DROUGHT",
            Headline = "DROUGHT YEAR: COOLING RESERVES LOW",
            Blurb = "WATER RIGHTS ARE CONTESTED. EVAPORATIVE COOLING COSTS MORE.",
            Kind = EventKind.Timed, Duration = 6,
            Effects = new EventEffects { UpkeepMult = 1.3f },
        },
        new GameEvent
        {
            Id = "NIMBY_COUNCIL",
            Headline = "COUNTY COUNCIL BLOCKS NEW ZONING",
            Blurb = "THE PUBLIC COMMENT SESSION RAN UNTIL 2AM. IT DID NOT GO WELL.",
            Kind = EventKind.Timed, Duration = 6,
            Effects = new EventEffects { NimbyMult = 1.25f },
        },
        new GameEvent
        {
            Id = "SUBSIDY",
            Headline = "STATE OFFERS COMPUTE SUBSIDIES",
            Blurb = "A GOVERNOR WANTS TO BE THE 'AI CAPITAL'. CHEQUES INBOUND.",
            Kind = EventKind.Timed, Duration = 5, Good = true,
            Effects = new EventEffects { RevenueMult = 1.2f },
        },
        new GameEvent
        {
            Id = "ENDORSEMENT",
            Headline = "A MOVIE STAR MENTIONS YOUR SERVERS",
            Blurb = "ZERO CONTEXT. ENORMOUS ENGAGEMENT. DEMAND SPIKES.",
            Kind = EventKind.Timed, Duration = 3, Good = true,
            Effects = new EventEffects { DemandMult = 1.15f },
        },
        new GameEvent
        {
            Id = "REGULATION",
            Headline = "SWEEPING AI SAFETY LAW PASSES",
            Blurb = "COMPLIANCE COSTS RISE, BUT THE RULES CUT BOTH WAYS.",
            Kind = EventKind.Timed, Duration = 8,
            Effects = new EventEffects { UpkeepMult = 1.15f, DemandMult = 1.05f },
        },
        new GameEvent
        {
            Id = "REBRAND_RIVAL",
            Headline = "A RIVAL REBRANDS TO 'SYNERGETIC INTELLIGENCE'",
            Blurb = "A NEW LOGO, A NEW NONSENSE WORD, AND A FRESH HYPE CYCLE.",
            Kind = EventKind.Timed, Duration = 3, Good = true,
            Effects = new EventEffects { DemandMult = 1.1f },
        },
        new GameEvent
        {
            Id = "CELEB_POST",
            Headline = "INDUSTRY FIGURE POSTS AN UNHINGED THREAD",
            Blurb = "ELLEN MUSKY CALLS YOUR RACKS 'BASED'. THEN DELETES IT.",
            Kind = EventKind.Timed, Duration = 2, Good = true,
            Effects = new EventEffects { DemandMult = 1.12f },
        },
        new GameEvent
        {
            Id = "GRANT_STRINGS",
            Headline = "A STATE OFFERS YOU A GRANT",
            Blurb = "MONEY NOW, STRINGS ATTACHED.",
            Kind = EventKind.Choice, Duration = 0,
            ChoiceA = ("TAKE THE CASH", new EventEffects { Cash = 2500f, NimbyMult = 1.25f }),
            ChoiceB = ("KEEP IT CLEAN", new EventEffects { ComfortAdd = 6f }),
        },
        new GameEvent
        {
            Id = "BUY_RIVAL",
            Headline = "A FAILING RIVAL IS FOR SALE",
            Blurb = "THEIR CUSTOMERS COULD BE YOURS. FOR A PRICE.",
            Kind = EventKind.Choice, Duration = 0,
            ChoiceA = ("ACQUIRE THEM", new EventEffects { Cash = -1800f, DemandMult = 1.3f }),
            ChoiceB = ("PASS", new EventEffects { }),
        },
    };

    public static GameEvent ById(string id) => All.FirstOrDefault(e => e.Id == id);

    public static IReadOnlyList<GameEvent> Rollable(int day) =>
        All.Where(e => e.Kind != EventKind.Choice && day >= e.MinDay).ToList();

    public static IReadOnlyList<GameEvent> Choices(int day) =>
        All.Where(e => e.Kind == EventKind.Choice && day >= e.MinDay).ToList();
}
