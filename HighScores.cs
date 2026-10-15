using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DataCenterTycoon;

public class Score
{
    public float Value;
    public int Day;
    public int Structures;
    public string Difficulty = "STANDARD";
    public string Outcome = "?";
}

public static class HighScores
{
    private static readonly string FileName = Path.Combine(AppContext.BaseDirectory, "scores.dat");
    private const int MaxEntries = 5;

    public static List<Score> Load()
    {
        var list = new List<Score>();
        try
        {
            if (!File.Exists(FileName)) return list;
            foreach (var line in File.ReadAllLines(FileName))
            {
                var p = line.Split('|');
                if (p.Length < 5) continue;
                if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (!int.TryParse(p[1], out var d)) continue;
                if (!int.TryParse(p[2], out var s)) continue;
                list.Add(new Score { Value = v, Day = d, Structures = s, Difficulty = p[3], Outcome = p[4] });
            }
        }
        catch { }
        return list.OrderByDescending(s => s.Value).Take(MaxEntries).ToList();
    }

    public static void Submit(Score score)
    {
        try
        {
            var list = Load();
            list.Add(score);
            var top = list.OrderByDescending(s => s.Value).Take(MaxEntries).ToList();
            var lines = top.Select(s => string.Join("|",
                s.Value.ToString("F0", CultureInfo.InvariantCulture), s.Day, s.Structures, s.Difficulty, s.Outcome));
            File.WriteAllLines(FileName, lines);
        }
        catch { }
    }

    public static bool IsHighScore(float value)
    {
        var list = Load();
        if (list.Count < MaxEntries) return true;
        return value > list[^1].Value;
    }
}
