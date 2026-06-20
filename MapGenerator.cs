using System;
using System.Collections.Generic;

namespace DataCenterTycoon;

public enum TileType
{
    Empty,
    River,
    Town,
    Forest
}

public class Town
{
    public int Id { get; }
    public List<(int x, int y)> Tiles { get; } = new();
    public int Size => Tiles.Count;
    public float CenterX { get; private set; }
    public float CenterY { get; private set; }
    public int CatchmentCapacity { get; set; }

    public Town(int id) { Id = id; }

    public void Recenter()
    {
        if (Tiles.Count == 0) return;
        float sx = 0, sy = 0;
        foreach (var (x, y) in Tiles) { sx += x; sy += y; }
        CenterX = sx / Tiles.Count;
        CenterY = sy / Tiles.Count;
    }
}

public class MapGenerator
{
    public const int TileSize = 40;
    public const int GridWidth = 48;
    public const int GridHeight = 27;
    public const int ServiceRadius = 6;

    public TileType[,] Tiles { get; }
    public long Seed { get; }
    public List<Town> Towns { get; } = new();
    public int[,] CatchmentTown { get; }

    private readonly float[,] _elevation;
    private readonly float[,] _moisture;

    private static readonly int[] TownSizes = { 22, 16, 10 };

    public MapGenerator(long seed)
    {
        Seed = seed;
        Tiles = new TileType[GridWidth, GridHeight];
        CatchmentTown = new int[GridWidth, GridHeight];
        _elevation = new float[GridWidth, GridHeight];
        _moisture = new float[GridWidth, GridHeight];

        var elevationNoise = new Noise(seed);
        var moistureNoise = new Noise(seed + 7919);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                float ux = x * 0.05f;
                float uy = y * 0.05f;
                _elevation[x, y] = (elevationNoise.Sample(ux, uy) + 1f) * 0.5f;
                _moisture[x, y] = (moistureNoise.Sample(ux * 1.3f, uy * 1.3f) + 1f) * 0.5f;
            }
        }

        IdentifyRiver();
        PlaceForests();
        PlaceTownsConsiderate();
        ComputeCatchments();
    }

    private void IdentifyRiver()
    {
        const int minRiverTiles = (int)(GridWidth * GridHeight * 0.15f);
        float threshold = 0.32f;
        List<(int x, int y)> largest = null;

        while (largest == null || largest.Count < minRiverTiles)
        {
            var visited = new bool[GridWidth, GridHeight];
            var components = new List<List<(int x, int y)>>();

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    if (_elevation[x, y] >= threshold) continue;
                    if (visited[x, y]) continue;

                    var component = new List<(int, int)>();
                    var queue = new Queue<(int, int)>();
                    queue.Enqueue((x, y));
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        component.Add((cx, cy));

                        foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                            if (visited[nx, ny]) continue;
                            if (_elevation[nx, ny] >= threshold) continue;

                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }

                    if (component.Count > 0)
                        components.Add(component);
                }
            }

            if (components.Count == 0)
            {
                threshold += 0.02f;
                continue;
            }

            largest = components[0];
            foreach (var c in components)
                if (c.Count > largest.Count) largest = c;

            if (largest.Count < minRiverTiles)
                threshold += 0.02f;
        }

        foreach (var (x, y) in largest)
            Tiles[x, y] = TileType.River;
    }

    private void PlaceForests()
    {
        const int minForestTiles = (int)(GridWidth * GridHeight * 0.20f);
        const int maxForestTiles = (int)(GridWidth * GridHeight * 0.35f);
        const int riverBuffer = 1;
        float moistureThreshold = 0.62f;
        List<List<(int x, int y)>> bestComponents = null;
        int bestTotal = 0;

        for (int iteration = 0; iteration < 20; iteration++)
        {
            var visited = new bool[GridWidth, GridHeight];
            var components = new List<List<(int x, int y)>>();

            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    if (_moisture[x, y] < moistureThreshold) continue;
                    if (Tiles[x, y] != TileType.Empty) continue;
                    if (visited[x, y]) continue;
                    if (IsNearType(x, y, TileType.River, riverBuffer)) continue;

                    var component = new List<(int, int)>();
                    var queue = new Queue<(int, int)>();
                    queue.Enqueue((x, y));
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        component.Add((cx, cy));

                        foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                            if (visited[nx, ny]) continue;
                            if (_moisture[nx, ny] < moistureThreshold) continue;
                            if (Tiles[nx, ny] != TileType.Empty) continue;
                            if (IsNearType(nx, ny, TileType.River, riverBuffer)) continue;

                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }

                    if (component.Count > 0)
                        components.Add(component);
                }
            }

            components.Sort((a, b) => b.Count.CompareTo(a.Count));
            int total = 0;
            for (int i = 0; i < Math.Min(2, components.Count); i++)
                total += components[i].Count;

            if (total >= minForestTiles && total <= maxForestTiles)
            {
                bestComponents = components;
                break;
            }

            if (bestComponents == null || Math.Abs(total - minForestTiles) < Math.Abs(bestTotal - minForestTiles))
            {
                bestComponents = components;
                bestTotal = total;
            }

            if (total < minForestTiles)
                moistureThreshold -= 0.02f;
            else
                moistureThreshold += 0.02f;
        }

        if (bestComponents != null)
        {
            for (int i = 0; i < Math.Min(2, bestComponents.Count); i++)
            {
                foreach (var (x, y) in bestComponents[i])
                    Tiles[x, y] = TileType.Forest;
            }
        }
    }

    private void PlaceTownsConsiderate()
    {
        var rivers = new List<(int x, int y)>();
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
                if (Tiles[x, y] == TileType.River)
                    rivers.Add((x, y));

        List<(int x, int y)> bestCells = null;
        int bestScore = int.MinValue;

        for (int attempt = 0; attempt < 70; attempt++)
        {
            ClearTownTiles();
            var rng = new Random(unchecked((int)Seed + 31337 + attempt * 1009));
            var placed = TryPlaceAllTowns(rng, rivers);

            if (placed.Count < TownSizes.Length) continue;

            int score = ScoreTownLayout(placed);
            if (score > bestScore)
            {
                bestScore = score;
                bestCells = CollectTownCells(placed);
            }
            if (score >= FairScoreThreshold())
                break;
        }

        ClearTownTiles();
        if (bestCells != null)
            foreach (var (x, y) in bestCells)
                Tiles[x, y] = TileType.Town;

        BuildTownsFromTiles();
    }

    private int FairScoreThreshold()
    {
        int sum = 0;
        foreach (int s in TownSizes) sum += TargetCapacity(s);
        return (int)(sum * 0.85f);
    }

    private static int TargetCapacity(int size) => (int)(6.67f * size);
    private static int MinCapacity(int size) => (int)(3.0f * size);

    private List<List<(int x, int y)>> TryPlaceAllTowns(Random rng, List<(int x, int y)> rivers)
    {
        var result = new List<List<(int x, int y)>>();
        var buffers = new (TileType, int)[] { (TileType.Forest, 2) };

        foreach (int targetSize in TownSizes)
        {
            List<(int x, int y)> townCells = null;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int x, y;
                if (rivers.Count > 0 && attempt < 120)
                {
                    var anchor = rivers[rng.Next(rivers.Count)];
                    int dist = 2 + rng.Next(4);
                    int side = rng.Next(4);
                    switch (side)
                    {
                        case 0: x = anchor.x - dist; y = anchor.y; break;
                        case 1: x = anchor.x + dist; y = anchor.y; break;
                        case 2: x = anchor.x; y = anchor.y - dist; break;
                        default: x = anchor.x; y = anchor.y + dist; break;
                    }
                }
                else
                {
                    x = rng.Next(GridWidth);
                    y = rng.Next(GridHeight);
                }

                if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight) continue;
                if (Tiles[x, y] != TileType.Empty) continue;
                if (IsNearType(x, y, TileType.Town, 3)) continue;
                if (attempt < 160 && IsNearType(x, y, TileType.Forest, 2)) continue;

                townCells = GrowCluster(rng, x, y, targetSize, TileType.Town,
                    new[] { TileType.River, TileType.Forest, TileType.Town }, buffers);
                if (townCells.Count >= targetSize * 0.6f) break;
                foreach (var (cx, cy) in townCells) Tiles[cx, cy] = TileType.Empty;
                townCells = null;
            }

            if (townCells == null) break;
            result.Add(townCells);
        }

        return result;
    }

    private int ScoreTownLayout(List<List<(int x, int y)>> townCellLists)
    {
        var nearest = ComputeNearestTown(townCellLists);
        var capacity = new int[townCellLists.Count];
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
            {
                int t = nearest[x, y];
                if (t >= 0 && Tiles[x, y] == TileType.Empty)
                    capacity[t]++;
            }

        int score = 0;
        for (int i = 0; i < townCellLists.Count; i++)
        {
            int size = townCellLists[i].Count;
            score += Math.Min(capacity[i], TargetCapacity(size));
            if (capacity[i] < MinCapacity(size))
                score -= (MinCapacity(size) - capacity[i]) * 4;
        }
        return score;
    }

    private int[,] ComputeNearestTown(List<List<(int x, int y)>> townCellLists)
    {
        var nearest = new int[GridWidth, GridHeight];
        var dist = new int[GridWidth, GridHeight];
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++) { nearest[x, y] = -1; dist[x, y] = int.MaxValue; }

        var queue = new Queue<(int x, int y)>();
        for (int t = 0; t < townCellLists.Count; t++)
            foreach (var (x, y) in townCellLists[t])
            {
                if (nearest[x, y] != -1) continue;
                nearest[x, y] = t;
                dist[x, y] = 0;
                queue.Enqueue((x, y));
            }

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (dist[cx, cy] >= ServiceRadius) continue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                    if (nearest[nx, ny] != -1) continue;
                    nearest[nx, ny] = nearest[cx, cy];
                    dist[nx, ny] = dist[cx, cy] + 1;
                    queue.Enqueue((nx, ny));
                }
        }

        return nearest;
    }

    private void ComputeCatchments()
    {
        var townCellLists = new List<List<(int x, int y)>>();
        foreach (var t in Towns) townCellLists.Add(t.Tiles);

        var nearest = ComputeNearestTown(townCellLists);
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
                CatchmentTown[x, y] = nearest[x, y];

        foreach (var t in Towns) t.CatchmentCapacity = 0;
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
            {
                int t = CatchmentTown[x, y];
                if (t >= 0 && Tiles[x, y] == TileType.Empty)
                    Towns[t].CatchmentCapacity++;
            }
    }

    private List<(int x, int y)> CollectTownCells(List<List<(int x, int y)>> townCellLists)
    {
        var all = new List<(int x, int y)>();
        foreach (var list in townCellLists)
            all.AddRange(list);
        return all;
    }

    private void ClearTownTiles()
    {
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
                if (Tiles[x, y] == TileType.Town)
                    Tiles[x, y] = TileType.Empty;
    }

    private void BuildTownsFromTiles()
    {
        Towns.Clear();
        var visited = new bool[GridWidth, GridHeight];
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
            {
                if (Tiles[x, y] != TileType.Town || visited[x, y]) continue;
                var town = new Town(Towns.Count);
                var queue = new Queue<(int x, int y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    town.Tiles.Add((cx, cy));
                    foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                        if (visited[nx, ny] || Tiles[nx, ny] != TileType.Town) continue;
                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
                town.Recenter();
                Towns.Add(town);
            }
    }

    private List<(int x, int y)> GrowCluster(Random rng, int startX, int startY, int targetSize, TileType type, TileType[] avoid, (TileType type, int distance)[] buffers = null)
    {
        Tiles[startX, startY] = type;
        var placed = new List<(int x, int y)> { (startX, startY) };
        var placedSet = new HashSet<(int, int)> { (startX, startY) };
        var frontier = new List<(int, int)> { (startX, startY) };
        int count = 1;

        while (count < targetSize && frontier.Count > 0)
        {
            int idx = rng.Next(frontier.Count);
            var (cx, cy) = frontier[idx];
            frontier.RemoveAt(idx);

            var dirs = new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            foreach (var (dx, dy) in dirs)
            {
                if (count >= targetSize) break;
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                if (IsForbidden(nx, ny, avoid, buffers)) continue;
                if (placedSet.Contains((nx, ny))) continue;

                Tiles[nx, ny] = type;
                placed.Add((nx, ny));
                placedSet.Add((nx, ny));
                frontier.Add((nx, ny));
                count++;
            }
        }

        return placed;
    }

    private bool IsForbidden(int x, int y, TileType[] avoid, (TileType type, int distance)[] buffers)
    {
        if (Array.IndexOf(avoid, Tiles[x, y]) >= 0) return true;
        if (buffers == null) return false;
        foreach (var (type, dist) in buffers)
        {
            for (int dx = -dist; dx <= dist; dx++)
                for (int dy = -dist; dy <= dist; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                    if (Tiles[nx, ny] == type) return true;
                }
        }
        return false;
    }

    private bool IsNearType(int x, int y, TileType type, int buffer)
    {
        for (int dx = -buffer; dx <= buffer; dx++)
            for (int dy = -buffer; dy <= buffer; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= GridWidth || ny < 0 || ny >= GridHeight) continue;
                if (Tiles[nx, ny] == type) return true;
            }
        return false;
    }
}
