using System;

namespace DataCenterTycoon;

public enum StructureType
{
    None = 0,
    Shed = 1,
    Hall = 2,
    Campus = 3,
    Park = 4,
    CoolingTower = 5,
    PowerPlant = 6,
    Recycler = 7
}

public interface IServesDemand
{
    float Compute { get; }
    float NimbyLoad { get; }
}

public interface ICalms
{
    float ComfortAdd { get; }
}

public interface ICools
{
    int CoolingReach { get; }
}

public interface IPowers
{
    float PowerOutput { get; }
}

public interface IConsumesPower
{
    float PowerDraw { get; }
}

public interface IGeneratesValue
{
    float PassiveIncome { get; }
}

public class StructureDef
    : IServesDemand, ICalms, ICools, IPowers, IConsumesPower, IGeneratesValue
{
    public StructureType Type { get; }
    public string Name { get; }
    public string Short { get; }
    public float Cost { get; }
    public float Upkeep { get; }
    public float NimbyLoad { get; }
    public float ComfortAdd { get; }
    public float Compute { get; }
    public int CoolingReach { get; }
    public float PowerOutput { get; }
    public float PowerDraw { get; }
    public float PassiveIncome { get; }

    public bool ServesDemand => Compute > 0f;
    public bool Calms => ComfortAdd != 0f;
    public bool Cools => CoolingReach > 0;
    public bool Powers => PowerOutput > 0f;
    public bool ConsumesPower => PowerDraw > 0f;
    public bool GeneratesValue => PassiveIncome > 0f;

    public StructureDef(StructureType type, string name, string shortName,
        float cost, float upkeep,
        float compute = 0f, float nimbyLoad = 0f, float comfortAdd = 0f,
        int coolingReach = 0, float powerOutput = 0f, float powerDraw = 0f, float passiveIncome = 0f)
    {
        Type = type;
        Name = name;
        Short = shortName;
        Cost = cost;
        Upkeep = upkeep;
        Compute = compute;
        NimbyLoad = nimbyLoad;
        ComfortAdd = comfortAdd;
        CoolingReach = coolingReach;
        PowerOutput = powerOutput;
        PowerDraw = powerDraw;
        PassiveIncome = passiveIncome;
    }
}

public static class Structures
{
    public static readonly StructureDef Shed = new(
        StructureType.Shed, "SERVER SHED", "SHED", 12f, 1.0f,
        compute: 1f, nimbyLoad: 1.0f, powerDraw: 0f);

    public static readonly StructureDef Hall = new(
        StructureType.Hall, "DATA HALL", "HALL", 55f, 4.0f,
        compute: 3f, nimbyLoad: 1.9f, powerDraw: 0f);

    public static readonly StructureDef Campus = new(
        StructureType.Campus, "HYPERSCALE", "HYPR", 170f, 12.0f,
        compute: 7f, nimbyLoad: 3.4f, powerDraw: 2f);

    public static readonly StructureDef Park = new(
        StructureType.Park, "PARK", "PARK", 20f, 0.4f, comfortAdd: 3.0f);

    public static readonly StructureDef CoolingTower = new(
        StructureType.CoolingTower, "COOLING TOWER", "COOL", 90f, 3.0f,
        comfortAdd: 1.0f, coolingReach: 1, powerDraw: 1f);

    public static readonly StructureDef PowerPlant = new(
        StructureType.PowerPlant, "POWER PLANT", "PWR", 130f, 2.0f,
        nimbyLoad: 2.2f, powerOutput: 22f);

    public static readonly StructureDef Recycler = new(
        StructureType.Recycler, "E-WASTE RECYCLER", "RECYC", 60f, 1.2f,
        nimbyLoad: 0.6f, passiveIncome: 6f, powerDraw: 1f);

    public static readonly StructureDef[] All = { Shed, Hall, Campus, Park, CoolingTower, PowerPlant, Recycler };

    public static readonly StructureDef[] TierLadder = { Shed, Hall, Campus };

    public static readonly StructureDef[] PowerSources = { PowerPlant };

    public static StructureDef Get(StructureType type) => type switch
    {
        StructureType.Shed => Shed,
        StructureType.Hall => Hall,
        StructureType.Campus => Campus,
        StructureType.Park => Park,
        StructureType.CoolingTower => CoolingTower,
        StructureType.PowerPlant => PowerPlant,
        StructureType.Recycler => Recycler,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static string Display(StructureType type)
        => type == StructureType.None ? "RAZE" : Get(type).Short;

    public static bool IsDefined(StructureType type)
        => type == StructureType.None || (int)type > 0 && (int)type < All.Length + 1;
}
