using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace DataCenterTycoon;

public class SoftCanvas
{
    public int Width { get; }
    public int Height { get; }
    private readonly Color[] _px;

    public SoftCanvas(int w, int h)
    {
        Width = w;
        Height = h;
        _px = new Color[w * h];
        for (int i = 0; i < _px.Length; i++) _px[i] = Color.Transparent;
    }

    public Color[] Pixels => _px;

    public void Clear(Color c)
    {
        for (int i = 0; i < _px.Length; i++) _px[i] = c;
    }

    public void Fill(Rectangle r, Color c)
    {
        int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
        int x1 = Math.Min(Width, r.X + r.Width), y1 = Math.Min(Height, r.Y + r.Height);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                BlendPixel(x, y, c);
    }

    public void Outline(Rectangle r, Color c)
    {
        Fill(new Rectangle(r.X, r.Y, r.Width, 1), c);
        Fill(new Rectangle(r.X, r.Y + r.Height - 1, r.Width, 1), c);
        Fill(new Rectangle(r.X, r.Y, 1, r.Height), c);
        Fill(new Rectangle(r.X + r.Width - 1, r.Y, 1, r.Height), c);
    }

    private void BlendPixel(int x, int y, Color c)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        int i = y * Width + x;
        if (c.A == 255) { _px[i] = c; return; }
        if (c.A == 0) return;
        var d = _px[i];
        float a = c.A / 255f;
        _px[i] = new Color(
            (byte)(c.R * a + d.R * (1 - a)),
            (byte)(c.G * a + d.G * (1 - a)),
            (byte)(c.B * a + d.B * (1 - a)),
            (byte)255);
    }

    public void BlitPattern(string[] rows, Dictionary<char, Color> palette, Rectangle dest, Color tint)
    {
        int ph = rows.Length;
        if (ph == 0) return;
        int pw = rows[0].Length;
        for (int dy = 0; dy < dest.Height; dy++)
        {
            int sy = dy * ph / dest.Height;
            if (sy >= ph) sy = ph - 1;
            for (int dx = 0; dx < dest.Width; dx++)
            {
                int sx = dx * pw / dest.Width;
                if (sx >= pw) sx = pw - 1;
                char ch = rows[sy][sx];
                if (!palette.TryGetValue(ch, out var col) || col.A == 0) continue;
                col = new Color(
                    (byte)(col.R * tint.R / 255),
                    (byte)(col.G * tint.G / 255),
                    (byte)(col.B * tint.B / 255),
                    col.A);
                BlendPixel(dest.X + dx, dest.Y + dy, col);
            }
        }
    }
}
