using System;

namespace DataCenterTycoon;

public class Noise
{
    private readonly int[] _perm;

    public Noise(long seed)
    {
        var rng = new Random((int)seed);
        _perm = new int[512];
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public float Sample(float x, float y)
    {
        int X = (int)Math.Floor(x);
        int Y = (int)Math.Floor(y);
        x -= X;
        y -= Y;

        float u = Fade(x);
        float v = Fade(y);

        int aa = _perm[_perm[X & 255] + (Y & 255)];
        int ab = _perm[_perm[X & 255] + ((Y + 1) & 255)];
        int ba = _perm[_perm[(X + 1) & 255] + (Y & 255)];
        int bb = _perm[_perm[(X + 1) & 255] + ((Y + 1) & 255)];

        return Lerp(
            Lerp(Grad(aa, x, y), Grad(ba, x - 1, y), u),
            Lerp(Grad(ab, x, y - 1), Grad(bb, x - 1, y - 1), u),
            v);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y)
    {
        int h = hash & 7;
        float u = h < 4 ? x : y;
        float v = h < 4 ? y : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
