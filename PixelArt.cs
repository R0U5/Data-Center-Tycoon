using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DataCenterTycoon;

public enum Sprite
{
    GrassA, GrassB, WaterA, WaterB, Tree,
    HouseA, HouseB, Shed, HallA, HallB,
    CampusA, CampusB, Park, Raze
}

public class PixelArt
{
    public const int SpriteSize = 12;

    public Texture2D Atlas { get; }
    private readonly Dictionary<Sprite, Rectangle> _rects = new();

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.Transparent,
        ['g'] = new Color(56, 128, 68),
        ['G'] = new Color(76, 150, 84),
        ['h'] = new Color(44, 108, 56),
        ['w'] = new Color(36, 80, 172),
        ['W'] = new Color(70, 120, 214),
        ['t'] = new Color(92, 60, 36),
        ['l'] = new Color(28, 92, 44),
        ['L'] = new Color(52, 122, 58),
        ['r'] = new Color(168, 64, 50),
        ['R'] = new Color(196, 88, 66),
        ['a'] = new Color(196, 150, 100),
        ['d'] = new Color(92, 60, 36),
        ['y'] = new Color(250, 214, 110),
        ['s'] = new Color(120, 126, 142),
        ['S'] = new Color(164, 170, 186),
        ['k'] = new Color(58, 60, 74),
        ['K'] = new Color(40, 42, 54),
        ['1'] = new Color(90, 240, 130),
        ['2'] = new Color(255, 180, 60),
        ['c'] = new Color(110, 220, 255),
        ['p'] = new Color(198, 178, 124),
        ['f'] = new Color(238, 120, 160),
        ['Y'] = new Color(250, 220, 90),
        ['b'] = new Color(36, 104, 48),
        ['x'] = new Color(225, 70, 60),
        ['X'] = new Color(255, 120, 100),
    };

    private static readonly Dictionary<Sprite, string[]> Patterns = new()
    {
        [Sprite.GrassA] = new[]
        {
            "gggggggggggg",
            "ggGggggggghg",
            "gggggggGgggg",
            "gggggggggggg",
            "gGgggggggggg",
            "ggggggGggggg",
            "gggggggggghg",
            "ghgggggggggg",
            "ggggGggggggg",
            "gggggggggGgg",
            "gggggggggggg",
            "ggGgggghgggg",
        },
        [Sprite.GrassB] = new[]
        {
            "gggggggggggg",
            "gggggghGgggg",
            "gGgggggggggg",
            "ggggggggghgg",
            "gggghGgggggg",
            "gggggggggggg",
            "gGgggggGgggg",
            "gggggggggggg",
            "gghggggggGgg",
            "ggggGggggggg",
            "gggggggggggg",
            "ghgggggGgggg",
        },
        [Sprite.WaterA] = new[]
        {
            "wwwwwwwwwwww",
            "wWWwwwwwwwww",
            "wwwwwwwWWWww",
            "wwwwwwwwwwww",
            "wwwWWwwwwwww",
            "wwwwwwwwwWWw",
            "wWWwwwwwwwww",
            "wwwwwWWwwwww",
            "wwwwwwwwwwww",
            "wwWWwwwwwWWw",
            "wwwwwwWWwwww",
            "wwwwwwwwwwww",
        },
        [Sprite.WaterB] = new[]
        {
            "wwwwwwwWWwww",
            "wwwwwwwwwwww",
            "wWWwwwwwwwww",
            "wwwwwwWWWwww",
            "wwwwwwwwwwww",
            "wwWWwwwwwwww",
            "wwwwwwwwwWWw",
            "wwwwwwwwwwww",
            "wwwwWWwwwwww",
            "wwwwwwwwwwww",
            "wWWwwwwwWWww",
            "wwwwwwwwwwww",
        },
        [Sprite.Tree] = new[]
        {
            "gggggggggggg",
            "gggggllggggg",
            "ggggllllgggg",
            "ggglllLllggg",
            "gggllLlllggg",
            "gglllllLllgg",
            "ggllLlllllgg",
            "gggllllllggg",
            "ggggllllgggg",
            "gggggttggggg",
            "gggggttggggg",
            "ghgggggggGgg",
        },
        [Sprite.HouseA] = new[]
        {
            "gggggggggggg",
            "gggRrrrrrggg",
            "ggRrrrrrrrgg",
            "gRrrrrrrrrrg",
            "ggaaaaaaaagg",
            "ggayyaayyagg",
            "ggayyaayyagg",
            "ggaaaaaaaagg",
            "ggaaaddaaagg",
            "ggaaaddaaagg",
            "gggggggggggg",
            "gggggggggggg",
        },
        [Sprite.HouseB] = new[]
        {
            "gggggggggggg",
            "ggggRrrrgggg",
            "gggRrrrrrggg",
            "ggRrrrrrrrgg",
            "ggaaaaaaaagg",
            "ggayyaaaaagg",
            "ggayyaaddagg",
            "ggaaaaaddagg",
            "ggaaaaaaaagg",
            "ggaaaaaaaagg",
            "gggggggggggg",
            "gggggggggggg",
        },
        [Sprite.Shed] = new[]
        {
            "gggggggggggg",
            "gggggggggggg",
            "ggkkkkkkkkgg",
            "ggksSSSSskgg",
            "ggksSSSSskgg",
            "ggksssssskgg",
            "ggkss1ssskgg",
            "ggkssddsskgg",
            "ggkssddsskgg",
            "ggkkkkkkkkgg",
            "gggggggggggg",
            "ghgggggGgggg",
        },
        [Sprite.HallA] = new[]
        {
            "gggggggggggg",
            "gkkkkkkkkkkg",
            "gksssssssskg",
            "gksKKKKKKskg",
            "gksK1K2K1skg",
            "gksKKKKKKskg",
            "gksK2K1K2skg",
            "gksKKKKKKskg",
            "gksssssssskg",
            "gkkkkkkkkkkg",
            "gggggggggggg",
            "gggggggggggg",
        },
        [Sprite.HallB] = new[]
        {
            "gggggggggggg",
            "gkkkkkkkkkkg",
            "gksssssssskg",
            "gksKKKKKKskg",
            "gksK2K1K2skg",
            "gksKKKKKKskg",
            "gksK1K2K1skg",
            "gksKKKKKKskg",
            "gksssssssskg",
            "gkkkkkkkkkkg",
            "gggggggggggg",
            "gggggggggggg",
        },
        [Sprite.CampusA] = new[]
        {
            "KKKKKKKKKKKK",
            "KkkkkkkkkkkK",
            "Kk1kk2kk1kkK",
            "KkkkkkkkkkkK",
            "Kk2kk1kk2kkK",
            "KkkkkkkkkkkK",
            "Kk1kk2kk1kkK",
            "KkkkkkkkkkkK",
            "Kk2kk1kk2kkK",
            "KkkkkkkkkkkK",
            "KkkcckkcckkK",
            "KKKKKKKKKKKK",
        },
        [Sprite.CampusB] = new[]
        {
            "KKKKKKKKKKKK",
            "KkkkkkkkkkkK",
            "Kk2kk1kk2kkK",
            "KkkkkkkkkkkK",
            "Kk1kk2kk1kkK",
            "KkkkkkkkkkkK",
            "Kk2kk1kk2kkK",
            "KkkkkkkkkkkK",
            "Kk1kk2kk1kkK",
            "KkkkkkkkkkkK",
            "KkkcckkcckkK",
            "KKKKKKKKKKKK",
        },
        [Sprite.Park] = new[]
        {
            "gggggggggggg",
            "ggbbgggggfgg",
            "gbLLbggggggg",
            "ggbbggpppggg",
            "gggggppppggg",
            "ggggppgppggg",
            "gggppgggppgg",
            "ggfppgggppgg",
            "gggggggbbggg",
            "ggYggggbLLbg",
            "gggggggbbggg",
            "gggggggggggg",
        },
        [Sprite.Raze] = new[]
        {
            "............",
            ".xx......xx.",
            ".xXx....xXx.",
            "..xXx..xXx..",
            "...xXxxXx...",
            "....xXXx....",
            "....xXXx....",
            "...xXxxXx...",
            "..xXx..xXx..",
            ".xXx....xXx.",
            ".xx......xx.",
            "............",
        },
    };

    public PixelArt(GraphicsDevice device)
    {
        var sprites = (Sprite[])Enum.GetValues(typeof(Sprite));
        int texW = sprites.Length * SpriteSize;
        var data = new Color[texW * SpriteSize];
        foreach (var sprite in sprites)
        {
            var rows = Patterns[sprite];
            int ox = (int)sprite * SpriteSize;
            for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                    data[y * texW + ox + x] = Palette[rows[y][x]];
            _rects[sprite] = new Rectangle(ox, 0, SpriteSize, SpriteSize);
        }
        Atlas = new Texture2D(device, texW, SpriteSize);
        Atlas.SetData(data);
    }

    public Rectangle Src(Sprite sprite) => _rects[sprite];
}
