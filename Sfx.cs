using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace DataCenterTycoon;

public enum Sound { Build, Demolish, Deny, Day, Select, Warn, Win, Lose }

public class Sfx
{
    private const int Rate = 22050;
    private readonly Dictionary<Sound, SoundEffect> _sounds = new();
    private readonly bool _available;
    public bool Muted { get; set; }

    public Sfx()
    {
        try
        {
            _sounds[Sound.Build] = Make(Seq((660, 40), (880, 60)));
            _sounds[Sound.Demolish] = Make(Seq((300, 50), (180, 80)));
            _sounds[Sound.Deny] = Make(Seq((140, 120)));
            _sounds[Sound.Day] = Make(Seq((523, 35), (659, 45)));
            _sounds[Sound.Select] = Make(Seq((990, 25)));
            _sounds[Sound.Warn] = Make(Seq((622, 90), (466, 90), (622, 90), (466, 90)));
            _sounds[Sound.Win] = Make(Seq((523, 90), (659, 90), (784, 90), (1047, 220)));
            _sounds[Sound.Lose] = Make(Seq((392, 130), (330, 130), (262, 130), (196, 320)));
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public void Play(Sound sound, float volume = 0.5f)
    {
        if (!_available || Muted) return;
        try { _sounds[sound].Play(volume, 0f, 0f); } catch { }
    }

    private static short[] Seq(params (float freq, int ms)[] notes)
    {
        var parts = new List<short[]>();
        foreach (var (freq, ms) in notes)
            parts.Add(Tone(freq, ms));
        int total = 0;
        foreach (var p in parts) total += p.Length;
        var all = new short[total];
        int offset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, all, offset, p.Length);
            offset += p.Length;
        }
        return all;
    }

    private static short[] Tone(float freq, int ms)
    {
        int samples = Rate * ms / 1000;
        var data = new short[samples];
        int period = Math.Max(2, (int)(Rate / freq));
        int attack = Math.Min(64, samples / 4);
        int release = Math.Min(samples / 3, 400);
        for (int i = 0; i < samples; i++)
        {
            float env = 1f;
            if (i < attack) env = i / (float)attack;
            else if (i > samples - release) env = (samples - i) / (float)release;
            short v = (short)(((i % period) < period / 2 ? 1 : -1) * 7000 * env);
            data[i] = v;
        }
        return data;
    }

    private static SoundEffect Make(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return new SoundEffect(bytes, Rate, AudioChannels.Mono);
    }
}
