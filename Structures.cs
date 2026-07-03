using System;

namespace DataCenterTycoon;

public enum StructureType
{
    None = 0,
    Shed = 1,
    Hall = 2,
    Campus = 3,
    Park = 4
}

public class StructureDef
{
    public StructureType Type { get; }
    public string Name { get; }
    public string Short { get; }
    public float Cost { get; }
    public float Upkeep { get; }
    public float Compute { get; }
    public float NimbyLoad { get; }
    public float ComfortAdd { get; }

    public StructureDef(StructureType type, string name, string shortName,
        float cost, float upkeep, float compute, float nimbyLoad, float comfortAdd)
    {
        Type = type;
        Name = name;
        Short = shortName;
        Cost = cost;
        Upkeep = upkeep;
        Compute = compute;
        NimbyLoad = nimbyLoad;
        ComfortAdd = comfortAdd;
    }
}

public static class Structures
{
    public static readonly StructureDef Shed = new(StructureType.Shed, "SERVER SHED", "SHED", 12f, 1.0f, 1f, 1.0f, 0f);
    public static readonly StructureDef Hall = new(StructureType.Hall, "DATA HALL", "HALL", 55f, 4.0f, 3f, 1.9f, 0f);
    public static readonly StructureDef Campus = new(StructureType.Campus, "HYPERSCALE", "HYPR", 170f, 12.0f, 7f, 3.4f, 0f);
    public static readonly StructureDef Park = new(StructureType.Park, "PARK", "PARK", 20f, 0.4f, 0f, 0f, 3.0f);

    public static readonly StructureDef[] All = { Shed, Hall, Campus, Park };

    public static StructureDef Get(StructureType type) => type switch
    {
        StructureType.Shed => Shed,
        StructureType.Hall => Hall,
        StructureType.Campus => Campus,
        StructureType.Park => Park,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
