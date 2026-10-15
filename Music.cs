using System;
using Microsoft.Xna.Framework.Audio;

namespace DataCenterTycoon;

public class Music
{
    private const int Rate = 22050;
    private readonly SoundEffectInstance _loop;
    private readonly bool _available;
    public bool Available => _available;

    public bool Muted
    {
        get => _loop == null || _loop.Volume <= 0.001f;
        set { try { if (_loop != null) _loop.Volume = value ? 0f : 0.22f; } catch { } }
    }

    public Music()
    {
        try
        {
            int[] scale = { 262, 294, 330, 349, 392, 440, 494, 523 };
            int stepMs = 300;
            var notes = new short[0];
            var buf = new System.Collections.Generic.List<short>();
            int[] melody = { 0, 2, 4, 7, 4, 2, 0, 2, 4, 5, 4, 2, 1, 0, 2, 4 };
            foreach (int m in melody)
            {
                buf.AddRange(Tone(scale[m], stepMs * 2, 0.5f));
                buf.AddRange(new short[Rate * stepMs / 1000 / 4]);
            }
            notes = buf.ToArray();
            var bytes = new byte[notes.Length * 2];
            Buffer.BlockCopy(notes, 0, bytes, 0, bytes.Length);
            var effect = new SoundEffect(bytes, Rate, AudioChannels.Mono);
            _loop = effect.CreateInstance();
            _loop.IsLooped = true;
            _loop.Volume = 0.22f;
            _loop.Play();
            _available = true;
        }
        catch
        {
            _loop = null;
            _available = false;
        }
    }

    private static short[] Tone(int freq, int ms, float amp)
    {
        int samples = Rate * ms / 1000;
        var data = new short[samples];
        int period = Math.Max(2, Rate / freq);
        int attack = Math.Min(64, samples / 4);
        int release = Math.Min(samples / 3, 500);
        for (int i = 0; i < samples; i++)
        {
            float env = 1f;
            if (i < attack) env = i / (float)attack;
            else if (i > samples - release) env = (samples - i) / (float)release;
            bool tri = (i % period) < period / 2;
            short v = (short)((tri ? 1 : -1) * 4500 * env * amp);
            data[i] = v;
        }
        return data;
    }
}
