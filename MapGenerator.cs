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

public class MapGenerator
{
    public const int TileSize = 40;
    public const int GridWidth = 48;
    public const int GridHeight = 27;

    public TileType[,] Tiles { get; }
    public long Seed { get; }

    private readonly float[,] _elevation;
    private readonly float[,] _moisture;

    public MapGenerator(long seed)
    {
        Seed = seed;
        Tiles = new TileType[GridWidth, GridHeight];
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
        PlaceTowns();
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

    private void PlaceTowns()
    {
        var rivers = new List<(int x, int y)>();
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
                if (Tiles[x, y] == TileType.River)
                    rivers.Add((x, y));

        if (rivers.Count == 0) return;

        var rng = new Random((int)(Seed + 31337));
        var buffers = new (TileType, int)[] { (TileType.Forest, 2) };

        var townSizes = new[] { 22, 16, 10 };

        foreach (int targetSize in townSizes)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int x, y;
                if (attempt < 100)
                {
                    var anchor = rivers[rng.Next(rivers.Count)];
                    int dist = 1 + rng.Next(3);
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
                if (IsNearType(x, y, TileType.Forest, 2)) continue;

                GrowCluster(rng, x, y, targetSize, TileType.Town, new[] { TileType.River, TileType.Forest, TileType.Town }, buffers);
                break;
            }
        }
    }

    private void GrowCluster(Random rng, int startX, int startY, int targetSize, TileType type, TileType[] avoid, (TileType type, int distance)[] buffers = null)
    {
        Tiles[startX, startY] = type;
        var placed = new HashSet<(int, int)> { (startX, startY) };
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
                if (placed.Contains((nx, ny))) continue;

                Tiles[nx, ny] = type;
                placed.Add((nx, ny));
                frontier.Add((nx, ny));
                count++;
            }
        }
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
